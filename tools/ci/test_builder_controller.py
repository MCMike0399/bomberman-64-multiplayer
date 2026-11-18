import datetime
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import builder_controller as controller
import builder_guest
import builder_journal as journal
import builder_namespace as names


class Store:
    def __init__(self,run):self.run=run;self.bucket='owned';self.files={}
    def call(self,operation,*args):
        key=args[args.index('--key')+1]
        if operation=='put-object':self.files[key]=Path(args[-1]).read_bytes();return{}
        if key not in self.files:raise controller.builder_s3.ApiError('NoSuchKey')
        Path(args[-1]).write_bytes(self.files[key]);return{}


class PhaseWiring(unittest.TestCase):
    def setUp(self):
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup);self.root=Path(temporary.name)
        self.plan={'runId':'sancus-builder-20261030-120b573-1234567890abcdef','payloadSource':'a'*40,
                   'account':names.ACCOUNT,'region':names.REGION,'profile':'sancus','verificationRunId':'123456789',
                   'fixedCampaignExpiryUtc':(datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(hours=2,minutes=40)).isoformat(),
                   'staging':{'inputs':[],'manifest':{'sha256':'b'*64},'bootstrap':{},'awsCli':{}},
                   'instances':[{'os':'Windows','ami':'ami-windows','slot':slot,'lane':row['lane']}for slot,row in names.SLOTS.items()]}
        (self.root/'plan.json').write_text(json.dumps(self.plan));self.session=controller.Controller(self.root,operator=True)
        self.store=Store(self.plan['runId']);self.session.client=self.store
        # 2026-11-17: two parallel Windows lanes keyed by slot; both qualify windows-x64 only.
        self.ids={names.CORE_SLOT:'i-0000000000000c0e0',names.REMAINING_SLOT:'i-0000000000000aea0'}
        self.session.data['targets']={slot:{'instanceId':identity,'state':'booting','target':'windows-x64','lane':names.SLOTS[slot]['lane']}
                                      for slot,identity in self.ids.items()}
        self.session.data['bootstrapDeadline']=self.session.stop
        self.session.api=lambda *args:({'Account':names.ACCOUNT,'Arn':f'arn:aws:sts::{names.ACCOUNT}:assumed-role/{names.CI_ROLE}/test'}if args[0]=='sts'else{
            'InstanceInformationList':[{'InstanceId':identity,'PingStatus':'Online'}for identity in self.ids.values()]})

    def ready(self,target,**overrides):
        value={'schemaVersion':1,'runId':self.plan['runId'],'source':self.plan['payloadSource'],'account':names.ACCOUNT,
               'region':names.REGION,'target':'windows-x64','instanceId':self.ids[target],'payloadManifestSha256':'b'*64,
               'ready':True,'hardwareQualified':False,'user':'BlastQa','sessionId':1,**overrides}
        self.store.files[names.instance_object(self.ids[target],'bootstrap-ready.json')]=json.dumps(value).encode()

    def output(self,target,state,files=None):
        work=self.root/('guest-'+target+'-'+state);(work/'artifacts').mkdir(parents=True)
        (work/'artifacts/runtime.log').write_text('raw '+state)
        for relative,data in (files or{}).items():
            path=work/'artifacts'/relative;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)
        descriptor={'runId':self.plan['runId'],'source':self.plan['payloadSource'],'target':'windows-x64','slot':target,
                    'lane':names.SLOTS[target]['lane'],'account':names.ACCOUNT,'region':names.REGION,'instanceId':self.ids[target]}
        return builder_guest.publish(self.store,work,descriptor,set(),state)

    def core_files(self):
        return {relative:('{"core": "%s"}'%relative).encode()for relative in names.HANDOFF_FILES}

    def test_two_lane_core_handoff_final_and_cleanup_wiring(self):
        for target in self.ids:self.ready(target)
        self.output(names.CORE_SLOT,'finished',self.core_files())
        with (patch.object(controller.builder_ssm,'dispatch',return_value='command-id')as dispatch,
                patch.object(self.session,'owned_target',side_effect=lambda target:self.ids[target]),
                patch.object(controller,'new_event')):
            self.session.collect('core')
        # Both lanes are dispatched in parallel; the core phase ends once core is retained.
        self.assertEqual([call.args[4]for call in dispatch.call_args_list],[names.CORE_SLOT,names.REMAINING_SLOT])
        self.assertEqual(self.session.data['targets'][names.CORE_SLOT]['state'],'finished')
        self.assertEqual(self.session.data['targets'][names.REMAINING_SLOT]['state'],'running')
        with patch.object(self.session,'stop_target')as stop:self.session.acknowledge()
        stop.assert_called_once_with(names.CORE_SLOT)  # The idle core instance is terminated early.
        receipt=json.loads(self.store.files[names.HANDOFF_RECEIPT_KEY])
        self.assertEqual((receipt['coreInstanceId'],receipt['remainingInstanceId']),(self.ids[names.CORE_SLOT],self.ids[names.REMAINING_SLOT]))
        self.assertEqual(receipt['coreManifestSha256'],self.session.data['targets'][names.CORE_SLOT]['retention']['rawManifestSha256'])
        for row in receipt['files']:
            self.assertEqual(self.store.files[row['objectKey']],self.core_files()[row['path']])
        self.assertEqual(self.session.data['coreHandoffSha256'],hashlib.sha256(self.store.files[names.HANDOFF_RECEIPT_KEY]).hexdigest())
        with patch.object(self.session,'stop_target')as stop:self.session.acknowledge()  # Resumable, never republished differently.
        self.output(names.REMAINING_SLOT,'finished')
        with patch.object(self.session,'verify_games',return_value={})as verify,patch.object(controller,'new_event'):
            self.session.collect('final');verify.assert_called_once()
        with patch.object(controller.supervisor,'cleanup_owned',return_value={'cleanupComplete':True,'evidenceComplete':True})as cleanup:
            self.assertTrue(self.session.cleanup()['cleanupComplete']);cleanup.assert_called_once()
        self.assertTrue(all(row['state']=='finished'for row in self.session.data['targets'].values()))
        self.assertTrue((self.root/'evidence'/names.REMAINING_SLOT/'finished/retention.json').is_file())

    def test_no_handoff_before_the_core_lane_is_finished_or_after_the_remaining_lane_failed(self):
        with self.assertRaisesRegex(ValueError,'core lane must be finished'):self.session.acknowledge()
        self.session.data['targets'][names.CORE_SLOT]['state']='finished'
        for state in ('failed','stopped','booting'):
            self.session.data['targets'][names.REMAINING_SLOT]['state']=state
            with self.subTest(state=state),self.assertRaisesRegex(ValueError,'nothing can qualify'):self.session.acknowledge()
        self.assertFalse(any(key.startswith('payload/core-handoff')for key in self.store.files))

    def test_a_changed_handoff_receipt_is_never_republished(self):
        self.session.data['targets'][names.CORE_SLOT]['state']='finished'
        self.session.data['targets'][names.REMAINING_SLOT]['state']='running'
        (self.root/'core-handoff.json').write_text('{"other": true}');self.session.data['coreHandoffSha256']='0'*64
        with self.assertRaisesRegex(ValueError,'earlier core handoff changed'):self.session.acknowledge()
        (self.root/'core-handoff.json').unlink()
        with self.assertRaisesRegex(ValueError,'earlier core handoff changed'):self.session.acknowledge()
        self.assertNotIn(names.HANDOFF_RECEIPT_KEY,self.store.files)

    def test_core_phase_waits_for_the_remaining_lane_dispatch_and_final_waits_for_both(self):
        self.session.data['targets'][names.CORE_SLOT]['state']='running'
        self.session.data['targets'][names.REMAINING_SLOT]['state']='booting'
        self.output(names.CORE_SLOT,'finished',self.core_files())
        clock=[self.session.stop-100]
        def tick():clock[0]+=10;return clock[0]
        self.session.data['bootstrapDeadline']=self.session.stop+60
        with (patch.object(controller.time,'time',side_effect=tick),patch.object(controller,'new_event'),
              patch.object(self.session,'observe_bootstrap',return_value=False)as observed):
            with self.assertRaisesRegex(TimeoutError,'Fixed cleanup boundary'):self.session.collect('core')
        self.assertGreater(observed.call_count,1)
        self.assertEqual(self.session.data['targets'][names.CORE_SLOT]['state'],'finished')
        self.session.data['targets'][names.REMAINING_SLOT]['state']='running';clock[0]=self.session.stop-100
        with (patch.object(controller.time,'time',side_effect=tick),patch.object(controller,'new_event'),
              patch.object(self.session,'verify_games')as verify):
            with self.assertRaisesRegex(TimeoutError,'Fixed cleanup boundary'):self.session.collect('final')
        verify.assert_not_called()

    def test_one_failed_lane_stops_the_other_lane_at_once(self):
        for slot in self.ids:self.session.data['targets'][slot]['state']='running'
        self.output(names.REMAINING_SLOT,'failed')
        with patch.object(self.session,'stop_target')as stop,patch.object(controller,'new_event'):
            with self.assertRaisesRegex(RuntimeError,'other lane was stopped'):self.session.collect('core')
        self.assertEqual([call.args[0]for call in stop.call_args_list],[names.REMAINING_SLOT,names.CORE_SLOT])
        rows=self.session.data['targets']
        self.assertEqual((rows[names.REMAINING_SLOT]['state'],rows[names.CORE_SLOT]['state']),('failed','stopped'))
        self.assertTrue((self.root/'evidence'/names.REMAINING_SLOT/'failed/retention.json').is_file())
        with patch.object(self.session,'stop_target'),patch.object(controller,'new_event'):
            with self.assertRaisesRegex(RuntimeError,'already failed'):self.session.collect('final')

    def test_peer_termination_errors_are_recorded_without_hiding_the_lane_failure(self):
        for slot in self.ids:self.session.data['targets'][slot]['state']='running'
        self.output(names.CORE_SLOT,'failed')
        def stop(slot):
            if slot==names.REMAINING_SLOT:raise ValueError('not running')
        with patch.object(self.session,'stop_target',side_effect=stop),patch.object(controller,'new_event'):
            with self.assertRaisesRegex(RuntimeError,'Lane windows-x64-core failed'):self.session.collect('core')
        self.assertIn({'target':names.REMAINING_SLOT,'error':'not running','scope':'peer-termination'},self.session.data['errors'])

    def test_final_verification_requires_both_lanes_and_the_published_handoff(self):
        with self.assertRaisesRegex(ValueError,'Every complete'):self.session.verify_games()
        for slot in self.ids:self.session.data['targets'][slot]['state']='finished'
        with self.assertRaisesRegex(ValueError,'handoff is required'):self.session.verify_games()
        (self.root/'core-handoff.json').write_text('{}');self.session.data['coreHandoffSha256']='0'*64
        with self.assertRaisesRegex(ValueError,'changed after publication'):self.session.verify_games()
        self.session.data['coreHandoffSha256']=hashlib.sha256(b'{}').hexdigest()
        with patch.object(controller.transfer,'verify_lanes',side_effect=ValueError('not byte-identical'))as lanes:
            with self.assertRaisesRegex(ValueError,'byte-identical'):self.session.verify_games()
        self.assertEqual(lanes.call_args.args[2:4],({},hashlib.sha256(b'{}').hexdigest()))

    def test_wrong_producer_or_system_session_never_dispatches(self):
        for changes in ({'instanceId':'i-protected'},{'user':'SYSTEM'},{'sessionId':0}):
            self.ready(names.REMAINING_SLOT,**changes)
            with patch.object(controller.builder_ssm,'dispatch')as dispatch,self.assertRaises(ValueError):
                self.session.observe_bootstrap(names.REMAINING_SLOT)
            dispatch.assert_not_called()

    def test_console_failure_marker_fails_target_fast_and_retains_console(self):
        console={'text':'cloud-init running'};calls=[]
        def api(*args):
            calls.append(args[:2])
            if args[0]=='sts':return {'Account':names.ACCOUNT}
            if args[:2]==('ec2','get-console-output'):return {'Output':console['text']}
            return {'InstanceInformationList':[]}
        self.session.api=api;clock=[1000.0]
        with patch.object(controller.time,'time',side_effect=lambda:clock[0]):
            self.assertFalse(self.session.observe_bootstrap(names.REMAINING_SLOT))
            self.assertFalse(self.session.observe_bootstrap(names.REMAINING_SLOT))  # Rate-limited.
            self.assertEqual(calls.count(('ec2','get-console-output')),1)
            clock[0]+=controller.CONSOLE_SAMPLE_SECONDS
            console['text']='x\nSANCUS-BUILDER-BOOTSTRAP-FAILED status=100 line=12\ntail'
            with self.assertRaisesRegex(RuntimeError,'bootstrap reported failure'):
                self.session.observe_bootstrap(names.REMAINING_SLOT)
        row=self.session.data['targets'][names.REMAINING_SLOT]
        self.assertIn('SANCUS-BUILDER-BOOTSTRAP-FAILED',row['consoleMarkersSeen'])
        retained=(self.root/'evidence'/names.REMAINING_SLOT/'console'/'bootstrap-failed.txt').read_text()
        self.assertIn('status=100',retained)
        self.assertEqual(row['consoleSamples'][-1]['sha256'],__import__('hashlib').sha256(retained.encode()).hexdigest())

    def test_console_read_errors_are_recorded_never_fatal_and_markers_are_recorded(self):
        outputs=iter([RuntimeError('ec2/get-console-output: AccessDenied'),{'Output':'SANCUS-BUILDER-BOOTSTRAP-SYSTEM-READY audio gate passed'}])
        def api(*args):
            if args[0]=='sts':return {'Account':names.ACCOUNT}
            if args[:2]==('ec2','get-console-output'):
                value=next(outputs)
                if isinstance(value,Exception):raise value
                return value
            return {'InstanceInformationList':[]}
        self.session.api=api;clock=[1000.0]
        with patch.object(controller.time,'time',side_effect=lambda:clock[0]):
            self.assertFalse(self.session.observe_bootstrap(names.REMAINING_SLOT))
            clock[0]+=controller.CONSOLE_SAMPLE_SECONDS
            self.assertFalse(self.session.observe_bootstrap(names.REMAINING_SLOT))
        row=self.session.data['targets'][names.REMAINING_SLOT]
        self.assertIn('AccessDenied',row['consoleSamples'][0]['error'])
        self.assertEqual(row['consoleMarkersSeen'],['SANCUS-BUILDER-BOOTSTRAP-SYSTEM-READY'])

    def test_bootstrap_wait_retains_bounded_ping_samples_and_first_ready_time(self):
        statuses=iter(['error','missing']+['ConnectionLost']*60)
        def api(*args):
            if args[0]=='sts':return {'Account':names.ACCOUNT}
            if args[:2]==('ec2','get-console-output'):return {'Output':'booting'}
            status=next(statuses,'Online')
            if status=='error':raise RuntimeError('ssm/describe-instance-information: throttled')
            if status=='missing':return {'InstanceInformationList':[]}
            return {'InstanceInformationList':[{'InstanceId':self.ids[names.REMAINING_SLOT],'PingStatus':status}]}
        self.session.api=api;clock=[1000.0]
        with patch.object(controller.time,'time',side_effect=lambda:clock[0]):
            for _ in range(controller.PING_SAMPLE_LIMIT+10):
                self.assertFalse(self.session.observe_bootstrap(names.REMAINING_SLOT))
                self.assertFalse(self.session.observe_bootstrap(names.REMAINING_SLOT))  # Rate-limited: no extra API call.
                clock[0]+=controller.PING_SAMPLE_SECONDS
            row=self.session.data['targets'][names.REMAINING_SLOT]
            self.assertEqual(row['pingSampleCount'],controller.PING_SAMPLE_LIMIT+10)
            self.assertEqual(len(row['pingSamples']),controller.PING_SAMPLE_LIMIT)
            self.assertIn('throttled',row['pingSamples'][0]['error'])  # First observation retained.
            self.assertEqual(row['pingSamples'][-1]['pingStatus'],'ConnectionLost')
            self.assertNotIn('readyAtUtc',row)
            statuses=iter([]);self.ready(names.REMAINING_SLOT)
            with (patch.object(controller.builder_ssm,'dispatch',return_value='command-id')as dispatch,
                  patch.object(self.session,'owned_target',return_value=self.ids[names.REMAINING_SLOT])):
                self.assertTrue(self.session.observe_bootstrap(names.REMAINING_SLOT))
            dispatch.assert_called_once()
        saved=json.loads((self.root/'controller.json').read_text())['targets'][names.REMAINING_SLOT]
        self.assertTrue(saved['readyAtUtc'].endswith('+00:00'))
        self.assertEqual(saved['pingSamples'][-1]['pingStatus'],'Online')
        self.assertEqual(saved['pingSampleCount'],controller.PING_SAMPLE_LIMIT+11)

    def test_the_campaign_has_exactly_one_windows_target_and_two_lane_slots(self):
        # 2026-11-13: the Linux AWS builder was removed; Linux qualifies on the owned workstation.
        self.assertEqual(controller.TARGETS,{'windows-x64':'Windows'})
        self.assertIs(controller.TARGETS,names.TARGETS)
        self.assertEqual(list(controller.SLOTS),['windows-x64-core','windows-x64-remaining'])

    def test_bootstrap_deadline_uses_the_reviewed_window(self):
        self.assertEqual(controller.builder_timing.BOOTSTRAP_SECONDS,900)
        self.assertEqual(self.session.stop,self.session.expiry-20*60)

    def test_changed_checkpoint_is_not_accepted_again(self):
        target=names.CORE_SLOT;manifest=self.output(target,'finished')
        observed=self.root/'observations'/target;observed.mkdir(parents=True)
        (observed/'manifest.json').write_text(json.dumps(manifest))
        self.session.retain(target,manifest)
        manifest['files'][0]['sha256']='e'*64
        with self.assertRaisesRegex(ValueError,'checkpoint changed'):self.session.retain(target,manifest)

    def test_bootstrap_only_evidence_is_never_called_game_results(self):
        target=self.root/'observations/ready.json';target.parent.mkdir();target.write_text('{}')
        with (patch.object(controller.campaign,'stack_entry',return_value={'id':'owned-stack'}),
                patch.object(self.session,'verify_games',side_effect=ValueError('not run'))):
            result=self.session.retain_evidence()
        self.assertEqual(result['scope'],'failure-only');self.assertNotEqual(result['verdict'],'pass')

    def test_launch_requires_published_intent_before_any_allocation(self):
        self.session.operator=False
        row=journal.append(controller.campaign.JOURNAL,self.plan,'INTENT','AWS::CloudFormation::Stack',self.plan['runId'],{})
        with patch.object(controller,'verify_context'),patch.object(controller.campaign,'submit_intent')as submit:
            with self.assertRaises(FileNotFoundError):self.session.launch()
            submit.assert_not_called()

    def test_launch_wires_exact_stack_observation_arming_and_staging_in_order(self):
        journal.append(controller.campaign.JOURNAL,self.plan,'INTENT','AWS::CloudFormation::Stack',self.plan['runId'],{})
        calls=[]
        logical={slot:{'id':identity,'details':{'logicalId':names.slot_logical(slot,'Instance')}}for slot,identity in self.ids.items()}
        def wait(*args,**kwargs):kwargs['observe']();calls.append('wait-complete')
        with (patch.object(controller,'verify_context'),
              patch.object(controller.campaign,'submit_intent',side_effect=lambda plan:calls.append('submit')or'stack'),
              patch.object(controller.campaign,'wait_stack',side_effect=wait),
              patch.object(controller.campaign,'sync',side_effect=lambda *args,**kwargs:calls.append('inventory')),
              patch.object(controller.arming,'set_schedule_auto_delete',side_effect=lambda plan:calls.append('auto-delete')),
              patch.object(controller.arming,'verify_live',side_effect=lambda plan:calls.append('armed')or{'runtimeCleanupArmed':True}),
              patch.object(controller.transfer,'upload_inputs',side_effect=lambda *args:calls.append('inputs')),
              patch.object(controller.transfer,'publish_bootstrap',side_effect=lambda *args:calls.append('bootstrap-last')),
              patch.object(controller.journal,'effective_created',return_value=logical)):
            result=self.session.launch()
        self.assertTrue(result['runtimeCleanupArmed'])
        self.assertEqual(calls,['submit','inventory','wait-complete','inventory','auto-delete','armed','inputs','bootstrap-last'])
        self.assertEqual({slot:row['instanceId']for slot,row in self.session.data['targets'].items()},self.ids)
        self.assertEqual({slot:row['lane']for slot,row in self.session.data['targets'].items()},
                         {names.CORE_SLOT:'core',names.REMAINING_SLOT:'remaining'})

    def test_final_phase_cannot_substitute_finished_flags_for_receipt_validation(self):
        for target in self.ids:
            self.session.data['targets'][target]['state']='running';self.output(target,'finished')
        with patch.object(self.session,'verify_games',side_effect=ValueError('GPU receipt hashes differ')),patch.object(controller,'new_event'):
            with self.assertRaisesRegex(ValueError,'hashes differ'):self.session.collect('final')

    def test_early_worker_exit_zero_is_still_failure_without_the_required_receipt(self):
        target=names.CORE_SLOT
        for slot in self.ids:self.session.data['targets'][slot]['state']='running'
        outcome={'runId':self.plan['runId'],'source':self.plan['payloadSource'],'account':names.ACCOUNT,
                 'region':names.REGION,'instanceId':self.ids[target],'target':'windows-x64','exitCode':0}
        self.store.files[names.instance_object(self.ids[target],'worker-exit.json')]=json.dumps(outcome).encode()
        with patch.object(self.session,'stop_target')as stop,patch.object(controller,'new_event'):
            with self.assertRaisesRegex(RuntimeError,'failed'):self.session.collect('core')
        self.assertEqual(stop.call_args_list[0].args,(target,))
        self.assertIn('required complete receipt',self.session.data['targets'][target]['error'])

    def test_tag_alone_does_not_authorize_terminating_an_unjournaled_instance(self):
        with (patch.object(controller.campaign,'authorize',return_value='owned-stack'),
              patch.object(controller.campaign,'rows',return_value=[]),
              patch.object(controller.builder_ssm,'verify_instance')as metadata,
              patch.object(self.session,'api')as api):
            with self.assertRaisesRegex(ValueError,'journaled stack member'):self.session.stop_target(names.CORE_SLOT)
        metadata.assert_not_called();api.assert_not_called()

    def test_account_match_alone_does_not_prove_the_hosted_role(self):
        self.session.operator=False
        with patch.object(self.session,'api',return_value={'Account':names.ACCOUNT,'Arn':f'arn:aws:iam::{names.ACCOUNT}:user/operator'}):
            with self.assertRaisesRegex(ValueError,'scoped builder role'):self.session.check_actor()

    def test_skipped_allocation_step_still_runs_actual_namespace_audit(self):
        context={'controllerRunId':'123','controllerRunAttempt':'1','controllerJobId':'campaign'}
        self.session.plan.update(context)
        environment={'GITHUB_RUN_ID':'123','GITHUB_RUN_ATTEMPT':'1','GITHUB_JOB':'campaign','BUILDER_LAUNCH_OUTCOME':'skipped'}
        audit={'passed':True,'checked':{'CFN':{'state':'absent'}},'leftovers':[],'errors':[]}
        with (patch.dict(controller.os.environ,environment),patch.object(controller.builder_audit,'audit',return_value=audit)as observed,
              patch.object(controller.supervisor,'cleanup_owned')as paid_cleanup):
            result=self.session.cleanup()
        observed.assert_called_once();paid_cleanup.assert_not_called()
        self.assertTrue(result['cleanupComplete']);self.assertFalse(result['allocationAttempted'])

    def test_recovered_intent_is_never_treated_as_a_never_submitted_request(self):
        self.session.plan.update(controllerRunId='123',controllerRunAttempt='1',controllerJobId='campaign')
        (self.root/'state-restored.json').write_text('{}')
        environment={'GITHUB_RUN_ID':'123','GITHUB_RUN_ATTEMPT':'1','GITHUB_JOB':'campaign','BUILDER_LAUNCH_OUTCOME':'failure'}
        with (patch.dict(controller.os.environ,environment),patch.object(controller.supervisor,'cleanup_owned',return_value={'cleanupComplete':False})as recovery,
              patch.object(controller.builder_audit,'audit')as shortcut):
            self.session.cleanup()
        recovery.assert_called_once();shortcut.assert_not_called()


if __name__=='__main__':unittest.main()


class PaidApprovalAllocationBoundary(unittest.TestCase):
    setUp = PhaseWiring.setUp
    def test_late_environment_approval_never_submits_creation(self):
        import os
        self.session.operator=False
        self.session.plan['fixedCampaignExpiryUtc']=(datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(minutes=30)).isoformat()
        spending={'budgetUsd':25,'closedEstimateUsd':4.80}
        self.session.plan['approvalPlan']=controller.builder_approval.build(self.plan['payloadSource'],self.plan['verificationRunId'],self.session.plan['fixedCampaignExpiryUtc'],spending)
        row=journal.append(controller.campaign.JOURNAL,self.session.plan,'INTENT','AWS::CloudFormation::Stack',self.plan['runId'],{})
        (self.root/'intent-published.json').write_text(json.dumps({'intentSha256':journal.canonical_hash(row),'controllerRunId':'fixture-run'}))
        with patch.dict(os.environ,{'GITHUB_RUN_ID':'fixture-run','SANCUS_BUILDER_PAID_HOLD':'false'}),patch.object(controller,'verify_context'),patch.object(controller.builder_approval,'ledger',return_value=spending),patch.object(controller.campaign,'submit_intent')as submit:
            with self.assertRaisesRegex(ValueError,'Late approval'):self.session.launch()
            submit.assert_not_called()

    def test_held_job_never_submits_creation(self):
        import os
        self.session.operator=False
        row=journal.append(controller.campaign.JOURNAL,self.plan,'INTENT','AWS::CloudFormation::Stack',self.plan['runId'],{})
        (self.root/'intent-published.json').write_text(json.dumps({'intentSha256':journal.canonical_hash(row),'controllerRunId':'fixture-run'}))
        with patch.dict(os.environ,{'GITHUB_RUN_ID':'fixture-run','SANCUS_BUILDER_PAID_HOLD':'true'}),patch.object(controller,'verify_context'),patch.object(controller.campaign,'submit_intent')as submit:
            with self.assertRaisesRegex(ValueError,'hold'):self.session.launch()
            submit.assert_not_called()


class ReceiptPolls(unittest.TestCase):
    def test_receipt_poll_samples_404_and_fails_at_once_on_403(self):
        from types import SimpleNamespace
        import builder_s3
        def owner(code):
            def call(operation,*arguments):raise builder_s3.ApiError(code)
            return SimpleNamespace(client=SimpleNamespace(call=call),data={})
        with tempfile.TemporaryDirectory()as root:
            pending=owner('NoSuchKey')
            self.assertIsNone(controller.Controller.read_object(pending,'results/windows-x64/worker-exit.json',Path(root)/'a.json'))
            self.assertEqual(pending.data['objectPollSamples']['results/windows-x64/worker-exit.json'][0]['code'],'NoSuchKey')
            with self.assertRaises(builder_s3.ApiError):
                controller.Controller.read_object(owner('AccessDenied'),'results/windows-x64/worker-exit.json',Path(root)/'b.json')

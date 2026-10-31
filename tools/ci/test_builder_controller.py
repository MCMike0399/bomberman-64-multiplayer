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
                   'instances':[{'os':'Linux','ami':'ami-linux'},{'os':'Windows','ami':'ami-windows'}]}
        (self.root/'plan.json').write_text(json.dumps(self.plan));self.session=controller.Controller(self.root,operator=True)
        self.store=Store(self.plan['runId']);self.session.client=self.store
        self.ids={'linux-x64':'i-00000000000000001','windows-x64':'i-00000000000000002'}
        self.session.data['targets']={target:{'instanceId':identity,'state':'booting'}for target,identity in self.ids.items()}
        self.session.data['bootstrapDeadline']=self.session.stop
        self.session.api=lambda *args:({'Account':names.ACCOUNT,'Arn':f'arn:aws:sts::{names.ACCOUNT}:assumed-role/{names.CI_ROLE}/test'}if args[0]=='sts'else{
            'InstanceInformationList':[{'InstanceId':identity,'PingStatus':'Online'}for identity in self.ids.values()]})

    def ready(self,target,**overrides):
        value={'schemaVersion':1,'runId':self.plan['runId'],'source':self.plan['payloadSource'],'account':names.ACCOUNT,
               'region':names.REGION,'target':target,'instanceId':self.ids[target],'payloadManifestSha256':'b'*64,
               'ready':True,'hardwareQualified':False,'user':'BlastQa'if target=='windows-x64'else'ubuntu',
               'sessionId':1 if target=='windows-x64'else None,**overrides}
        self.store.files['results/'+target+'/bootstrap-ready.json']=json.dumps(value).encode()

    def output(self,target,state):
        work=self.root/('guest-'+target+'-'+state);(work/'artifacts').mkdir(parents=True)
        (work/'artifacts/runtime.log').write_text('raw '+state)
        descriptor={'runId':self.plan['runId'],'source':self.plan['payloadSource'],'target':target,
                    'account':names.ACCOUNT,'region':names.REGION,'instanceId':self.ids[target]}
        return builder_guest.publish(self.store,work,descriptor,set(),state)

    def test_core_upload_ack_final_and_cleanup_wiring(self):
        for target in self.ids:self.ready(target);self.output(target,'core-retained')
        with (patch.object(controller.builder_ssm,'dispatch',return_value='command-id')as dispatch,
                patch.object(self.session,'owned_target',side_effect=lambda target:self.ids[target]),
                patch.object(controller.threading,'Event')):
            self.session.collect('core')
        self.assertEqual(dispatch.call_count,2)
        self.session.acknowledge()
        for target in self.ids:
            self.assertIn('payload/core-retained-'+target+'.json',self.store.files)
            self.output(target,'finished')
        with patch.object(self.session,'verify_games',return_value={})as verify,patch.object(controller.threading,'Event'):
            self.session.collect('final');verify.assert_called_once()
        with patch.object(controller.supervisor,'cleanup_owned',return_value={'cleanupComplete':True,'evidenceComplete':True})as cleanup:
            self.assertTrue(self.session.cleanup()['cleanupComplete']);cleanup.assert_called_once()
        self.assertTrue(all(row['state']=='finished'for row in self.session.data['targets'].values()))

    def test_no_acknowledgement_for_partial_core(self):
        with self.assertRaisesRegex(ValueError,'Both committed'):self.session.acknowledge()
        self.assertFalse(any('core-retained-'in key for key in self.store.files))

    def test_wrong_producer_or_system_session_never_dispatches(self):
        for changes in ({'instanceId':'i-protected'},{'user':'SYSTEM'},{'sessionId':0}):
            self.ready('windows-x64',**changes)
            with patch.object(controller.builder_ssm,'dispatch')as dispatch,self.assertRaises(ValueError):
                self.session.observe_bootstrap('windows-x64')
            dispatch.assert_not_called()

    def test_bootstrap_wait_retains_bounded_ping_samples_and_first_ready_time(self):
        statuses=iter(['error','missing']+['ConnectionLost']*60)
        def api(*args):
            if args[0]=='sts':return {'Account':names.ACCOUNT}
            status=next(statuses,'Online')
            if status=='error':raise RuntimeError('ssm/describe-instance-information: throttled')
            if status=='missing':return {'InstanceInformationList':[]}
            return {'InstanceInformationList':[{'InstanceId':self.ids['linux-x64'],'PingStatus':status}]}
        self.session.api=api;clock=[1000.0]
        with patch.object(controller.time,'time',side_effect=lambda:clock[0]):
            for _ in range(controller.PING_SAMPLE_LIMIT+10):
                self.assertFalse(self.session.observe_bootstrap('linux-x64'))
                self.assertFalse(self.session.observe_bootstrap('linux-x64'))  # Rate-limited: no extra API call.
                clock[0]+=controller.PING_SAMPLE_SECONDS
            row=self.session.data['targets']['linux-x64']
            self.assertEqual(row['pingSampleCount'],controller.PING_SAMPLE_LIMIT+10)
            self.assertEqual(len(row['pingSamples']),controller.PING_SAMPLE_LIMIT)
            self.assertIn('throttled',row['pingSamples'][0]['error'])  # First observation retained.
            self.assertEqual(row['pingSamples'][-1]['pingStatus'],'ConnectionLost')
            self.assertNotIn('readyAtUtc',row)
            statuses=iter([]);self.ready('linux-x64')
            with (patch.object(controller.builder_ssm,'dispatch',return_value='command-id')as dispatch,
                  patch.object(self.session,'owned_target',return_value=self.ids['linux-x64'])):
                self.assertTrue(self.session.observe_bootstrap('linux-x64'))
            dispatch.assert_called_once()
        saved=json.loads((self.root/'controller.json').read_text())['targets']['linux-x64']
        self.assertTrue(saved['readyAtUtc'].endswith('+00:00'))
        self.assertEqual(saved['pingSamples'][-1]['pingStatus'],'Online')
        self.assertEqual(saved['pingSampleCount'],controller.PING_SAMPLE_LIMIT+11)

    def test_bootstrap_deadline_uses_the_reviewed_window(self):
        self.assertEqual(controller.builder_timing.BOOTSTRAP_SECONDS,900)
        self.assertEqual(self.session.stop,self.session.expiry-20*60)

    def test_failed_target_is_retained_and_terminated_without_discarding_other_core(self):
        for target in self.ids:
            self.session.data['targets'][target]['state']='running-core'
            self.output(target,'failed'if target=='windows-x64'else'core-retained')
        with patch.object(self.session,'stop_target')as stop,patch.object(controller.threading,'Event'):
            with self.assertRaisesRegex(RuntimeError,'target failed'):self.session.collect('core')
        stop.assert_called_once_with('windows-x64')
        self.assertEqual(self.session.data['targets']['linux-x64']['state'],'core-retained')
        self.assertTrue((self.root/'evidence/windows-x64/failed/retention.json').is_file())

    def test_changed_checkpoint_is_not_accepted_again(self):
        target='linux-x64';manifest=self.output(target,'core-retained')
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
        logical={target:{'id':identity,'details':{'logicalId':controller.TARGETS[target]+'Instance'}}for target,identity in self.ids.items()}
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
        self.assertEqual(self.session.data['targets']['windows-x64']['instanceId'],self.ids['windows-x64'])

    def test_final_phase_cannot_substitute_finished_flags_for_receipt_validation(self):
        for target in self.ids:
            self.session.data['targets'][target]['state']='running-remaining';self.output(target,'finished')
        with patch.object(self.session,'verify_games',side_effect=ValueError('GPU receipt hashes differ')),patch.object(controller.threading,'Event'):
            with self.assertRaisesRegex(ValueError,'hashes differ'):self.session.collect('final')

    def test_early_worker_exit_zero_is_still_failure_without_the_required_receipt(self):
        target='linux-x64';self.session.data['targets']['windows-x64']['state']='failed'
        self.session.data['targets'][target]['state']='running-core'
        outcome={'runId':self.plan['runId'],'source':self.plan['payloadSource'],'account':names.ACCOUNT,
                 'region':names.REGION,'instanceId':self.ids[target],'target':target,'exitCode':0}
        self.store.files['results/'+target+'/worker-exit.json']=json.dumps(outcome).encode()
        with patch.object(self.session,'stop_target')as stop,patch.object(controller.threading,'Event'):
            with self.assertRaisesRegex(RuntimeError,'target failed'):self.session.collect('core')
        stop.assert_called_once_with(target)
        self.assertIn('required complete receipt',self.session.data['targets'][target]['error'])

    def test_tag_alone_does_not_authorize_terminating_an_unjournaled_instance(self):
        with (patch.object(controller.campaign,'authorize',return_value='owned-stack'),
              patch.object(controller.campaign,'rows',return_value=[]),
              patch.object(controller.builder_ssm,'verify_instance')as metadata,
              patch.object(self.session,'api')as api):
            with self.assertRaisesRegex(ValueError,'journaled stack member'):self.session.stop_target('linux-x64')
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

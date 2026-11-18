import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import builder_guest as guest
import builder_transfer


class GuestProtocol(unittest.TestCase):
    def setUp(self):
        self.task={'runId':'sancus-builder-20261030-8762d11-1234567890abcdef','source':'a'*40,
                   'account':'524329886851','region':'us-east-1','target':'windows-x64','instanceId':'i-owned'}
        self.manifest={'schemaVersion':1,**{key:self.task[key]for key in ('runId','source','account','region')},'files':[
            {'role':'source','target':'all','objectKey':'payload/input-000.bin','fileName':'source.zip','sha256':'b'*64,'size':10},
            {'role':'artifact','target':'windows-x64','objectKey':'payload/input-001.bin','fileName':'package.zip','sha256':'c'*64,'size':20}]}

    def test_target_selection_is_data_only_and_requires_actual_source_and_package(self):
        self.assertEqual(len(guest.validate_inputs(self.manifest,self.task)),2)
        manifest=copy.deepcopy(self.manifest);manifest['files'][1]['target']='all'
        manifest['files'][1]['role']='godot'
        with self.assertRaisesRegex(ValueError,'package artifacts'):guest.validate_inputs(manifest,self.task)
        # Windows only since 2026-11-13: a Linux payload row is not a reviewed target.
        manifest=copy.deepcopy(self.manifest);manifest['files'][1]['target']='linux-x64'
        with self.assertRaisesRegex(ValueError,'target'):guest.validate_inputs(manifest,self.task)

    def test_wrong_identity_role_path_duplicate_or_hash_refused(self):
        for key,value in [('role','execute-shell'),('fileName','../script'),('objectKey','results/overwrite'),('sha256','bad')]:
            manifest=copy.deepcopy(self.manifest);manifest['files'][0][key]=value
            with self.subTest(key=key),self.assertRaises(ValueError):guest.validate_inputs(manifest,self.task)
        manifest=copy.deepcopy(self.manifest);manifest['source']='d'*40
        with self.assertRaisesRegex(ValueError,'identity'):guest.validate_inputs(manifest,self.task)
        manifest=copy.deepcopy(self.manifest);manifest['files'].append(manifest['files'][0])
        with self.assertRaises(ValueError):guest.validate_inputs(manifest,self.task)

    def lane(self,slot='windows-x64-remaining'):
        return {**self.task,'slot':slot,'lane':guest.names.SLOTS[slot]['lane'],'instanceId':'i-0000000000000aea0'}

    def test_publish_keeps_raw_evidence_excludes_inputs_and_validates_for_host_retention(self):
        calls=[]
        class Client:
            def call(self,operation,*args):calls.append((operation,args))
        task=self.lane()
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);artifacts=root/'artifacts';artifacts.mkdir()
            (artifacts/'case').mkdir();(artifacts/'case/runtime.log').write_text('actual error retained')
            (artifacts/'case/capture.png').write_bytes(b'raw image bytes')
            (artifacts/'package.zip').write_bytes(b'input must not upload')
            (artifacts/'metadata.json').write_text('input metadata must not upload')
            (artifacts/'gpu-engine').mkdir();(artifacts/'gpu-engine/input.json').write_text('tool input')
            manifest=guest.publish(Client(),root,task,{'package.zip','metadata.json'},'failed')
            self.assertEqual({row['path']for row in manifest['files']},{'failed/case/runtime.log','failed/case/capture.png'})
            self.assertEqual(manifest['state'],'failed');self.assertEqual(manifest['gameVerdict'],'not-inferred')
            self.assertEqual((manifest['slot'],manifest['lane'],manifest['target']),('windows-x64-remaining','remaining','windows-x64'))
            self.assertTrue(all('gpu'not in row['objectKey'].lower()and'blast'not in row['objectKey'].lower()for row in manifest['files']))
            builder_transfer.validate_results(manifest,{**task,'payloadSource':task['source']},'windows-x64-remaining')
            self.assertEqual(calls[-1][1][1],'results/windows-x64-remaining/manifest.json')
            with self.assertRaisesRegex(ValueError,'slot'):
                builder_transfer.validate_results(manifest,{**task,'payloadSource':task['source']},'windows-x64-core')

    def test_publish_uploads_every_object_before_the_manifest_and_stops_on_a_failed_upload(self):
        import threading
        task=self.lane()
        for fail in (False,True):
            with self.subTest(fail=fail),tempfile.TemporaryDirectory()as root:
                root=Path(root);artifacts=root/'artifacts';artifacts.mkdir()
                for index in range(40):(artifacts/f'case-{index:02d}.json').write_text(str(index))
                calls=[];lock=threading.Lock()
                class Client:
                    def call(self,operation,*args):
                        key=args[1]
                        if fail and key.endswith('object-000007.bin'):raise RuntimeError('put failed')
                        with lock:calls.append(key)
                if fail:
                    with self.assertRaisesRegex(RuntimeError,'put failed'):guest.publish(Client(),root,task,set(),'finished')
                    self.assertNotIn('results/windows-x64-remaining/manifest.json',calls)
                    continue
                manifest=guest.publish(Client(),root,task,set(),'finished')
                self.assertEqual(calls[-1],'results/windows-x64-remaining/manifest.json')
                self.assertEqual(sorted(calls[:-1]),[row['objectKey']for row in manifest['files']])
                self.assertEqual([row['objectKey']for row in manifest['files']],
                                 [f'results/windows-x64-remaining/finished/object-{index:06d}.bin'for index in range(40)])
                self.assertTrue(all(set(row)=={'path','objectKey','size','sha256'}for row in manifest['files']))
                self.assertEqual(json.loads((root/'result-manifest.json').read_text()),manifest)

    def test_each_lane_publishes_only_terminal_checkpoints_under_its_own_slot(self):
        objects={}
        class Client:
            def call(self,operation,*args):objects[args[1]]=Path(args[-1]).read_bytes()
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);(root/'artifacts').mkdir();(root/'artifacts/progress.json').write_text('core committed')
            manifest=guest.publish(Client(),root,self.lane('windows-x64-core'),set(),'finished')
            self.assertTrue(manifest['files'][0]['objectKey'].startswith('results/windows-x64-core/finished/'))
            self.assertIn('results/windows-x64-core/manifest.json',objects)
            for state in ('core-retained','partial'):
                with self.subTest(state=state),self.assertRaisesRegex(ValueError,'checkpoint'):guest.publish(Client(),root,self.lane(),set(),state)
            with self.assertRaisesRegex(ValueError,'slot'):guest.publish(Client(),root,{**self.lane(),'lane':'core'},set(),'failed')

    def handoff(self,root,core_bytes=(b'{"collectionComplete": true}',b'{"passed": true}'),**changes):
        """Controller-written handoff in a fake bucket; returns the client."""
        objects={}
        rows=[{'path':path,'objectKey':guest.names.handoff_object(index),'size':len(data),'sha256':hashlib.sha256(data).hexdigest()}
              for index,(path,data) in enumerate(zip(guest.names.HANDOFF_FILES,core_bytes))]
        for row,data in zip(rows,core_bytes):objects[row['objectKey']]=data
        receipt={'schemaVersion':1,**{key:self.task[key]for key in ('runId','source','account','region','target')},
                 'coreSlot':'windows-x64-core','coreInstanceId':'i-0000000000000c0e0','coreManifestSha256':'d'*64,
                 'remainingSlot':'windows-x64-remaining','remainingInstanceId':'i-0000000000000aea0','files':rows,**changes}
        objects[guest.names.HANDOFF_RECEIPT_KEY]=json.dumps(receipt).encode()
        class Client:
            served=objects
            def call(self,operation,*args):
                key=args[args.index('--key')+1]
                if key not in objects:raise guest.builder_s3.ApiError('AccessDenied')
                Path(args[-1]).write_bytes(objects[key])
        return Client()

    def test_hash_bound_handoff_places_core_gate_inputs_at_their_original_paths(self):
        task={**self.lane(),'expiresAtUtc':'2099-01-01T00:00:00Z'}
        with tempfile.TemporaryDirectory()as root:
            client=self.handoff(root)
            result=guest.await_core_handoff(client,root,task)
            for path,data in zip(guest.names.HANDOFF_FILES,(b'{"collectionComplete": true}',b'{"passed": true}')):
                self.assertEqual((Path(root)/'artifacts'/path).read_bytes(),data)
            self.assertEqual(result['receiptSha256'],hashlib.sha256(client.served[guest.names.HANDOFF_RECEIPT_KEY]).hexdigest())
            self.assertEqual((result['coreInstanceId'],result['coreManifestSha256']),('i-0000000000000c0e0','d'*64))
            # Never overwrite: a second placement in the same work tree fails.
            with self.assertRaises(FileExistsError):guest.await_core_handoff(client,root,task)

    def test_handoff_hash_mismatch_fails_before_anything_is_placed(self):
        task={**self.lane(),'expiresAtUtc':'2099-01-01T00:00:00Z'}
        with tempfile.TemporaryDirectory()as root:
            client=self.handoff(root);client.served[guest.names.handoff_object(1)]=b'{"passed": false}'
            with self.assertRaisesRegex(ValueError,'differ'):guest.await_core_handoff(client,root,task)
            self.assertFalse((Path(root)/'artifacts').exists())

    def test_handoff_for_another_lane_instance_or_file_set_is_refused(self):
        task={**self.lane(),'expiresAtUtc':'2099-01-01T00:00:00Z'}
        for changes in ({'remainingInstanceId':'i-0000000000000fff0'},{'coreInstanceId':'i-0000000000000aea0'},
                        {'remainingSlot':'windows-x64-core'},{'runId':'sancus-builder-other'},{'coreManifestSha256':'short'}):
            with self.subTest(changes=changes),tempfile.TemporaryDirectory()as root,self.assertRaisesRegex(ValueError,'names another'):
                guest.await_core_handoff(self.handoff(root,**changes),root,task)
        with tempfile.TemporaryDirectory()as root:
            client=self.handoff(root);receipt=json.loads(client.served[guest.names.HANDOFF_RECEIPT_KEY])
            receipt['files']=receipt['files'][:1];client.served[guest.names.HANDOFF_RECEIPT_KEY]=json.dumps(receipt).encode()
            with self.assertRaisesRegex(ValueError,'exactly the gate inputs'):guest.await_core_handoff(client,root,task)
        with tempfile.TemporaryDirectory()as root,self.assertRaisesRegex(ValueError,'names another'):
            guest.await_core_handoff(self.handoff(root),root,{**self.lane('windows-x64-core'),'instanceId':'i-0000000000000aea0','expiresAtUtc':'2099-01-01T00:00:00Z'})

    def test_access_denied_before_the_handoff_exists_is_pending_and_recorded(self):
        # Campaign 2 attempt 3: without s3:ListBucket a missing key answers 403, not 404.
        task={**self.lane(),'expiresAtUtc':'2099-01-01T00:00:00Z'}
        with tempfile.TemporaryDirectory()as root:
            client=self.handoff(root);replies=iter(['AccessDenied','AccessDenied','NoSuchKey'])
            original=type(client).call
            def call(self,operation,*args):
                if args[args.index('--key')+1]==guest.names.HANDOFF_RECEIPT_KEY:
                    code=next(replies,None)
                    if code:raise guest.builder_s3.ApiError(code)
                return original(self,operation,*args)
            type(client).call=call
            clock=[0.0];samples=[]
            with patch.object(guest.threading.Event,'wait',lambda self,seconds:clock.__setitem__(0,clock[0]+seconds)):
                result=guest.await_core_handoff(client,root,task,now=lambda:clock[0],samples=samples)
        self.assertEqual(result['coreInstanceId'],'i-0000000000000c0e0')
        self.assertEqual([(row['code'],row['count'])for row in samples],[('AccessDenied',2),('NoSuchKey',1)])

    def test_handoff_wait_is_bounded_before_expiry_minus_cleanup_and_publish_reserve(self):
        class Broken:
            def call(self,operation,*args):raise guest.builder_s3.ApiError('InvalidAccessKeyId')
        class Denied:
            def call(self,operation,*args):raise guest.builder_s3.ApiError('AccessDenied')
        with tempfile.TemporaryDirectory()as root:
            with self.assertRaises(guest.builder_s3.ApiError):
                guest.await_core_handoff(Broken(),root,{**self.lane(),'expiresAtUtc':'2099-01-01T00:00:00Z'})
            # Expiry one hour away: the wait ends at E-20 min-reserve, i.e. 3600-1200-1500 = 900 s.
            clock=[0.0];start=1_000_000.0
            task={**self.lane(),'expiresAtUtc':guest.datetime.datetime.fromtimestamp(start+3600,guest.datetime.timezone.utc).isoformat()}
            with patch.object(guest.threading.Event,'wait',lambda self,seconds:clock.__setitem__(0,clock[0]+seconds)):
                with self.assertRaisesRegex(TimeoutError,'AccessDenied'):
                    guest.await_core_handoff(Denied(),root,task,now=lambda:clock[0],clock=lambda:start)
            self.assertEqual(clock[0],3600-20*60-guest.HANDOFF_PUBLISH_RESERVE_SECONDS)
            self.assertLessEqual(guest.HANDOFF_WAIT_SECONDS,5400)
            with self.assertRaisesRegex(TimeoutError,'no gate ran'):
                guest.await_core_handoff(Denied(),root,{**self.lane(),'expiresAtUtc':'2020-01-01T00:00:00Z'})


class LaneSequences(unittest.TestCase):
    """The real guest `run` per lane, with every process, IMDS and S3 call replaced."""
    def run_lane(self,slot,handoff=None,fail_step=None,expires='2099-01-01T00:00:00+00:00',lane=None):
        commands=[];published=[]
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);work=root/'work';(work/'artifacts/gpu-engine').mkdir(parents=True)
            (work/'artifacts/gpu-engine/godot_console.exe').write_text('engine')
            manifest=root/'manifest.json';manifest.write_text('{}')
            task={'runId':'sancus-builder-20261030-8762d11-1234567890abcdef','source':'a'*40,'account':'524329886851',
                  'region':'us-east-1','target':'windows-x64','slot':slot,'lane':lane or guest.names.SLOTS.get(slot,{}).get('lane'),
                  'instanceId':'i-0000000000000aea0','verificationRunId':'123','expiresAtUtc':expires,
                  'payloadManifestSha256':guest.digest(manifest)}
            (root/'task.json').write_text(json.dumps(task))
            def process(command,**kwargs):
                name=Path(kwargs['stdout'].name).name[len('guest-'):-len('.log')];commands.append(name)
                return guest.subprocess.CompletedProcess(command,1 if name==fail_step else 0)
            session=json.dumps({'sessionId':1,'user':'BlastQa','administrator':False,'driverModel':'WDDM'})
            args=guest.argparse.Namespace(task=root/'task.json',manifest=manifest,cache=root/'cache',work=work)
            with (patch.object(guest.native_execution,'instance_identity',return_value={'accountId':'524329886851','region':'us-east-1','instanceId':'i-0000000000000aea0'}),
                  patch.object(guest,'prepare_source',return_value=[]),patch.object(guest.native_execution,'context',return_value={'kind':'test'}),
                  patch.object(guest,'object_client',return_value=None),
                  patch.object(guest.subprocess,'run',side_effect=process),patch.object(guest.subprocess,'check_output',return_value=session),
                  patch.object(guest,'await_core_handoff',side_effect=handoff or(lambda *a,**k:{'receiptSha256':'e'*64}))as waited,
                  patch.object(guest,'publish',side_effect=lambda client,root,descriptor,names,state:published.append((descriptor['slot'],state)))):
                code=guest.run(args)
            report=json.loads((work/'artifacts/guest-execution.json').read_text())
        return code,commands,published,report,waited

    def test_core_lane_runs_the_core_collector_and_finishes_without_any_wait(self):
        code,commands,published,report,waited=self.run_lane('windows-x64-core')
        self.assertEqual(code,0);self.assertEqual(commands,['desktop','engine','core'])
        self.assertEqual(published,[('windows-x64-core','finished')]);waited.assert_not_called()
        self.assertEqual((report['slot'],report['lane'],report['complete']),('windows-x64-core','core',True))

    def test_remaining_lane_runs_at_once_then_gates_against_the_handoff(self):
        code,commands,published,report,waited=self.run_lane('windows-x64-remaining')
        self.assertEqual(code,0)
        self.assertEqual(commands,['desktop','engine','installed-launcher','remaining','preliminary-qualification'])
        self.assertEqual(published,[('windows-x64-remaining','finished')]);waited.assert_called_once()
        self.assertEqual(report['coreHandoff'],{'receiptSha256':'e'*64})
        steps={row['name']:row for row in report['steps']}
        self.assertNotIn('core',steps)

    def test_missing_handoff_or_failed_step_publishes_failed_and_never_gates(self):
        code,commands,published,report,_=self.run_lane('windows-x64-remaining',handoff=TimeoutError('Core handoff did not arrive'))
        self.assertEqual(code,1);self.assertNotIn('preliminary-qualification',commands)
        self.assertEqual(published,[('windows-x64-remaining','failed')]);self.assertIn('Core handoff',report['errors'][0])
        code,commands,published,_,waited=self.run_lane('windows-x64-remaining',fail_step='remaining')
        self.assertEqual(code,1);waited.assert_not_called();self.assertEqual(published,[('windows-x64-remaining','failed')])

    def test_remaining_lane_without_suite_headroom_never_starts_the_suite(self):
        expires=(guest.datetime.datetime.now(guest.datetime.timezone.utc)+guest.datetime.timedelta(seconds=20*60+guest.REMAINING_STEP_SECONDS-60)).isoformat()
        code,commands,published,report,_=self.run_lane('windows-x64-remaining',expires=expires)
        self.assertEqual(code,1);self.assertEqual(commands,['desktop','engine','installed-launcher'])
        self.assertIn('headroom: remaining',report['errors'][0])

    def test_remaining_step_also_needs_its_handoff_gate_and_publish_reserve(self):
        # Enough for the step alone, not for the gate and publication after it.
        expires=(guest.datetime.datetime.now(guest.datetime.timezone.utc)+guest.datetime.timedelta(
            seconds=20*60+guest.REMAINING_STEP_SECONDS+guest.HANDOFF_PUBLISH_RESERVE_SECONDS-120)).isoformat()
        code,commands,published,report,waited=self.run_lane('windows-x64-remaining',expires=expires)
        self.assertEqual(code,1);self.assertEqual(commands,['desktop','engine','installed-launcher'])
        self.assertIn('headroom: remaining',report['errors'][0]);waited.assert_not_called()
        self.assertEqual(published,[('windows-x64-remaining','failed')])

    def test_unknown_or_mismatched_lane_never_runs_a_workload(self):
        for slot,lane in (('windows-x64-core','remaining'),('windows-x64','core'),('windows-x64-remaining',None)):
            with self.subTest(slot=slot,lane=lane):
                code,commands,published,report,_=self.run_lane(slot,lane=lane or 'unknown')
                self.assertEqual(code,1);self.assertEqual(commands,[]);self.assertIn('lane slot',report['errors'][0])


if __name__=='__main__':unittest.main()

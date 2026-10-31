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
                   'account':'524329886851','region':'us-east-1','target':'linux-x64','instanceId':'i-owned'}
        self.manifest={'schemaVersion':1,**{key:self.task[key]for key in ('runId','source','account','region')},'files':[
            {'role':'source','target':'all','objectKey':'payload/input-000.bin','fileName':'source.zip','sha256':'b'*64,'size':10},
            {'role':'artifact','target':'linux-x64','objectKey':'payload/input-001.bin','fileName':'package.zip','sha256':'c'*64,'size':20}]}

    def test_target_selection_is_data_only_and_requires_actual_source_and_package(self):
        self.assertEqual(len(guest.validate_inputs(self.manifest,self.task)),2)
        manifest=copy.deepcopy(self.manifest);manifest['files'][1]['target']='windows-x64'
        with self.assertRaisesRegex(ValueError,'package artifacts'):guest.validate_inputs(manifest,self.task)

    def test_wrong_identity_role_path_duplicate_or_hash_refused(self):
        for key,value in [('role','execute-shell'),('fileName','../script'),('objectKey','results/overwrite'),('sha256','bad')]:
            manifest=copy.deepcopy(self.manifest);manifest['files'][0][key]=value
            with self.subTest(key=key),self.assertRaises(ValueError):guest.validate_inputs(manifest,self.task)
        manifest=copy.deepcopy(self.manifest);manifest['source']='d'*40
        with self.assertRaisesRegex(ValueError,'identity'):guest.validate_inputs(manifest,self.task)
        manifest=copy.deepcopy(self.manifest);manifest['files'].append(manifest['files'][0])
        with self.assertRaises(ValueError):guest.validate_inputs(manifest,self.task)

    def test_publish_keeps_raw_evidence_excludes_inputs_and_validates_for_host_retention(self):
        calls=[]
        class Client:
            def call(self,operation,*args):calls.append((operation,args))
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);artifacts=root/'artifacts';artifacts.mkdir()
            (artifacts/'case').mkdir();(artifacts/'case/runtime.log').write_text('actual error retained')
            (artifacts/'case/capture.png').write_bytes(b'raw image bytes')
            (artifacts/'package.zip').write_bytes(b'input must not upload')
            (artifacts/'metadata.json').write_text('input metadata must not upload')
            (artifacts/'gpu-engine').mkdir();(artifacts/'gpu-engine/input.json').write_text('tool input')
            manifest=guest.publish(Client(),root,self.task,{'package.zip','metadata.json'},'failed')
            self.assertEqual({row['path']for row in manifest['files']},{'failed/case/runtime.log','failed/case/capture.png'})
            self.assertEqual(manifest['state'],'failed');self.assertEqual(manifest['gameVerdict'],'not-inferred')
            self.assertTrue(all('gpu'not in row['objectKey'].lower()and'blast'not in row['objectKey'].lower()for row in manifest['files']))
            builder_transfer.validate_results(manifest,{**self.task,'payloadSource':self.task['source']},'linux-x64')
            self.assertEqual(calls[-1][1][1],'results/linux-x64/manifest.json')

    def test_later_progress_cannot_overwrite_the_committed_core_checkpoint(self):
        objects={}
        class Client:
            def call(self,operation,*args):objects[args[1]]=Path(args[-1]).read_bytes()
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);(root/'artifacts').mkdir();path=root/'artifacts/progress.json'
            path.write_text('core committed')
            first=guest.publish(Client(),root,self.task,set(),'core-retained')
            path.write_text('later final result')
            second=guest.publish(Client(),root,self.task,set(),'finished')
            self.assertNotEqual(first['files'][0]['objectKey'],second['files'][0]['objectKey'])
            self.assertEqual(hashlib.sha256(objects[first['files'][0]['objectKey']]).hexdigest(),first['files'][0]['sha256'])

    def test_only_matching_core_ack_releases_the_next_phase(self):
        task={**self.task,'expiresAtUtc':'2099-01-01T00:00:00Z'};ack={**self.task,'coreManifestSha256':'d'*64,'retained':True}
        class Client:
            def call(self,operation,*args):Path(args[-1]).write_text(json.dumps(ack))
        with tempfile.TemporaryDirectory()as root:
            self.assertTrue(guest.await_core_retention(Client(),root,task,'d'*64)['retained'])
            ack['coreManifestSha256']='e'*64
            with self.assertRaisesRegex(ValueError,'acknowledgement'):guest.await_core_retention(Client(),root,task,'d'*64)

    def test_access_denied_before_the_ack_exists_is_pending_and_recorded(self):
        # Campaign 2 attempt 3: without s3:ListBucket a missing key answers 403, not 404.
        task={**self.task,'expiresAtUtc':'2099-01-01T00:00:00Z'};ack={**self.task,'coreManifestSha256':'d'*64,'retained':True}
        replies=iter(['AccessDenied','AccessDenied','NoSuchKey',None])
        class Client:
            def call(self,operation,*args):
                code=next(replies)
                if code:raise guest.builder_s3.ApiError(code)
                Path(args[-1]).write_text(json.dumps(ack))
        clock=[0.0];samples=[]
        with tempfile.TemporaryDirectory()as root,patch.object(guest.threading.Event,'wait',lambda self,seconds:clock.__setitem__(0,clock[0]+seconds)):
            result=guest.await_core_retention(Client(),root,task,'d'*64,now=lambda:clock[0],samples=samples)
        self.assertTrue(result['retained'])
        self.assertEqual([(row['code'],row['count'])for row in samples],[('AccessDenied',2),('NoSuchKey',1)])

    def test_other_errors_still_fail_and_a_persistent_403_times_out_with_evidence(self):
        task={**self.task,'expiresAtUtc':'2099-01-01T00:00:00Z'}
        class Broken:
            def call(self,operation,*args):raise guest.builder_s3.ApiError('InvalidAccessKeyId')
        class Denied:
            def call(self,operation,*args):raise guest.builder_s3.ApiError('AccessDenied')
        with tempfile.TemporaryDirectory()as root:
            with self.assertRaises(guest.builder_s3.ApiError):guest.await_core_retention(Broken(),root,task,'d'*64)
            clock=[0.0]
            with patch.object(guest.threading.Event,'wait',lambda self,seconds:clock.__setitem__(0,clock[0]+seconds)):
                with self.assertRaisesRegex(TimeoutError,'AccessDenied'):
                    guest.await_core_retention(Denied(),root,task,'d'*64,now=lambda:clock[0])
            self.assertGreaterEqual(clock[0],guest.CORE_ACK_WAIT_SECONDS)
            self.assertLessEqual(guest.CORE_ACK_WAIT_SECONDS,5400)

    def test_expired_window_never_starts_remaining_work(self):
        with tempfile.TemporaryDirectory()as root:
            with self.assertRaisesRegex(TimeoutError,'no long tours'):
                guest.await_core_retention(None,root,{**self.task,'expiresAtUtc':'2020-01-01T00:00:00Z'},'d'*64)


if __name__=='__main__':unittest.main()

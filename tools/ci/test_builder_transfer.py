import copy
import hashlib
from pathlib import Path
import tempfile
import unittest

import builder_transfer as transfer


class ResultRetention(unittest.TestCase):
    def setUp(self):
        self.plan={'runId':'sancus-builder-20261030-8762d11-1234567890abcdef','payloadSource':'a'*40,
                   'account':'524329886851','region':'us-east-1'}
        self.data=b'raw failed scenario evidence\n'
        self.manifest={'schemaVersion':1,'runId':self.plan['runId'],'source':'a'*40,'target':'linux-x64','state':'failed',
                       'files':[{'path':'failed/core/runtime.log','objectKey':'results/linux-x64/failed/object-000000.bin',
                                 'size':len(self.data),'sha256':hashlib.sha256(self.data).hexdigest()}]}

    def test_hash_verified_retention_does_not_imply_product_pass(self):
        outer=self
        class Client:
            run=outer.plan['runId']
            def call(self,operation,*args):
                outer.assertEqual(operation,'get-object');Path(args[-1]).write_bytes(outer.data)
        with tempfile.TemporaryDirectory()as root:
            result=transfer.retain_results(Client(),self.plan,'linux-x64',self.manifest,Path(root)/'new')
            self.assertTrue(result['retained']);self.assertEqual(result['gameVerdict'],'not-inferred')
            self.assertEqual(Path(result['files'][0]['localPath']).read_bytes(),self.data)

    def test_digest_mismatch_never_writes_retention_success(self):
        outer=self
        class Client:
            run=outer.plan['runId']
            def call(self,operation,*args):Path(args[-1]).write_bytes(b'corrupt')
        with tempfile.TemporaryDirectory()as root:
            destination=Path(root)/'new'
            with self.assertRaisesRegex(ValueError,'digest/size'):transfer.retain_results(Client(),self.plan,'linux-x64',self.manifest,destination)
            self.assertFalse((destination/'retention.json').exists())

    def test_stale_source_run_or_target_rejected(self):
        for key,value in [('source','b'*40),('runId','old'),('target','windows-x64')]:
            manifest={**self.manifest,key:value}
            with self.subTest(key=key),self.assertRaisesRegex(ValueError,'differs'):
                transfer.validate_results(manifest,self.plan,'linux-x64')

    def test_traversal_case_collision_wrong_prefix_and_size_fail(self):
        for path in ('../secret','/absolute','C:/temp','core\\log','core/../log','core//log'):
            manifest=copy.deepcopy(self.manifest);manifest['files'][0]['path']=path
            with self.subTest(path=path),self.assertRaises(ValueError):transfer.validate_results(manifest,self.plan,'linux-x64')
        manifest=copy.deepcopy(self.manifest);manifest['files'][0]['objectKey']='payload/private'
        with self.assertRaises(ValueError):transfer.validate_results(manifest,self.plan,'linux-x64')
        manifest=copy.deepcopy(self.manifest);manifest['files'].append({**manifest['files'][0],'path':'failed/core/Runtime.log','objectKey':'results/linux-x64/failed/object-000001.bin'})
        with self.assertRaises(ValueError):transfer.validate_results(manifest,self.plan,'linux-x64')
        with self.assertRaises(ValueError):transfer.validate_results(self.manifest,self.plan,'linux-x64',max_bytes=1)

    def test_bootstrap_marker_is_uploaded_only_after_the_frozen_manifest(self):
        outer=self;calls=[]
        class Client:
            run=outer.plan['runId'];bucket='owned-bucket'
            def call(self,operation,*args):calls.append(args[1])
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);items=[]
            for name in ('manifest.json','bootstrap.zip','aws.msi'):
                path=root/name;path.write_bytes(name.encode());items.append({'path':str(path),'sha256':transfer.sha256(path)})
            transfer.publish_bootstrap(Client(),self.plan,root/'journal.jsonl',*items)
            self.assertEqual(calls,['payload/bootstrap-awscli.msi','payload/manifest.json','payload/bootstrap.zip'])


if __name__=='__main__':unittest.main()

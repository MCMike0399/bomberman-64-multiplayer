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
                       'account':self.plan['account'],'region':self.plan['region'],
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

    def many(self,count=40):
        files=[];blobs={}
        for index in range(count):
            data=('evidence %d\n'%index).encode();key='results/linux-x64/failed/object-%06d.bin'%index
            blobs[key]=data
            files.append({'path':'failed/core/file-%03d.log'%index,'objectKey':key,'size':len(data),'sha256':hashlib.sha256(data).hexdigest()})
        return {**self.manifest,'files':files},blobs

    def test_parallel_retention_verifies_every_file_keeps_order_and_records_throughput(self):
        import threading,time
        manifest,blobs=self.many();outer=self;state={'active':0,'peak':0};lock=threading.Lock()
        class Client:
            run=outer.plan['runId']
            def call(self,operation,*args):
                with lock:state['active']+=1;state['peak']=max(state['peak'],state['active'])
                time.sleep(0.01);Path(args[-1]).write_bytes(blobs[args[args.index('--key')+1]])
                with lock:state['active']-=1
        with tempfile.TemporaryDirectory()as root:
            result=transfer.retain_results(Client(),self.plan,'linux-x64',manifest,Path(root)/'new',workers=8)
        self.assertGreater(state['peak'],1)
        self.assertLessEqual(state['peak'],8)
        self.assertEqual([row['path']for row in result['files']],[row['path']for row in manifest['files']])
        timing=result['retentionTiming']
        self.assertEqual((timing['workers'],timing['files'],timing['bytes']),(8,40,sum(len(b)for b in blobs.values())))
        self.assertGreater(timing['throughputMBps'],0)

    def test_one_digest_mismatch_fails_the_whole_parallel_retention(self):
        manifest,blobs=self.many();outer=self
        bad=manifest['files'][17]['objectKey']
        class Client:
            run=outer.plan['runId']
            def call(self,operation,*args):
                key=args[args.index('--key')+1]
                Path(args[-1]).write_bytes(b'corrupt'if key==bad else blobs[key])
        with tempfile.TemporaryDirectory()as root:
            destination=Path(root)/'new'
            with self.assertRaisesRegex(ValueError,'digest/size'):
                transfer.retain_results(Client(),self.plan,'linux-x64',manifest,destination,workers=8)
            self.assertFalse((destination/'retention.json').exists())
            self.assertEqual([path for path in destination.rglob('tmp*')],[])  # no temporary leftovers

    def test_one_worker_error_fails_the_whole_parallel_retention(self):
        manifest,blobs=self.many();outer=self
        failing=manifest['files'][5]['objectKey']
        class Client:
            run=outer.plan['runId']
            def call(self,operation,*args):
                key=args[args.index('--key')+1]
                if key==failing:raise RuntimeError('s3api/get-object failed (InternalError)')
                Path(args[-1]).write_bytes(blobs[key])
        with tempfile.TemporaryDirectory()as root:
            destination=Path(root)/'new'
            with self.assertRaisesRegex(RuntimeError,'InternalError'):
                transfer.retain_results(Client(),self.plan,'linux-x64',manifest,destination,workers=8)
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

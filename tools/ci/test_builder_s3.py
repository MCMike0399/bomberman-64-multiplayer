import unittest
import json
from unittest.mock import Mock,patch

import builder_namespace as names
import builder_s3 as s3
import builder_staging

RUN='sancus-builder-20261030-8762d11-1234567890abcdef'


class OwnerBoundStaging(unittest.TestCase):
    def test_new_name_uses_random_nonce_and_account_within_s3_limit(self):
        with patch.object(names.secrets,'token_hex',return_value='1234567890abcdef')as random:
            run=names.new_run_id('20261030','a'*40)
        random.assert_called_once_with(8)
        self.assertTrue(run.endswith('-1234567890abcdef'))
        self.assertLessEqual(len(builder_staging.bucket_name(run)),63)
        self.assertTrue(builder_staging.bucket_name(run).endswith('-'+names.ACCOUNT))

    def test_preexisting_or_foreign_name_aborts_before_create_or_upload(self):
        for existing,status in ((True,200),(False,200),(False,403)):
            create=Mock();calls=[]
            def aws(*args):
                calls.append(args)
                if args[0]=='sts':return {'Account':names.ACCOUNT}
                if args[1]=='list-buckets':return {'Buckets':[{'Name':builder_staging.bucket_name(RUN)}]if existing else[]}
                if status==403:raise s3.ApiError('403')
                return {}
            with self.subTest(existing=existing,status=status),self.assertRaises(s3.BucketCollision):s3.provision_new(aws,RUN,create)
            create.assert_not_called()
            self.assertFalse(any(row[1]=='put-object'for row in calls))

    def test_create_collision_is_not_retried_or_reused(self):
        def aws(*args):
            if args[0]=='sts':return {'Account':names.ACCOUNT}
            if args[1]=='list-buckets':return {'Buckets':[]}
            raise s3.ApiError('404')
        create=Mock(side_effect=s3.ApiError('BucketAlreadyExists'))
        with self.assertRaises(s3.BucketCollision):s3.provision_new(aws,RUN,create)
        create.assert_called_once_with()

    def test_new_creation_is_owner_and_nonce_checked_before_upload(self):
        created=False;calls=[]
        def create():
            nonlocal created
            created=True
        def aws(*args):
            calls.append(args)
            if args[0]=='sts':return {'Account':names.ACCOUNT}
            if args[1]=='list-buckets':return {'Buckets':[]}
            if args[1]=='head-bucket':
                if not created:raise s3.ApiError('404')
                return {}
            if args[1]=='get-bucket-tagging':return {'TagSet':[{'Key':names.RUN_TAG,'Value':RUN}]}
            if args[1]=='put-object':return {}
            raise AssertionError(args)
        client=s3.provision_new(aws,RUN,create)
        self.assertTrue(created)
        self.assertFalse(any(row[1]=='put-object'for row in calls))
        client.call('put-object','--key','payload/client.zip','--body','owned-file.zip')
        self.assertEqual(calls[-1][calls[-1].index('--expected-bucket-owner')+1],names.ACCOUNT)

    def test_every_supported_bucket_and_object_call_pins_owner(self):
        aws=Mock(return_value={});client=s3.Objects(aws,RUN)
        for operation in client.OPERATIONS:
            client.call(operation)
            arguments=aws.call_args.args
            self.assertEqual(arguments[arguments.index('--expected-bucket-owner')+1],names.ACCOUNT)
            self.assertEqual(arguments[arguments.index('--bucket')+1],client.bucket)
        for argument in ('--bucket','--expected-bucket-owner=other','--cli-input-json','--profile=other','--endpoint-url'):
            with self.assertRaises(ValueError):client.call('put-object',argument,'other')

    def test_absence_needs_both_owned_list_and_owner_bound_head(self):
        for listed,status,expected in ((True,403,False),(False,403,True),(False,404,True),(False,200,False)):
            def aws(*args):
                if args[0]=='sts':return {'Account':names.ACCOUNT}
                if args[1]=='list-buckets':return {'Buckets':[{'Name':builder_staging.bucket_name(RUN)}]if listed else[]}
                if status!=200:raise s3.ApiError(str(status))
                return {}
            with self.subTest(listed=listed,status=status):self.assertEqual(s3.verify_absent(aws,RUN),expected)

    def test_paginated_owned_list_cannot_miss_a_preexisting_bucket(self):
        calls=[]
        def aws(*args):
            calls.append(args)
            if args[0]=='sts':return {'Account':names.ACCOUNT}
            if '--continuation-token'not in args:return {'Buckets':[],'ContinuationToken':'next'}
            return {'Buckets':[{'Name':builder_staging.bucket_name(RUN)}]}
        create=Mock()
        with self.assertRaises(s3.BucketCollision):s3.provision_new(aws,RUN,create)
        create.assert_not_called()
        self.assertTrue(any('--continuation-token'in row for row in calls))

    def test_cleanup_aborts_uploads_then_deletes_versions_objects_and_bucket(self):
        calls=[];versions=[{'Key':'results/a','VersionId':'v1'}];markers=[{'Key':'results/a','VersionId':'m1'}]
        uploads=[{'Key':'results/b','UploadId':'u1'}];objects=[{'Key':'results/c'}]
        deleted=False
        def aws(*args):
            nonlocal deleted
            operation=args[1];calls.append(operation)
            if operation=='get-caller-identity':return {'Account':names.ACCOUNT}
            if operation=='list-buckets':return {'Buckets':[]if deleted else[{'Name':builder_staging.bucket_name(RUN)}]}
            self.assertEqual(args[args.index('--expected-bucket-owner')+1],names.ACCOUNT)
            if operation=='list-multipart-uploads':return {'Uploads':list(uploads)}
            if operation=='abort-multipart-upload':uploads.clear();return {}
            if operation=='list-parts':raise s3.ApiError('NoSuchUpload')
            if operation=='list-object-versions':return {'Versions':list(versions),'DeleteMarkers':list(markers)}
            if operation=='list-objects-v2':return {'Contents':list(objects)}
            if operation=='delete-objects':
                rows=json.loads(args[args.index('--delete')+1])['Objects']
                if any('VersionId'in row for row in rows):versions.clear();markers.clear()
                else:objects.clear()
                return {}
            if operation=='delete-bucket':deleted=True;return {}
            if operation=='head-bucket':raise s3.ApiError('404')
            raise AssertionError(operation)
        authorize=Mock();stop=Mock(side_effect=lambda:calls.append('stop-writers'))
        result=s3.purge_owned(s3.Objects(aws,RUN),authorize,stop,deadline=100,now=lambda:0)
        self.assertEqual(result['state'],'absent')
        self.assertLess(calls.index('stop-writers'),calls.index('abort-multipart-upload'))
        self.assertLess(calls.index('abort-multipart-upload'),calls.index('delete-objects'))
        self.assertLess(calls.index('delete-objects'),calls.index('delete-bucket'))
        self.assertGreater(authorize.call_count,1)

    def test_ownership_failure_prevents_any_delete(self):
        aws=Mock();authorize=Mock(side_effect=RuntimeError('Wrong run ownership'))
        with self.assertRaisesRegex(RuntimeError,'ownership'):
            s3.purge_owned(s3.Objects(aws,RUN),authorize,Mock(),deadline=100,now=lambda:0)
        aws.assert_not_called()


class PendingObjectPolicy(unittest.TestCase):
    """One bounded-poll step shared by the guest acknowledgement wait and controller receipts."""
    def client(self,*codes):
        replies=list(codes)
        def call(operation,*arguments):
            code=replies.pop(0)
            if code:raise s3.ApiError(code)
        return Mock(call=call)

    def test_guest_policy_treats_missing_403_and_throttling_as_pending_with_samples(self):
        samples=[];client=self.client('AccessDenied','AccessDenied','NoSuchKey','SlowDown',None)
        results=[s3.fetch_if_present(client,'payload/k','/tmp/x',s3.GUEST_PENDING,samples,now=lambda:7)for _ in range(5)]
        self.assertEqual(results,[False,False,False,False,True])
        self.assertEqual([(row['code'],row['count'])for row in samples],[('AccessDenied',2),('NoSuchKey',1),('SlowDown',1)])

    def test_any_other_code_fails_at_once(self):
        for policy in (s3.GUEST_PENDING,s3.CONTROLLER_PENDING):
            with self.assertRaises(s3.ApiError):s3.fetch_if_present(self.client('InvalidAccessKeyId'),'k','/tmp/x',policy,[])

    def test_controller_holds_list_bucket_so_403_is_a_real_denial(self):
        self.assertNotIn('AccessDenied',s3.CONTROLLER_PENDING);self.assertNotIn('403',s3.CONTROLLER_PENDING)
        with self.assertRaises(s3.ApiError):s3.fetch_if_present(self.client('AccessDenied'),'k','/tmp/x',s3.CONTROLLER_PENDING,[])
        self.assertIn('s3:ListBucket',builder_staging.BUCKET_ACTIONS)
        self.assertNotIn('s3:ListBucket',json.dumps(builder_staging.guest_statements(RUN)))


if __name__=='__main__':unittest.main()

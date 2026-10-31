import unittest

import builder_audit
import builder_namespace as names


def empty(service,operation,*arguments):
    if service=='sts':return {'Account':names.ACCOUNT,'Arn':f'arn:aws:sts::{names.ACCOUNT}:assumed-role/{names.CI_ROLE}/test'}
    return {key:[]for key in ('StackSummaries','Reservations','Volumes','NetworkInterfaces','SecurityGroups','Addresses',
            'Snapshots','Images','Roles','InstanceProfiles','Schedules','ScheduleGroups','logGroups','Buckets',
            'Parameters','DocumentIdentifiers','ResourceTagMappingList','KeyPairs')}


class EmptyAudit(unittest.TestCase):
    def test_empty_role_scoped_audit_checks_every_type_without_mutating(self):
        calls=[]
        def aws(*args):calls.append(args);return empty(*args)
        result=builder_audit.audit(aws)
        self.assertTrue(result['passed'])
        self.assertTrue({'CFN','EC2','EBS','ENI','SG','EIP','IAMRoles','IAMProfiles','SchedulerSchedules',
                         'Logs','S3','SSMParameters','SSMDocuments','TaggedResources'}<=set(result['checked']))
        self.assertEqual(result['resourcesCreated'],[])
        self.assertTrue(all(args[1].startswith(('get-','list-','describe-'))for args in calls))

    def test_missing_permissions_do_not_mean_empty_and_other_reads_continue(self):
        calls=[]
        def aws(*args):
            calls.append(args)
            if args[:2]==('logs','describe-log-groups'):raise RuntimeError('AccessDenied')
            return empty(*args)
        result=builder_audit.audit(aws)
        self.assertFalse(result['passed']);self.assertEqual(result['checked']['Logs']['state'],'unverified')
        self.assertIn(('s3api','list-buckets'),calls)

    def test_unexpected_builder_resource_is_retained_not_deleted(self):
        def aws(*args):
            result=empty(*args)
            if args[:2]==('iam','list-roles'):result['Roles']=[{'RoleName':names.CI_ROLE},{'RoleName':'sancus-builder-20261030-other'}]
            return result
        result=builder_audit.audit(aws)
        self.assertFalse(result['passed'])
        self.assertEqual(result['leftovers'],[{'type':'IAMRoles','id':'sancus-builder-20261030-other'}])
        self.assertEqual(result['resourcesDeleted'],[])

    def test_wrong_account_or_operator_credentials_are_not_oidc_proof(self):
        for arn in ('arn:aws:iam::524329886851:user/operator','arn:aws:sts::111111111111:assumed-role/sancus-builder-ci/a'):
            def aws(*args):return {'Account':names.ACCOUNT,'Arn':arn}
            with self.subTest(arn=arn),self.assertRaises(RuntimeError):builder_audit.audit(aws)

    def test_only_exact_standing_identity_is_excluded_from_tag_sweep(self):
        self.assertTrue(builder_audit.expected_standing(f'arn:aws:iam::{names.ACCOUNT}:role/sancus-builder-ci'))
        self.assertFalse(builder_audit.expected_standing(f'arn:aws:iam::{names.ACCOUNT}:role/sancus-builder-ci-other'))



ARN=f'arn:aws:{{}}:{names.REGION}:{names.ACCOUNT}:{{}}'


def missing(code):
    return RuntimeError(f'ec2/describe: An error occurred ({code}) when calling the operation: not found')


class TaggingIndexResolution(unittest.TestCase):
    """A tagging-API hit is cleared only by an authoritative gone answer."""
    def sweep(self,arn,answer):
        calls=[]
        def aws(*args):
            calls.append(args)
            if args[:2]==('resourcegroupstaggingapi','get-resources'):
                return {'ResourceTagMappingList':[{'ResourceARN':arn}]}
            if args[0]in ('sts',)or args[1]in ('list-stacks','list-roles','list-instance-profiles','list-schedules',
                    'list-schedule-groups','describe-log-groups','list-buckets','describe-parameters','list-documents',
                    'describe-snapshots','describe-images','describe-addresses','describe-key-pairs')or '--filters'in args:
                return empty(*args)
            if isinstance(answer,Exception):raise answer
            return answer
        result=builder_audit.audit(aws)
        self.assertTrue(all(args[1].startswith(('get-','list-','describe-','head-'))for args in calls))
        self.assertEqual(len(result['taggedResources']),1)
        row=result['taggedResources'][0]
        self.assertEqual((row['id'],row['taggingIndex']),(arn,'present'))
        return result,row

    def resolved(self,arn,answer):
        result,row=self.sweep(arn,answer)
        self.assertTrue(row['resolved'],row);self.assertTrue(result['passed']);self.assertEqual(result['leftovers'],[])
        return row

    def leftover(self,arn,answer):
        result,row=self.sweep(arn,answer)
        self.assertFalse(row['resolved'],row);self.assertFalse(result['passed'])
        self.assertEqual(result['leftovers'],[{'type':'TaggedResources','id':arn}])
        return row

    def instance(self,state):return {'Reservations':[{'Instances':[{'InstanceId':'i-0aa4cc1ac8162d4e5','State':{'Name':state}}]}]}

    def test_terminated_instance_resolves(self):
        row=self.resolved(ARN.format('ec2','instance/i-0aa4cc1ac8162d4e5'),self.instance('terminated'))
        self.assertEqual(row['authoritative'],'terminated')

    def test_not_found_resources_resolve(self):
        for resource,code in (('instance/i-0aa4cc1ac8162d4e5','InvalidInstanceID.NotFound'),('volume/vol-041eca3f76f58e996','InvalidVolume.NotFound'),
                              ('network-interface/eni-01','InvalidNetworkInterfaceID.NotFound'),('security-group/sg-01','InvalidGroup.NotFound')):
            with self.subTest(resource=resource):
                self.assertEqual(self.resolved(ARN.format('ec2',resource),missing(code))['authoritative'],code)
        run='sancus-builder-20261030-454da3d-0123456789abcdef'
        for arn,code in ((f'arn:aws:iam::{names.ACCOUNT}:role/{run}-agent','NoSuchEntity'),
                         (f'arn:aws:iam::{names.ACCOUNT}:instance-profile/{run}-profile','NoSuchEntity'),
                         (ARN.format('scheduler',f'schedule/{run}-expiry-group/{run}-linux-expiry'),'ResourceNotFoundException'),
                         (ARN.format('scheduler',f'schedule-group/{run}-expiry-group'),'ResourceNotFoundException')):
            with self.subTest(arn=arn):self.resolved(arn,missing(code))
        from builder_s3 import ApiError
        for code in ('404','NoSuchBucket'):
            with self.subTest(code=code):self.resolved('arn:aws:s3:::'+run,ApiError(code))
        stack=ARN.format('cloudformation',f'stack/{run}/0123')
        self.resolved(stack,{'Stacks':[{'StackStatus':'DELETE_COMPLETE'}]})
        self.resolved(stack,RuntimeError('An error occurred (ValidationError) when calling the DescribeStacks operation: Stack with id x does not exist'))

    def test_shutting_down_or_existing_resources_stay_leftovers(self):
        self.assertEqual(self.leftover(ARN.format('ec2','instance/i-0aa4cc1ac8162d4e5'),self.instance('shutting-down'))['authoritative'],'shutting-down')
        self.leftover(ARN.format('ec2','volume/vol-041eca3f76f58e996'),{'Volumes':[{'VolumeId':'vol-041eca3f76f58e996','State':'available'}]})
        self.leftover(ARN.format('cloudformation','stack/sancus-builder-20261030-x/1'),{'Stacks':[{'StackStatus':'DELETE_FAILED'}]})
        self.leftover('arn:aws:s3:::sancus-builder-20261030-x',{})

    def test_access_denied_or_other_errors_stay_leftovers(self):
        from builder_s3 import ApiError
        for arn,error in ((ARN.format('ec2','instance/i-1'),missing('UnauthorizedOperation')),
                          (ARN.format('ec2','volume/vol-1'),RuntimeError('timeout')),
                          ('arn:aws:s3:::sancus-builder-20261030-x',ApiError('403')),
                          ('arn:aws:s3:::sancus-builder-20261030-x',ApiError('AccessDenied')),
                          (f'arn:aws:iam::{names.ACCOUNT}:role/sancus-builder-20261030-x',missing('AccessDenied')),
                          (ARN.format('cloudformation','stack/sancus-builder-20261030-x/1'),missing('ValidationError'))):
            with self.subTest(arn=arn,error=str(error)):
                row=self.leftover(arn,error);self.assertEqual(row['authoritative'],'describe-error')

    def test_unknown_type_or_foreign_scope_stays_leftover(self):
        for arn in (ARN.format('logs','log-group:sancus-builder-20261030-x'),ARN.format('ec2','snapshot/snap-1'),
                    'arn:aws:ec2:eu-west-1:'+names.ACCOUNT+':instance/i-1','arn:aws:ec2:us-east-1:111111111111:instance/i-1','not-an-arn'):
            with self.subTest(arn=arn):self.leftover(arn,self.instance('terminated'))


if __name__=='__main__':unittest.main()

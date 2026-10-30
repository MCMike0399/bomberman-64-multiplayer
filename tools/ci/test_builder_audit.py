import unittest

import builder_audit
import builder_namespace as names


def empty(service,operation,*arguments):
    if service=='sts':return {'Account':names.ACCOUNT,'Arn':f'arn:aws:sts::{names.ACCOUNT}:assumed-role/{names.CI_ROLE}/test'}
    return {key:[]for key in ('StackSummaries','Reservations','Volumes','NetworkInterfaces','SecurityGroups','Addresses',
            'Snapshots','Images','Roles','InstanceProfiles','Schedules','ScheduleGroups','logGroups','Buckets',
            'Parameters','DocumentIdentifiers','ResourceTagMappingList')}


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


if __name__=='__main__':unittest.main()

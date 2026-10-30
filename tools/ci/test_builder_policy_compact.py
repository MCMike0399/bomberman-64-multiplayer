import unittest

import builder_identity as identity
import builder_policy_compact as compact


class BoundaryCompaction(unittest.TestCase):
    def test_existing_api_permissions_and_conditions_are_preserved(self):
        policy=identity.policy();result=compact.compact(policy)
        pairs=lambda data:set().union(*(compact.api_pairs(row)for row in data['Statement']))
        self.assertEqual(pairs(policy),pairs(result))

    def test_tagged_group_launch_cannot_expand_to_instance_launch(self):
        tag={'StringLike':{'aws:ResourceTag/sancus-builder:run':'sancus-builder-20??????-*'}}
        rows=[{'Effect':'Allow','Action':'ec2:RunInstances','Resource':'arn:aws:ec2:us-east-1:524329886851:security-group/*','Condition':tag},
              {'Effect':'Allow','Action':'ec2:TerminateInstances','Resource':'arn:aws:ec2:us-east-1:524329886851:instance/*','Condition':tag}]
        result=compact.compact({'Version':'2012-10-17','Statement':rows})
        self.assertEqual(len(result['Statement']),2)

    def test_vendor_read_never_becomes_vendor_write(self):
        rows=[{'Effect':'Allow','Action':'s3:GetObject','Resource':'arn:aws:s3:::vendor/driver.exe'},
              {'Effect':'Allow','Action':'s3:PutObject','Resource':'arn:aws:s3:::sancus-builder-20??????-*/*'}]
        result=compact.compact({'Version':'2012-10-17','Statement':rows})
        self.assertEqual(len(result['Statement']),2)

    def test_s3_bucket_and_object_wildcards_are_not_reinterpreted(self):
        rows=[{'Effect':'Allow','Action':'s3:ListBucket','Resource':'arn:aws:s3:::sancus-builder-20??????-*'},
              {'Effect':'Allow','Action':'s3:GetObject','Resource':'arn:aws:s3:::sancus-builder-20??????-*/*'}]
        self.assertEqual(compact.compact({'Version':'2012-10-17','Statement':rows})['Statement'],rows)

    def test_unknown_scoped_action_requires_explicit_review(self):
        with self.assertRaisesRegex(ValueError,'Unmapped'):
            compact.compact({'Version':'2012-10-17','Statement':[
                {'Effect':'Allow','Action':'iam:DeleteRolePermissionsBoundary','Resource':'arn:aws:iam::524329886851:role/example'}]})


if __name__=='__main__':unittest.main()

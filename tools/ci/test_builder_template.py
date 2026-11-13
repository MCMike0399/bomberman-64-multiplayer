import base64
import json
import unittest

import builder_template
import builder_namespace as names


class CampaignTemplate(unittest.TestCase):
    def template(self):
        plan={'runId':'sancus-builder-20261030-8762d11-1234567890abcdef',
              'fixedCampaignExpiryUtc':'2026-10-30T15:00:00Z',
              'instances':[{'os':'Windows','ami':'ami-windows','rootGb':80}]}
        return builder_template.build(plan,{'Windows':'public Windows bootstrap'})

    def test_compute_waits_for_owned_bucket_policy_and_expiry_group(self):
        resources=self.template()['Resources']
        self.assertEqual(sorted(key for key,row in resources.items()if row['Type']=='AWS::EC2::Instance'),['WindowsInstance'])
        for osname in ('Windows',):
            resource=resources[osname+'Instance'];properties=resource['Properties']
            self.assertEqual(resource['DependsOn'],['StagingBucketPolicy','ExpiryGroup'])
            self.assertEqual(properties['InstanceType'],'g4dn.xlarge')
            self.assertEqual(properties['MetadataOptions']['HttpTokens'],'required')
            ebs=properties['BlockDeviceMappings'][0]['Ebs'];self.assertTrue(ebs['Encrypted']and ebs['DeleteOnTermination'])
            # 2026-11-13: one Windows instance, Name tag exactly `sancus-builder` (ownership is the run tag).
            self.assertEqual(properties['Tags'][-1],{'Key':'Name','Value':'sancus-builder'})
            self.assertEqual(properties['Tags'][0]['Key'],names.RUN_TAG)

    def test_expiry_is_the_exact_owned_id_and_self_deletes(self):
        resources=self.template()['Resources']
        statements=resources['ExpiryRole']['Properties']['Policies'][0]['PolicyDocument']['Statement']
        self.assertEqual(len(statements),1);self.assertEqual(statements[0]['Action'],'ec2:TerminateInstances')
        self.assertEqual(len(statements[0]['Resource']),1)
        self.assertEqual(sorted(key for key,row in resources.items()if row['Type']=='AWS::Scheduler::Schedule'),['WindowsExpiry'])
        for osname in ('Windows',):
            schedule=resources[osname+'Expiry']['Properties']
            self.assertNotIn('ActionAfterCompletion',schedule)
            self.assertEqual(schedule['FlexibleTimeWindow'],{'Mode':'OFF'})
            self.assertEqual(schedule['GroupName'],names.resource_name('sancus-builder-20261030-8762d11-1234567890abcdef','expiry-group'))
            self.assertIn('${'+osname+'Instance}',schedule['Target']['Input']['Fn::Sub'])

    def test_a_linux_or_second_instance_plan_is_refused(self):
        base={'runId':'sancus-builder-20261030-8762d11-1234567890abcdef','fixedCampaignExpiryUtc':'2026-10-30T15:00:00Z'}
        windows={'os':'Windows','ami':'ami-windows','rootGb':80}
        for instances in ([{'os':'Linux','ami':'ami-linux','rootGb':75},windows],[{'os':'Linux','ami':'ami-linux','rootGb':75}],
                          [windows,windows],[]):
            with self.subTest(instances=instances),self.assertRaisesRegex(ValueError,'exactly one Windows'):
                builder_template.build({**base,'instances':instances},{'Windows':'w','Linux':'l'})

    def test_guests_do_not_receive_controller_or_github_permissions(self):
        resources=self.template()['Resources']
        role=resources['AgentRole']['Properties'];text=json.dumps(role['Policies'])
        self.assertNotIn('GitHub',text);self.assertNotIn('GITHUB_TOKEN',text)
        self.assertNotIn('s3:Delete',text);self.assertNotIn('ec2:Terminate',text)
        self.assertEqual(role['PermissionsBoundary'],f'arn:aws:iam::{names.ACCOUNT}:policy/sancus-builder-boundary')


if __name__=='__main__':unittest.main()

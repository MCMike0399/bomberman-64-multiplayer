import unittest
from unittest.mock import patch

import builder_campaign as campaign
import builder_journal as journal
import builder_namespace as names


class TerminationBoundary(unittest.TestCase):
    def setUp(self):
        self.plan={'account':names.ACCOUNT,'region':names.REGION,'runId':'sancus-builder-20261030-120b573-1234567890abcdef'}
        self.sid=f"arn:aws:cloudformation:{names.REGION}:{names.ACCOUNT}:stack/{self.plan['runId']}/owned"
        self.template={'Resources':{}};self.calls=[]
        self.entry={'id':self.sid,'details':{'parameters':{},'templateSha256':journal.canonical_hash(self.template)}}
        self.row={'event':'CREATED','type':'AWS::EC2::Instance','id':'i-owned','parent':self.sid,'details':{'logicalId':'LinuxInstance'}}
        self.instance={'InstanceId':'i-owned','State':{'Name':'running'},'Tags':[{'Key':names.RUN_TAG,'Value':self.plan['runId']}],
                       'BlockDeviceMappings':[],'NetworkInterfaces':[]}

    def aws(self,plan,service,operation,*args,**kwargs):
        self.calls.append((service,operation))
        if operation=='describe-instances':return {'Reservations':[{'Instances':[self.instance]}]}
        if operation=='describe-stacks':return {'Stacks':[{'StackId':self.sid,'Parameters':[],
                    'Tags':[{'Key':names.RUN_TAG,'Value':self.plan['runId']}]}]}
        if operation=='get-template':return {'TemplateBody':self.template}
        if operation=='list-stack-resources':return {'StackResourceSummaries':[
            {'ResourceType':'AWS::EC2::Instance','PhysicalResourceId':'i-owned','LogicalResourceId':'LinuxInstance'}]}
        raise AssertionError('Unrelated metadata call: '+service+'/'+operation)

    def check(self):
        with (patch.object(campaign,'actor'),patch.object(campaign,'stack_entry',return_value=self.entry),
              patch.object(campaign,'rows',return_value=[self.row]),patch.object(campaign,'protected',return_value={'vol-protected'}),
              patch.object(campaign,'aws',side_effect=self.aws)):
            return campaign.authorize_instance(self.plan,'i-owned')

    def test_exact_instance_proof_never_reads_unrelated_iam_or_s3(self):
        self.assertEqual(self.check(),'running')
        self.assertEqual({service for service,_ in self.calls},{'ec2','cloudformation'})

    def test_protected_attachment_prevents_termination(self):
        self.instance['BlockDeviceMappings']=[{'Ebs':{'VolumeId':'vol-protected'}}]
        with self.assertRaisesRegex(ValueError,'protected attachment'):self.check()

    def test_wrong_region_stack_arn_is_not_an_owned_target(self):
        self.entry['id']=self.sid.replace('us-east-1','us-west-2')
        with self.assertRaisesRegex(ValueError,'ARN account/region'):self.check()

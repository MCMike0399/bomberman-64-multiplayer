import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import builder_arming as arming
import builder_namespace as names
import builder_journal as journal
import builder_template


class ExactExpiry(unittest.TestCase):
    def setUp(self):
        self.plan={'runId':'sancus-builder-20261030-8762d11-1234567890abcdef','payloadSource':'a'*40,
                   'fixedCampaignExpiryUtc':'2026-10-30T15:00:00Z','account':names.ACCOUNT,'region':names.REGION,
                   'instances':[{'os':'Linux','ami':'ami-linux','rootGb':75},{'os':'Windows','ami':'ami-windows','rootGb':80}]}
        self.entry={'details':{'parameters':{'ExpiresAt':'2026-10-30T15:00:00'}}}
        self.logical={'LinuxInstance':{'id':'i-linux'},'WindowsInstance':{'id':'i-windows'},
                      'ExpiryRole':{'id':names.resource_name(self.plan['runId'],'expiry')}}
        resources=builder_template.build(self.plan,{'Linux':'bootstrap','Windows':'bootstrap'})['Resources']
        role=resources['ExpiryRole']['Properties']
        self.role={'RoleName':role['RoleName'],'Arn':f"arn:aws:iam::{names.ACCOUNT}:role/{role['RoleName']}",
                   'AssumeRolePolicyDocument':role['AssumeRolePolicyDocument'],
                   'PermissionsBoundary':{'PermissionsBoundaryArn':role['PermissionsBoundary']}}
        self.inline=[role['Policies'][0]['PolicyName']]
        self.policy=copy.deepcopy(role['Policies'][0]['PolicyDocument'])
        statement=self.policy['Statement'][0]
        statement['Resource']=[f'arn:aws:ec2:{names.REGION}:{names.ACCOUNT}:instance/i-'+osname.lower()for osname in ('Linux','Windows')]
        statement['Condition']['StringEquals']['ec2:ResourceTag/'+names.RUN_TAG]=self.plan['runId']
        self.schedules={}
        for osname in ('Linux','Windows'):
            schedule=copy.deepcopy(resources[osname+'Expiry']['Properties'])
            schedule['ScheduleExpression']='at('+self.entry['details']['parameters']['ExpiresAt']+')'
            schedule['Target']['RoleArn']=self.role['Arn']
            schedule['Target']['Input']=json.dumps({'InstanceIds':[self.logical[osname+'Instance']['id']]})
            schedule['ActionAfterCompletion']='DELETE'
            self.schedules[osname]=schedule

    def check(self):return arming.verify_expiry(self.plan,self.entry,self.logical,self.role,self.policy,self.inline,self.schedules)

    def test_actual_two_targets_and_bound_role_pass(self):self.assertEqual(self.check(),['i-linux','i-windows'])

    def test_wrong_target_deadline_retry_group_and_auto_delete_fail(self):
        original=copy.deepcopy(self.schedules)
        changes=[('ActionAfterCompletion','NONE'),('ScheduleExpression','at(2027-01-01T00:00:00)'),
                 ('GroupName','default'),('State','DISABLED')]
        for key,value in changes:
            self.schedules=copy.deepcopy(original);self.schedules['Windows'][key]=value
            with self.subTest(key=key),self.assertRaises(ValueError):self.check()
        self.schedules=copy.deepcopy(original);self.schedules['Windows']['Target']['Input']=json.dumps({'InstanceIds':['i-protected']})
        with self.assertRaises(ValueError):self.check()

    def test_broader_role_and_changed_boundary_or_trust_fail(self):
        original=copy.deepcopy(self.role)
        self.role['AssumeRolePolicyDocument']['Statement'][0]['Principal']={'AWS':'*'}
        with self.assertRaises(ValueError):self.check()
        self.role=copy.deepcopy(original);self.role['PermissionsBoundary']['PermissionsBoundaryArn']='foreign'
        with self.assertRaises(ValueError):self.check()
        self.role=original;self.policy['Statement'][0]['Resource']=['*']
        with self.assertRaises(ValueError):self.check()

    def test_prepared_receipt_cannot_move_expiry_or_inputs(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);arming.configure(root);file=root/'input.bin';file.write_bytes(b'fixed')
            template={'Resources':{}};(root/'template.json').write_text(json.dumps(template))
            receipt={**self.plan,'preparationComplete':True,'planSha256':journal.canonical_hash(self.plan),
                     'templateSha256':journal.canonical_hash(template),
                     'files':[{'path':str(file),'sha256':arming.hashlib.sha256(b'fixed').hexdigest()}]}
            (root/'prepared.json').write_text(json.dumps(receipt));arming.verify_prepared(self.plan)
            with self.assertRaisesRegex(ValueError,'expiry'):
                arming.verify_prepared({**self.plan,'fixedCampaignExpiryUtc':'2026-10-31T15:00:00Z'})
            file.write_bytes(b'changed')
            with self.assertRaisesRegex(ValueError,'input changed'):arming.verify_prepared(self.plan)


if __name__=='__main__':unittest.main()

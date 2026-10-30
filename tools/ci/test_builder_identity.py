import copy
import json
import unittest

import builder_identity as identity
import builder_namespace as names


class BuilderIdentity(unittest.TestCase):
    def test_managed_boundary_fits_iam_and_has_identical_permissions(self):
        boundary=identity.boundary()
        self.assertLessEqual(len(json.dumps(boundary,separators=(',',':'))),6144)
        def permissions(data):
            expanded=[]
            for row in data['Statement']:
                base={key:value for key,value in row.items()if key not in ('Sid','Action')}
                actions=row['Action']if isinstance(row['Action'],list)else[row['Action']]
                expanded.extend(json.dumps({**base,'Action':action},sort_keys=True)for action in actions)
            return sorted(expanded)
        self.assertEqual(permissions(boundary),permissions(identity.policy()))

    def test_only_owned_role_and_boundary_exist_provider_is_reference(self):
        resources=identity.template()['Resources']
        self.assertEqual({row['Type']for row in resources.values()},
                         {'AWS::IAM::ManagedPolicy','AWS::IAM::Role'})
        self.assertEqual(len(resources),2)
        role=resources['ControllerRole']['Properties']
        self.assertEqual(role['RoleName'],'sancus-builder-ci')
        self.assertEqual(role['PermissionsBoundary'],{'Ref':'Boundary'})
        self.assertEqual(resources['Boundary']['Properties']['ManagedPolicyName'],'sancus-builder-boundary')
        trust=role['AssumeRolePolicyDocument']['Statement'][0]
        self.assertEqual(trust['Principal']['Federated'],names.OIDC_PROVIDER)
        self.assertEqual(trust['Condition']['StringEquals'],{
            'token.actions.githubusercontent.com:aud':'sts.amazonaws.com',
            'token.actions.githubusercontent.com:sub':identity.OIDC_SUBJECT})
        self.assertIn('repo:MCMike0399@40257172/bomberman-64-multiplayer@1426399566:',identity.OIDC_SUBJECT)
        self.assertTrue(identity.OIDC_SUBJECT.endswith(':job_workflow_ref:'+identity.WORKFLOW))
        self.assertTrue(identity.WORKFLOW.endswith('/.github/workflows/builder-controller.yml@refs/heads/main'))

    def test_generated_names_are_plain_builder_including_inline_policy(self):
        template=identity.template()
        names.validate_aws_names(template)
        role=template['Resources']['ControllerRole']['Properties']
        for policy in role['Policies']:
            self.assertTrue(policy['PolicyName'].startswith(names.PREFIX))
            self.assertNotIn('gpu',policy['PolicyName']);self.assertNotIn('blast',policy['PolicyName'])
        text=json.dumps(identity.policy())
        self.assertNotIn('sancus:*',text);self.assertNotIn('sancus-*',text)
        self.assertNotIn('sancus:',text)

    def test_runtime_tagging_cannot_adopt_existing_instances(self):
        statements={row['Sid']:row for row in identity.policy()['Statement']}
        tag=statements['TagOnlyDuringCreation']
        self.assertEqual(tag['Condition']['StringEquals']['ec2:CreateAction'],
                         ['RunInstances','CreateSecurityGroup'])
        launch=statements['LaunchTaggedSmallInstances']
        self.assertEqual(launch['Condition']['StringEquals']['ec2:InstanceType'],'g4dn.xlarge')
        self.assertEqual(launch['Condition']['StringEquals']['ec2:MetadataHttpTokens'],'required')
        self.assertEqual(launch['Condition']['StringLike'],
                         {'aws:RequestTag/sancus-builder:run':'sancus-builder-20??????-*'})
        self.assertEqual(statements['TerminateOnlyOwnedInstances']['Condition']['StringLike'],
                         {'aws:ResourceTag/sancus-builder:run':'sancus-builder-20??????-*'})

    def test_audit_reads_are_retained_and_no_log_creation_is_granted(self):
        actions={action for row in identity.policy()['Statement']for action in
                 (row['Action']if isinstance(row['Action'],list)else[row['Action']])}
        self.assertIn('logs:DescribeLogGroups',actions)
        self.assertIn('scheduler:ListSchedules',actions)
        self.assertFalse({'logs:CreateLogGroup','logs:CreateLogStream','logs:PutLogEvents'}&actions)

    def test_child_roles_cannot_edit_or_pass_standing_identity(self):
        statements=identity.policy()['Statement']
        for row in statements:
            actions=row['Action']if isinstance(row['Action'],list)else[row['Action']]
            if any(action.startswith('iam:')and action not in ('iam:ListRoles','iam:ListInstanceProfiles')for action in actions):
                resources=row['Resource']if isinstance(row['Resource'],list)else[row['Resource']]
                self.assertTrue(all(names.RUN_GLOB in resource for resource in resources))
            self.assertNotIn('iam:PutRolePermissionsBoundary',actions)
            self.assertNotIn('iam:DeleteRolePermissionsBoundary',actions)
        create=next(row for row in statements if row['Sid']=='CreateBoundedOwnedRole')
        self.assertEqual(create['Condition']['ArnEquals']['iam:PermissionsBoundary'],
                         'arn:aws:iam::524329886851:policy/sancus-builder-boundary')

    def test_actual_template_regressions_are_rejected(self):
        for value in ('sancus-builder-gpu-ci','sancus-builder-blast-ci','sancus-runner-ci'):
            data=copy.deepcopy(identity.template())
            data['Resources']['ControllerRole']['Properties']['RoleName']=value
            with self.subTest(value=value),self.assertRaises(ValueError):names.validate_aws_names(data)
        data=identity.template()
        data['Resources']['ControllerRole']['Properties']['Tags']=[{'Key':'sancus:job','Value':'example'}]
        with self.assertRaises(ValueError):names.validate_aws_names(data)


if __name__=='__main__':unittest.main()

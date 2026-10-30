import copy
import json
import unittest

import builder_identity as identity
import builder_namespace as names


class BuilderIdentity(unittest.TestCase):
    def test_staging_boundary_fits_and_preserves_api_valid_permissions(self):
        import builder_policy_compact as compact
        policy=identity.policy(staging=True);boundary=identity.boundary(staging=True)
        pairs=lambda data:set().union(*(compact.api_pairs(row)for row in data['Statement']))
        self.assertEqual(pairs(policy),pairs(boundary))
        self.assertLessEqual(len(json.dumps(boundary,separators=(',',':'))),6144)
        def s3_rows(data):
            result=[]
            for row in data['Statement']:
                actions=compact.sequence(row['Action'])
                if any(action.startswith('s3:')for action in actions):
                    result.append(json.dumps({'Effect':row['Effect'],'Action':actions,
                        'Resource':compact.sequence(row['Resource']),'Condition':row.get('Condition',{})},sort_keys=True))
            return sorted(result)
        self.assertEqual(s3_rows(policy),s3_rows(boundary))
        resources=identity.template(staging=True)['Resources']
        self.assertEqual(len(resources),2)

    def test_managed_boundary_fits_iam_and_has_identical_permissions(self):
        import builder_policy_compact as compact
        boundary=identity.boundary()
        self.assertLessEqual(len(json.dumps(boundary,separators=(',',':'))),6144)
        pairs=lambda data:set().union(*(compact.api_pairs(row)for row in data['Statement']))
        self.assertEqual(pairs(boundary),pairs(identity.policy()))

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

    def test_security_group_has_no_ingress_rules_or_ingress_permission(self):
        import fnmatch
        actions={action for row in identity.policy(staging=True)['Statement']for action in
                 (row['Action']if isinstance(row['Action'],list)else[row['Action']])}
        for forbidden in ('ec2:AuthorizeSecurityGroupIngress','ec2:ModifySecurityGroupRules','ec2:UpdateSecurityGroupRuleDescriptionsIngress'):
            self.assertFalse(any(fnmatch.fnmatchcase(forbidden,pattern)for pattern in actions),forbidden)
        import builder_template
        source=open(builder_template.__file__).read()
        self.assertNotIn('SecurityGroupIngress',source)

    def test_audit_reads_are_retained_and_no_log_creation_is_granted(self):
        actions={action for row in identity.policy()['Statement']for action in
                 (row['Action']if isinstance(row['Action'],list)else[row['Action']])}
        import fnmatch
        granted=lambda action:any(fnmatch.fnmatchcase(action,pattern)for pattern in actions)
        for read in ('logs:DescribeLogGroups','scheduler:ListSchedules','scheduler:ListScheduleGroups','ssm:ListCommands',
                     'ssm:ListCommandInvocations','ssm:DescribeInstanceInformation','cloudformation:DescribeStacks','iam:GetRolePolicy'):
            self.assertTrue(granted(read),read)
        self.assertFalse({'logs:CreateLogGroup','logs:CreateLogStream','logs:PutLogEvents'}&actions)
        # Wildcards are read-only patterns only, and never reach SSM parameters (secrets).
        for pattern in (action for action in actions if '*'in action):
            self.assertRegex(pattern.split(':',1)[1],r'^(Describe|List|Get)')
        for secret in ('ssm:GetParameter','ssm:GetParameters','ssm:GetParametersByPath','ssm:GetSecretValue'):
            self.assertFalse(granted(secret),secret)

    def test_child_roles_cannot_edit_or_pass_standing_identity(self):
        statements=identity.policy()['Statement']
        for row in statements:
            actions=row['Action']if isinstance(row['Action'],list)else[row['Action']]
            # Read-only exceptions: account-wide listings/quota, and reading the boundary itself.
            if row['Sid']=='ReadOwnBoundary':
                self.assertEqual((actions,row['Resource']),(['iam:GetPolicy'],'arn:aws:iam::524329886851:policy/sancus-builder-boundary'));continue
            if any(action.startswith('iam:')and action not in ('iam:ListRoles','iam:ListInstanceProfiles','iam:GetAccountSummary')for action in actions):
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

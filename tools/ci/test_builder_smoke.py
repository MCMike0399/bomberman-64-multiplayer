import base64
import datetime
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import builder_approval
import builder_arming
import builder_campaign as campaign
import builder_identity
import builder_journal as journal
import builder_namespace as names
import builder_smoke as smoke
import builder_template

RUN='sancus-builder-20261030-8762d11-1234567890abcdef'
DRY='An error occurred (DryRunOperation) when calling the RunInstances operation: Request would have succeeded, but DryRun flag is set.'
DENIED=('An error occurred (UnauthorizedOperation) when calling the RunInstances operation: You are not authorized to perform '
        'this operation. Encoded authorization failure message: AbCdEf0123')


def plan():
    return {'runId':RUN,'payloadSource':'a'*40,'fixedCampaignExpiryUtc':'2026-10-30T15:00:00+00:00','instances':smoke.instances(),
            'profile':'sancus','region':names.REGION,'account':names.ACCOUNT,'permissionsSmoke':True,
            'references':{'vpc':'vpc-1','subnet':'subnet-1'}}


def real():
    value=plan();return builder_template.build(value,smoke.placeholder_userdata(value))


class SmokeTemplate(unittest.TestCase):
    def test_removes_exactly_the_two_instances_and_keeps_every_other_resource_identical(self):
        original=real();derived,removed=smoke.derive(original)
        self.assertEqual(sorted(removed),['LinuxInstance','WindowsInstance'])
        self.assertEqual(set(original['Resources'])-set(derived['Resources']),{'LinuxInstance','WindowsInstance'})
        self.assertEqual(set(derived['Resources'])|set(removed),set(original['Resources']))
        self.assertEqual(original['Parameters'],derived['Parameters'])
        changed={'ExpiryRole','LinuxExpiry','WindowsExpiry'}
        for key in set(derived['Resources'])-changed:
            with self.subTest(resource=key):self.assertEqual(derived['Resources'][key],original['Resources'][key])
        # Expiry role: only the instance ARNs change; trust, boundary, name and tags are the real ones.
        before=json.loads(json.dumps(original['Resources']['ExpiryRole']));after=derived['Resources']['ExpiryRole']
        before['Properties']['Policies'][0]['PolicyDocument']['Statement'][0].pop('Resource')
        resources=after['Properties']['Policies'][0]['PolicyDocument']['Statement'][0].pop('Resource')
        self.assertEqual(json.loads(json.dumps(after)),before)
        self.assertEqual(resources,[smoke.instance_arn(smoke.DUMMY_INSTANCES[osname])for osname in smoke.SYSTEMS])
        for osname in smoke.SYSTEMS:
            old=json.loads(json.dumps(original['Resources'][osname+'Expiry']['Properties']));new=derived['Resources'][osname+'Expiry']['Properties']
            self.assertEqual(new['State'],'DISABLED');self.assertEqual(old.pop('State'),'ENABLED')
            self.assertEqual(json.loads(new['Target']['Input']),{'InstanceIds':[smoke.DUMMY_INSTANCES[osname]]})
            old['Target'].pop('Input')
            self.assertEqual({**new,'Target':{k:v for k,v in new['Target'].items()if k!='Input'}},{**old,'State':'DISABLED'})
            self.assertEqual(new['ScheduleExpression'],{'Fn::Sub':'at(${ExpiresAt})'})

    def test_names_tags_and_boundary_follow_the_real_owned_namespace(self):
        derived,_=smoke.derive(real());text=json.dumps(derived)
        self.assertEqual(smoke.references(derived,{'LinuxInstance','WindowsInstance'}),[])
        self.assertNotIn('Instance}',text)
        for key in ('AgentRole','ExpiryRole'):
            self.assertEqual(derived['Resources'][key]['Properties']['PermissionsBoundary'],f'arn:aws:iam::{names.ACCOUNT}:policy/{names.BOUNDARY}')
        for row in derived['Resources'].values():
            for tag in row['Properties'].get('Tags',[]):
                if tag['Key']==names.RUN_TAG:self.assertIn(tag['Value'],({'Ref':'RunId'},RUN))
        self.assertEqual(derived['Resources']['StagingBucket']['Properties']['BucketName'],RUN+'-'+names.ACCOUNT)

    def test_dummy_instances_are_valid_never_real_ids(self):
        for value in smoke.DUMMY_INSTANCES.values():self.assertRegex(value,r'^i-0{16}[01]$')

    def test_any_new_instance_reference_fails_closed(self):
        original=real();original['Resources']['QaSecurityGroup']['DependsOn']=['LinuxInstance']
        with self.assertRaisesRegex(ValueError,'still references'):smoke.derive(original)
        original=real();original['Resources']['Extra']={'Type':'AWS::EC2::Instance','Properties':{}}
        with self.assertRaisesRegex(ValueError,'exactly'):smoke.derive(original)

    def test_instances_match_the_paid_approval_pins(self):
        spending={'closedEstimateUsd':4.8,'budgetUsd':200}
        approval=builder_approval.build('a'*40,'1','2026-10-30T15:00:00+00:00',spending)
        self.assertEqual(smoke.instances(),approval['instances'])
        self.assertEqual([item['ami']for item in smoke.instances()],list(builder_identity.AMIS))

    def test_placeholder_user_data_has_the_real_bootstrap_shape(self):
        import builder_bootstrap
        value=plan();placeholder=smoke.placeholder_userdata(value)
        actual=builder_bootstrap.userdata(value,'1'*64,'2'*64,{'url':'https://awscli.amazonaws.com/AWSCLIV2.msi','sha256':'3'*64})
        for osname in smoke.SYSTEMS:self.assertEqual(len(placeholder[osname]),len(actual[osname]))


class DryRunParameters(unittest.TestCase):
    physical={'QaSecurityGroup':'sg-0abc','AgentProfile':RUN+'-profile','RunId':RUN,'SubnetId':'subnet-1'}

    def parsed(self,osname):
        arguments=smoke.dry_run_arguments(real()['Resources'][osname+'Instance'],self.physical,campaign.stack_tags(plan()))
        self.assertEqual(arguments[0],'--dry-run')
        return {arguments[i]:arguments[i+1]for i in range(1,len(arguments),2)}

    def test_both_launches_match_the_real_template_instance_properties(self):
        template=real()
        for osname in smoke.SYSTEMS:
            with self.subTest(os=osname):
                properties=template['Resources'][osname+'Instance']['Properties'];values=self.parsed(osname)
                item=next(row for row in smoke.instances()if row['os']==osname)
                self.assertEqual(values['--image-id'],item['ami']);self.assertEqual(values['--instance-type'],'g4dn.xlarge')
                self.assertEqual(values['--count'],'1')
                self.assertEqual(json.loads(values['--iam-instance-profile']),{'Name':RUN+'-profile'})
                self.assertEqual(values['--instance-initiated-shutdown-behavior'],'terminate')
                self.assertEqual(json.loads(values['--metadata-options']),{'HttpTokens':'required','HttpEndpoint':'enabled','HttpPutResponseHopLimit':1})
                self.assertEqual(json.loads(values['--network-interfaces']),[{'DeviceIndex':0,'SubnetId':'subnet-1','AssociatePublicIpAddress':True,
                                                                            'DeleteOnTermination':True,'Groups':['sg-0abc']}])
                mapping=json.loads(values['--block-device-mappings'])
                self.assertEqual(mapping,properties['BlockDeviceMappings'])
                self.assertEqual(mapping[0]['Ebs'],{'VolumeSize':item['rootGb'],'VolumeType':'gp3','Encrypted':True,'DeleteOnTermination':True})
                self.assertEqual(values['--user-data'].encode(),base64.b64decode(properties['UserData']))
                specs=json.loads(values['--tag-specifications'])
                self.assertEqual([row['ResourceType']for row in specs],['instance','volume'])
                tags={row['Key']:row['Value']for row in specs[0]['Tags']}
                self.assertEqual(tags,{names.RUN_TAG:RUN,names.EXPIRY_TAG:'2026-10-30T15:00:00+00:00','Name':names.instance_name(osname.lower()),
                                       names.TAG_PREFIX+'source':'a'*40})
                self.assertEqual(specs[0]['Tags'],specs[1]['Tags'])

    def test_template_launch_drift_or_unresolved_reference_refuses(self):
        resource=real()['Resources']['LinuxInstance']
        changed=json.loads(json.dumps(resource));changed['Properties']['KeyName']='x'
        with self.assertRaisesRegex(ValueError,'launch properties changed'):smoke.dry_run_arguments(changed,self.physical,[])
        with self.assertRaisesRegex(ValueError,'Unresolved'):smoke.dry_run_arguments(resource,{'RunId':RUN},[])


class DryRunVerdict(unittest.TestCase):
    def call(self,*responses):
        calls=[]
        def call(plan,service,operation,*arguments):
            calls.append((service,operation,arguments));value=responses[min(len(calls),len(responses))-1]
            if isinstance(value,Exception):raise value
            return value
        return call,calls

    def test_dry_run_operation_passes_and_unauthorized_fails(self):
        call,calls=self.call(RuntimeError('ec2/run-instances: '+DRY))
        record=smoke.dry_run(plan(),'Linux',['--dry-run'],call=call)
        self.assertEqual((record['result'],record['passed'],record['errorCode']),('authorized',True,'DryRunOperation'))
        self.assertEqual(calls,[('ec2','run-instances',('--dry-run',))])
        call,_=self.call(RuntimeError('ec2/run-instances: '+DENIED))
        record=smoke.dry_run(plan(),'Windows',['--dry-run'],call=call)
        self.assertEqual((record['result'],record['passed'],record['errorCode']),('unauthorized',False,'UnauthorizedOperation'))
        self.assertNotIn('AbCdEf0123',record['message'])

    def test_other_errors_and_impossible_success_fail(self):
        for response in (RuntimeError('ec2/run-instances: An error occurred (InvalidAMIID.NotFound) x'),{'Instances':[{'InstanceId':'i-1'}]}):
            call,_=self.call(response)
            self.assertFalse(smoke.dry_run(plan(),'Linux',['--dry-run'],call=call)['passed'])

    def test_new_instance_profile_propagation_is_retried_within_its_bound(self):
        late=RuntimeError('ec2/run-instances: An error occurred (InvalidParameterValue) when calling the RunInstances operation: '
                          'Value (x) for parameter iamInstanceProfile.name is invalid. Invalid IAM Instance Profile name')
        call,calls=self.call(late,late,RuntimeError(DRY))
        record=smoke.dry_run(plan(),'Linux',['--dry-run'],call=call,sleep=lambda seconds:None,clock=lambda:0,deadline=10)
        self.assertEqual((record['passed'],record['attempts'],len(calls)),(True,3,3))
        call,calls=self.call(late)
        self.assertFalse(smoke.dry_run(plan(),'Linux',['--dry-run'],call=call,sleep=lambda seconds:None,clock=lambda:20,deadline=10)['passed'])

    def test_never_without_dry_run_in_the_smoke_or_the_campaign_wrapper(self):
        with self.assertRaises(ValueError):smoke.dry_run(plan(),'Linux',['--count','1'],call=lambda *a:self.fail('called'))
        with patch.object(campaign.subprocess,'run',side_effect=AssertionError('AWS must not be called')),\
             patch.dict(campaign.os.environ,{'BUILDER_DRY_RUN':'false'}):
            for operation,arguments in (('run-instances',('--count','1')),('run-instances',('--dry-run','--no-dry-run')),
                                        ('terminate-instances',('--instance-ids','i-1'))):
                with self.subTest(operation=operation),self.assertRaisesRegex(ValueError,'Permissions smoke'):
                    campaign.aws(plan(),'ec2',operation,*arguments)


class SmokeFlow(unittest.TestCase):
    def state(self,root):
        root=Path(root);value={**plan(),'timing':{'jobStartedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat()}}
        (root/'plan.json').write_text(json.dumps(value));(root/'campaign-template.json').write_text(json.dumps(real()))
        return root

    def flow(self,launch_error):
        with tempfile.TemporaryDirectory()as root:
            root=self.state(root);launches=[]
            def aws(plan,service,operation,*arguments):
                launches.append(arguments);raise RuntimeError('ec2/run-instances: '+launch_error)
            physical={'QaSecurityGroup':'sg-1','AgentProfile':RUN+'-profile','ExpiryRole':RUN+'-expiry'}
            with patch.object(campaign,'submit_intent',return_value='arn:stack'),patch.object(campaign,'stack_entry',return_value={'id':'arn:stack'}),\
                 patch.object(campaign,'wait_stack',return_value='CREATE_COMPLETE'),patch.object(campaign,'sync',return_value=[1,2]),\
                 patch.object(smoke.arming,'set_schedule_auto_delete'),patch.object(smoke,'verify_schedules',return_value={}),\
                 patch.object(smoke.transfer,'upload_inputs'),patch.object(smoke,'owned_physical',return_value=physical),\
                 patch.object(campaign,'aws',side_effect=aws):
                result=smoke.run(root)
            written=json.loads((root/'permissions-smoke.json').read_text())
        return result,written,launches

    def test_all_checks_and_both_authorized_dry_runs_pass_without_allocation(self):
        result,written,launches=self.flow(DRY)
        self.assertTrue(result['passed']);self.assertEqual(written['passed'],True)
        self.assertEqual([row['os']for row in written['dryRuns']],['Linux','Windows'])
        self.assertEqual((written['instancesAllocated'],written['paidAttempt']),(0,False))
        self.assertEqual(len(launches),2)
        for arguments in launches:self.assertIn('--dry-run',arguments)

    def test_an_unauthorized_launch_turns_the_smoke_red_but_records_both(self):
        result,written,_=self.flow(DENIED)
        self.assertFalse(result['passed']);self.assertFalse(written['checks']['dry-run-linux']['passed'])
        self.assertEqual([row['result']for row in written['dryRuns']],['unauthorized','unauthorized'])

    def test_failed_create_skips_everything_else_and_stays_red(self):
        with tempfile.TemporaryDirectory()as root:
            root=self.state(root)
            with patch.object(campaign,'submit_intent',side_effect=RuntimeError('cloudformation/create-stack: AccessDenied')):
                result=smoke.run(root)
        self.assertFalse(result['passed']);self.assertEqual(list(result['checks']),['create-stack'])


class SmokePreparation(unittest.TestCase):
    def test_preparation_writes_the_derived_template_and_the_exact_paid_style_intent(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);now=datetime.datetime.now(datetime.timezone.utc)
            (root/'job-clock.json').write_text(json.dumps({'jobStartedAtUtc':now.isoformat(),'jobTimeoutSeconds':1800,'jobMarginSeconds':900,
                'cleanupBudgetSeconds':1200,'fixedCampaignExpiryUtc':(now+datetime.timedelta(minutes=160)).isoformat(),
                'controllerRunId':'7','controllerRunAttempt':'1','controllerJobId':'campaign'}))
            import builder_s3,builder_prepare
            def aws(plan,service,operation,*arguments):
                if operation=='get-caller-identity':return {'Account':names.ACCOUNT,'Arn':'arn:aws:sts::x:assumed-role/sancus-builder-ci/s'}
                if operation=='list-buckets':return {'Buckets':[]}
                if operation=='head-bucket':raise builder_s3.ApiError('404')
                raise AssertionError(operation)
            environment={'SANCUS_BUILDER_VPC_ID':'vpc-0a','SANCUS_BUILDER_SUBNET_ID':'subnet-0b'}
            with patch.dict(smoke.os.environ,environment),patch.object(campaign,'aws',side_effect=aws),\
                 patch.object(smoke.builder_preflight,'inspect',return_value={'passed':False,'errors':[{'check':'quota','error':'x'}]}),\
                 patch.object(builder_prepare,'protected',return_value=[]):
                value=smoke.prepare(root,hosted=False)
            self.assertTrue(value['permissionsSmoke']);self.assertRegex(value['runId'],names.RUN_PATTERN)
            self.assertEqual(value['estimatedCampaignUsd'],0)
            template=json.loads((root/'template.json').read_text())
            self.assertFalse(any(row['Type']=='AWS::EC2::Instance'for row in template['Resources'].values()))
            builder_arming.configure(root);builder_arming.verify_prepared(value)
            rows=journal.read_journal(root/'creation-manifest.jsonl',value)
            self.assertEqual([(row['event'],row['type'])for row in rows],[('INTENT','AWS::CloudFormation::Stack')])
            self.assertEqual(rows[0]['details']['templateSha256'],journal.canonical_hash(template))

    def test_blocking_preflight_refuses_before_any_intent(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);now=datetime.datetime.now(datetime.timezone.utc)
            (root/'job-clock.json').write_text(json.dumps({'jobStartedAtUtc':now.isoformat(),'jobTimeoutSeconds':1800,'jobMarginSeconds':900,
                'cleanupBudgetSeconds':1200,'fixedCampaignExpiryUtc':(now+datetime.timedelta(minutes=160)).isoformat(),
                'controllerRunId':'7','controllerRunAttempt':'1','controllerJobId':'campaign'}))
            with patch.dict(smoke.os.environ,{'SANCUS_BUILDER_VPC_ID':'vpc-0a','SANCUS_BUILDER_SUBNET_ID':'subnet-0b'}),\
                 patch.object(smoke.builder_preflight,'inspect',return_value={'passed':False,'errors':[{'check':'leftover-discovery','error':'x'}]}):
                with self.assertRaisesRegex(ValueError,'leftovers'):smoke.prepare(root,hosted=False)
            self.assertFalse((root/'creation-manifest.jsonl').exists());self.assertFalse((root/'plan.json').exists())


if __name__=='__main__':unittest.main()

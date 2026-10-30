import datetime
import copy,datetime,json,subprocess,tempfile,unittest
from pathlib import Path
from unittest import mock
import builder_campaign as campaign
import builder_journal as safety
import builder_namespace as names

class RecoveryChecks(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name)
  self.plan={'account':'123456789012','region':'us-east-1','runId':'sancus-builder-20261030-8762d11-1234567890abcdef','payloadSource':'a'*40}
  self.template={'Resources':{}};self.params={'RunId':self.plan['runId']};self.arn='arn:aws:cloudformation:us-east-1:123456789012:stack/sancus-builder-20261030-8762d11-1234567890abcdef/uuid'
  self.journal=self.root/'manifest.jsonl';self.journal.touch()
  patch=mock.patch.object(campaign,'JOURNAL',self.journal);patch.start();self.addCleanup(patch.stop)
  patch=mock.patch.object(campaign,'actor',return_value={'Account':self.plan['account']});patch.start();self.addCleanup(patch.stop)
  self.intent=safety.append(self.journal,self.plan,'INTENT','AWS::CloudFormation::Stack',self.plan['runId'],{'templateSha256':safety.canonical_hash(self.template),'parameters':self.params,'requestToken':'exact-token','expiresAtUtc':'2026-10-30T00:00:00+00:00'})
  self.stack={'StackId':self.arn,'Tags':[{'Key':names.RUN_TAG,'Value':self.plan['runId']},{'Key':names.TAG_PREFIX+'source','Value':'a'*40}],'Parameters':[{'ParameterKey':'RunId','ParameterValue':self.plan['runId']}]}
 def api(self,plan,service,operation,*args,**kwargs):
  if operation=='describe-stacks':return {'Stacks':[self.stack]}
  if operation=='get-template':return {'TemplateBody':copy.deepcopy(self.template)}
  if operation=='describe-stack-events':return {'StackEvents':[{'ResourceType':'AWS::CloudFormation::Stack','ResourceStatus':'CREATE_IN_PROGRESS','ClientRequestToken':'exact-token'}]}
  raise AssertionError(operation)
 def test_accepted_create_lost_response_is_reconciled_from_exact_intent(self):
  with mock.patch.object(campaign,'aws',side_effect=self.api):result=campaign.reconcile(self.plan)
  self.assertEqual(result['id'],self.arn);self.assertEqual(campaign.stack_entry(self.plan)['id'],self.arn)
 def test_never_accepted_request_does_not_invent_created_resources(self):
  with mock.patch.object(campaign,'aws',return_value=None):self.assertIsNone(campaign.reconcile(self.plan))
  self.assertFalse(any(r['event']=='CREATED'for r in campaign.rows(self.plan)))
 def test_different_request_token_cannot_adopt_stack(self):
  def api(*args,**kwargs):
   result=self.api(*args,**kwargs)
   if args[2]=='describe-stack-events':result['StackEvents'][0]['ClientRequestToken']='another-request'
   return result
  with mock.patch.object(campaign,'aws',side_effect=api),self.assertRaises(ValueError):campaign.reconcile(self.plan)
 def test_actual_launch_timeout_after_acceptance_recovers_before_finally(self):
  self.plan['fixedCampaignExpiryUtc']=(datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(hours=2,minutes=30)).isoformat()
  self.plan['references']={'vpc':'vpc','subnet':'subnet'}
  self.plan.update(estimatedCampaignUsd=5.25,priorEstimateUsd=4.80,budgetUsd=25)
  # New launch starts without an earlier intent, but with a real readiness file.
  self.journal.write_text('');(self.root/'ready.json').write_text(json.dumps({'payloadSource':self.plan['payloadSource'],'ready':True}));(self.root/'template.json').write_text(json.dumps(self.template))
  def api(plan,service,operation,*args,**kwargs):
   if operation=='create-stack':raise subprocess.TimeoutExpired('aws',100)
   details=campaign.rows(self.plan)[0]['details']
   if operation=='describe-stacks':return {'Stacks':[{**self.stack,'Parameters':[{'ParameterKey':k,'ParameterValue':v}for k,v in details['parameters'].items()]}]}
   if operation=='get-template':return {'TemplateBody':self.template}
   if operation=='describe-stack-events':return {'StackEvents':[{'ResourceType':'AWS::CloudFormation::Stack','ClientRequestToken':details['requestToken']}]}
   raise AssertionError(operation)
  with mock.patch.object(campaign,'ROOT',self.root),mock.patch('builder_arming.verify_prepared',return_value={}),mock.patch.object(campaign.builder_s3,'require_unused_name'),mock.patch.object(campaign,'aws',side_effect=api)as call:
   with self.assertRaises(subprocess.TimeoutExpired):campaign.launch(self.plan)
   self.assertEqual(campaign.stack_entry(self.plan)['id'],self.arn)
   with self.assertRaises(ValueError):campaign.launch(self.plan)
   self.assertEqual(sum(c.args[2]=='create-stack'for c in call.call_args_list),1)
 def test_late_iam_confirmation_does_not_remove_replacement_guard(self):
  campaign.record_created(self.plan,'AWS::IAM::Role','role-own',{'state':'absent','cfnStackId':self.arn},self.arn)
  campaign.record_created(self.plan,'AWS::IAM::Role','role-own',{'uniqueId':'first','cfnStackId':self.arn,'ownershipVerified':True},self.arn)
  effective=safety.effective_created(campaign.rows(self.plan));self.assertEqual(effective[('AWS::IAM::Role','role-own')]['details']['uniqueId'],'first')
  with self.assertRaises(ValueError):campaign.record_created(self.plan,'AWS::IAM::Role','role-own',{'uniqueId':'replacement','cfnStackId':self.arn,'ownershipVerified':True},self.arn)
if __name__=='__main__':unittest.main()

class WaitAndMissingChecks(unittest.TestCase):
 def test_creation_can_exceed_old_100_second_waiter_limit(self):
  clock=[0.0];calls=[0]
  class ClockEvent:
   def wait(self,seconds):clock[0]+=seconds
  def api(*args,**kwargs):
   calls[0]+=1
   return {'Stacks':[{'StackStatus':'CREATE_COMPLETE'if clock[0]>=110 else 'CREATE_IN_PROGRESS'}]}
  with mock.patch.object(campaign.time,'time',side_effect=lambda:clock[0]),mock.patch.object(campaign.threading,'Event',return_value=ClockEvent()),mock.patch.object(campaign,'aws',side_effect=api):
   self.assertEqual(campaign.wait_stack({},'same-stack','create',1000),'CREATE_COMPLETE')
   self.assertGreater(clock[0],100)
 def test_sync_tolerates_missing_instance_during_rollback(self):
  plan={'account':'a','region':'r','runId':'run'}
  with tempfile.TemporaryDirectory()as temp,mock.patch.object(campaign,'JOURNAL',Path(temp)/'journal'),mock.patch.object(campaign,'actor'),mock.patch.object(campaign,'stack_entry',return_value={'id':'own-stack'}),mock.patch.object(campaign,'protected',return_value=set()),mock.patch.object(campaign,'metadata',return_value={}),mock.patch.object(campaign,'record_created')as record:
   def api(plan,service,op,*args,**kwargs):
    if op=='list-stack-resources':return {'StackResourceSummaries':[{'PhysicalResourceId':'i-own','ResourceType':'AWS::EC2::Instance','LogicalResourceId':'LinuxInstance'}]}
    if op=='describe-instances':return None
    raise AssertionError(op)
   (Path(temp)/'journal').touch()
   with mock.patch.object(campaign,'aws',side_effect=api):self.assertEqual(len(campaign.sync(plan,best_effort=True)),1)
   record.assert_called_once()

class EnumerationIsolation(unittest.TestCase):
 def test_one_child_lookup_timeout_does_not_hide_later_cfn_members(self):
  plan={'account':'a','region':'r','runId':'run'}
  with tempfile.TemporaryDirectory()as temp,mock.patch.object(campaign,'JOURNAL',Path(temp)/'journal'),mock.patch.object(campaign,'actor'),mock.patch.object(campaign,'stack_entry',return_value={'id':'stack'}),mock.patch.object(campaign,'protected',return_value=set()),mock.patch.object(campaign,'metadata',return_value={}),mock.patch.object(campaign,'record_created')as record:
   def api(plan,service,op,*args,**kwargs):
    if op=='list-stack-resources':return {'StackResourceSummaries':[{'PhysicalResourceId':'i-own','ResourceType':'AWS::EC2::Instance','LogicalResourceId':'LinuxInstance'},{'PhysicalResourceId':'sg-own','ResourceType':'AWS::EC2::SecurityGroup','LogicalResourceId':'QaSecurityGroup'}]}
    raise subprocess.TimeoutExpired('describe-instances',100)
   with mock.patch.object(campaign,'aws',side_effect=api):campaign.sync(plan,best_effort=True)
   self.assertEqual([c.args[2]for c in record.call_args_list],['i-own','sg-own'])
   self.assertTrue(any(r['details'].get('inventoryGap')for r in campaign.rows(plan)))

class TransientObservation(unittest.TestCase):
 def test_connect_timeout_is_not_a_terminal_stack_failure(self):
  with tempfile.TemporaryDirectory()as temp,mock.patch.object(campaign,'JOURNAL',Path(temp)/'journal'),mock.patch.object(campaign.threading,'Event')as event:
   responses=[RuntimeError('Connect timeout on endpoint'),{'Stacks':[{'StackStatus':'CREATE_COMPLETE'}]}]
   with mock.patch.object(campaign,'aws',side_effect=responses):
    self.assertEqual(campaign.wait_stack({'account':'a','region':'r','runId':'run'},'same-stack','create',campaign.time.time()+60),'CREATE_COMPLETE')
   event.return_value.wait.assert_called_once()

class DescriptionEnrichment(unittest.TestCase):
 def test_authorize_first_record_can_later_gain_exact_cfn_logical_id(self):
  plan={'account':'a','region':'r','runId':'run'}
  with tempfile.TemporaryDirectory()as temp,mock.patch.object(campaign,'JOURNAL',Path(temp)/'journal'):
   (Path(temp)/'journal').touch()
   campaign.record_created(plan,'AWS::IAM::Role','expiry-role',{'cfnStackId':'stack','uniqueId':'immutable','ownershipVerified':True},'stack')
   campaign.record_created(plan,'AWS::IAM::Role','expiry-role',{'cfnStackId':'stack','logicalId':'ExpiryRole','uniqueId':'immutable','ownershipVerified':True},'stack')
   self.assertEqual(safety.effective_created(campaign.rows(plan))[('AWS::IAM::Role','expiry-role')]['details']['logicalId'],'ExpiryRole')

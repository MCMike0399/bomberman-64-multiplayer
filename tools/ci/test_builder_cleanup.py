import json,tempfile,unittest
from pathlib import Path
from unittest import mock
import builder_campaign as campaign
import builder_cleanup as cleanup_verify
import builder_journal as safety

class CompletionChecks(unittest.TestCase):
 def check(self,own_state,volume_present,stack_present=False,deleted_record=False,inventory_gap=False):
  with tempfile.TemporaryDirectory()as temp:
   root=Path(temp);(root/'protected-instances.json').write_text(json.dumps([{'id':'i-protected','state':'running'}]))
   rows=[{'event':'CREATED','type':'AWS::CloudFormation::Stack','id':'stack-own'}, {'event':'CREATED','type':'AWS::EC2::Instance','id':'i-own'},{'event':'CREATED','type':'implicit:EBS','id':'vol-own'}]
   if inventory_gap:rows.append({'event':'STATUS','id':'i-own','details':{'inventoryGap':True}})
   def api(plan,service,operation,*args,**kwargs):
    if service=='cloudformation':return {'Stacks':[{'StackStatus':'DELETE_COMPLETE'if deleted_record else 'DELETE_FAILED'}]}if stack_present else None
    if operation=='describe-volumes':return {'Volumes':[{'VolumeId':'vol-own'}]}if volume_present else None
    if operation=='describe-instances':return {'Reservations':[{'Instances':[{'InstanceId':args[1],'State':{'Name':own_state if args[1]=='i-own'else 'running'}}]}]}
    raise AssertionError('Unexpected mock API')
   plan={'account':'a','region':'r','runId':'run','payloadSource':'sha'}
   with mock.patch.object(cleanup_verify,'ROOT',root),mock.patch.object(cleanup_verify,'evidence',return_value={}),mock.patch.object(campaign,'actor'),mock.patch.object(cleanup_verify.builder_audit,'audit',return_value={'checked':{},'errors':[],'leftovers':[],'nonResourceRecords':[],'expectedStandingIdentity':{}}),mock.patch.object(campaign,'aws',side_effect=api),mock.patch.object(safety,'read_journal',return_value=rows),mock.patch.object(safety,'append')as appended:
    if own_state=='terminated'and not volume_present and (not stack_present or deleted_record) and not inventory_gap:
     result=cleanup_verify.final(plan);self.assertTrue(result['allCreatedResourcesGone']);appended.assert_called_once()
    else:
     with self.assertRaises(RuntimeError):cleanup_verify.final(plan)
     appended.assert_not_called()
 def test_terminated_and_no_children_is_complete(self):self.check('terminated',False)
 def test_unresolved_child_inventory_is_never_complete(self):self.check('terminated',False,inventory_gap=True)
 def test_stopped_is_never_complete(self):self.check('stopped',False)
 def test_surviving_volume_is_never_complete(self):self.check('terminated',True)
 def test_deleted_stack_record_is_complete_when_children_gone(self):self.check('terminated',False,True,True)
 def test_deleted_stack_record_does_not_hide_volume(self):self.check('terminated',True,True,True)
 def test_delete_failed_is_never_complete(self):self.check('terminated',False,True)
class TaggingIndexSweep(unittest.TestCase):
 def result(self,leftovers):
  return {'checked':{},'errors':[],'leftovers':[{'type':'TaggedResources','id':item}for item in leftovers],
          'taggedResources':[{'id':item,'taggingIndex':'present','authoritative':'shutting-down','resolved':False}for item in leftovers]}
 def run_sweep(self,results,deadline):
  clock=[1000.0];sleeps=[]
  def sleep(seconds):sleeps.append(seconds);clock[0]+=seconds
  with mock.patch.object(cleanup_verify.builder_audit,'audit',side_effect=results)as audit:
   value=cleanup_verify.sweep(lambda *args:None,deadline,now=lambda:clock[0],sleep=sleep)
  return value,audit.call_count,sleeps,clock[0]
 def test_retry_then_resolve(self):
  value,count,sleeps,_=self.run_sweep([self.result(['arn:i-1']),self.result(['arn:i-1']),self.result([])],2000+1200)
  self.assertEqual(count,3);self.assertEqual(value['leftovers'],[])
  self.assertEqual([row['passed']for row in value['sweepAttempts']],[False,False,True])
  self.assertEqual(value['sweepAttempts'][0]['leftovers'],['arn:i-1'])
 def test_retry_is_bounded_by_ten_minutes(self):
  value,count,sleeps,end=self.run_sweep([self.result(['arn:i-1'])]*100,1000+5000)
  self.assertLessEqual(end-1000,cleanup_verify.SWEEP_RETRY_SECONDS);self.assertEqual(count,21)
  self.assertEqual(value['leftovers'][0]['id'],'arn:i-1')
 def test_retry_respects_the_cleanup_deadline(self):
  value,count,sleeps,end=self.run_sweep([self.result(['arn:i-1'])]*100,1000+200)
  self.assertLessEqual(end,1000+200-cleanup_verify.SWEEP_DEADLINE_MARGIN_SECONDS);self.assertEqual(count,5)
 def test_no_deadline_or_expired_deadline_is_one_attempt(self):
  for deadline in (None,900):
   with self.subTest(deadline=deadline):
    _,count,sleeps,_=self.run_sweep([self.result(['arn:i-1'])]*3,deadline);self.assertEqual(count,1);self.assertEqual(sleeps,[])
 def test_final_receipt_keeps_both_views_and_fails_closed(self):
  arn='arn:aws:ec2:us-east-1:524329886851:instance/i-0aa4cc1ac8162d4e5'
  with tempfile.TemporaryDirectory()as temp:
   root=Path(temp);(root/'protected-instances.json').write_text(json.dumps([{'id':'i-protected','state':'running'}]))
   rows=[{'event':'CREATED','type':'AWS::EC2::Instance','id':'i-own'}]
   def api(plan,service,operation,*args,**kwargs):
    if operation=='describe-instances':return {'Reservations':[{'Instances':[{'InstanceId':args[1],'State':{'Name':'terminated'if args[1]=='i-own'else'running'}}]}]}
    raise AssertionError('Unexpected mock API')
   plan={'account':'a','region':'r','runId':'run','payloadSource':'sha'}
   for leftovers in ([arn],[]):
    with self.subTest(leftovers=leftovers),mock.patch.object(cleanup_verify,'ROOT',root),mock.patch.object(cleanup_verify,'evidence',return_value={}),mock.patch.object(campaign,'actor'),mock.patch.object(cleanup_verify.builder_audit,'audit',return_value={**self.result(leftovers),'nonResourceRecords':[],'expectedStandingIdentity':{}}),mock.patch.object(campaign,'aws',side_effect=api),mock.patch.object(safety,'read_journal',return_value=rows),mock.patch.object(safety,'append')as appended:
     if leftovers:
      with self.assertRaises(RuntimeError):cleanup_verify.final(plan)
      appended.assert_not_called()
     else:cleanup_verify.final(plan);appended.assert_called_once()
     receipt=json.loads((root/'cleanup-receipt.json').read_text())
     self.assertEqual(receipt['allCreatedResourcesGone'],not leftovers)
     self.assertEqual([(row['id'],row['taggingIndex'],row['authoritative'])for row in receipt['taggedResources']],[(item,'present','shutting-down')for item in leftovers])
     self.assertEqual(len(receipt['sweepAttempts']),1)
class ProtectedInventoryChecks(unittest.TestCase):
 def test_expired_terminated_record_is_reported_not_invented(self):
  changes,missing,count=cleanup_verify.protected_comparison([{'id':'old','state':'terminated'}],{})
  self.assertEqual(changes,[]);self.assertEqual(count,0);self.assertTrue(missing[0]['baselineAlreadyTerminated'])
 def test_missing_live_baseline_is_an_unexplained_change(self):
  changes,missing,count=cleanup_verify.protected_comparison([{'id':'active','state':'running'}],{})
  self.assertEqual(count,0);self.assertEqual(len(changes),1);self.assertFalse(missing[0]['baselineAlreadyTerminated'])
 def test_returned_baseline_still_compares_fields(self):
  changes,missing,count=cleanup_verify.protected_comparison([{'id':'active','state':'running','type':'small'}],{'active':{'State':{'Name':'running'},'InstanceType':'large'}})
  self.assertEqual(count,1);self.assertEqual(missing,[]);self.assertIn('type',changes[0]['differences'])

if __name__=='__main__':unittest.main()


class FinalInstanceStateNeverRaises(unittest.TestCase):
    """Reviewer 2026-10-31: an instance that ages out during this run's own sweep must
    resolve through the AGED_OUT rule or stay AMBIGUOUS, never crash cleanup."""
    class Journals:
        def __init__(self, evidence): self.evidence = evidence
        def instance(self, identity): return {'evidence': self.evidence, 'matchingJournals': 1 if self.evidence else 0, 'errors': []}

    def state(self, data, evidence=None):
        import builder_cleanup
        return builder_cleanup.instance_final_state(data, 'i-0aa4cc1ac8162d4e5', journals=self.Journals(evidence))

    def test_known_shapes(self):
        self.assertEqual(self.state(None), ('absent', None))
        one = {'Reservations': [{'Instances': [{'InstanceId': 'i-0aa4cc1ac8162d4e5', 'State': {'Name': 'terminated'}}]}]}
        self.assertEqual(self.state(one)[0], 'terminated')
        one['Reservations'][0]['Instances'][0]['State']['Name'] = 'shutting-down'
        self.assertEqual(self.state(one)[0], 'shutting-down')

    def test_empty_reservations_resolve_only_with_journal_proof(self):
        state, aged = self.state({'Reservations': []}, evidence={'workflowRunId': '41151908001', 'observedTermination': 'terminated'})
        self.assertEqual(state, 'AGED_OUT')
        self.assertEqual(aged['retainedJournal']['workflowRunId'], '41151908001')
        state, aged = self.state({'Reservations': []}, evidence=None)
        self.assertEqual(state, 'AMBIGUOUS:empty-reservations-without-journal-proof')
        self.assertFalse(aged['resolved'])

    def test_unexpected_shapes_are_ambiguous_not_exceptions(self):
        for data in ({}, {'Reservations': None}, {'Reservations': [{}]}, {'Reservations': [{'Instances': []}]},
                     {'Reservations': [{'Instances': [{'InstanceId': 'i-other', 'State': {'Name': 'terminated'}}]}]},
                     {'Reservations': [{'Instances': [{'State': {}}]}]}, 'not-a-dict'):
            with self.subTest(data=data):
                self.assertEqual(self.state(data)[0], 'AMBIGUOUS:unexpected-describe-shape')

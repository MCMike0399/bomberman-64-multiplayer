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

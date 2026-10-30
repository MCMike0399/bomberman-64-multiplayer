import tempfile
from pathlib import Path
import unittest
from unittest.mock import patch

import builder_campaign as campaign
import builder_journal as journal
import builder_namespace as names


class ChildInventory(unittest.TestCase):
    def check(self,previously_known):
        plan={'account':names.ACCOUNT,'region':names.REGION,'runId':'run'}
        with tempfile.TemporaryDirectory()as directory:
            path=Path(directory)/'journal.jsonl';path.touch()
            if previously_known:journal.append(path,plan,'STATUS','AWS::EC2::Instance','i-owned',{'inventoryGap':False})
            def aws(plan,service,operation,*args,**kwargs):
                if operation=='list-stack-resources':return {'StackResourceSummaries':[
                    {'ResourceType':'AWS::EC2::Instance','PhysicalResourceId':'i-owned','LogicalResourceId':'LinuxInstance'}]}
                if operation=='describe-instances':return {'Reservations':[{'Instances':[{
                    'InstanceId':'i-owned','Tags':[{'Key':names.RUN_TAG,'Value':'run'}],
                    'State':{'Name':'terminated'},'LaunchTime':'2026-10-30T00:00:00Z',
                    'BlockDeviceMappings':[],'NetworkInterfaces':[]}]}]}
                raise AssertionError(operation)
            with (patch.object(campaign,'JOURNAL',path),patch.object(campaign,'actor'),patch.object(campaign,'protected',return_value=set()),
                    patch.object(campaign,'stack_entry',return_value={'id':'stack'}),patch.object(campaign,'metadata',return_value={}),
                    patch.object(campaign,'record_created'),patch.object(campaign,'aws',side_effect=aws)):
                campaign.sync(plan)
            return journal.read_journal(path,plan)[-1]['details']['inventoryGap']

    def test_empty_terminated_instance_does_not_invent_an_exhaustive_inventory(self):self.assertTrue(self.check(False))
    def test_earlier_completed_inventory_is_not_lost_when_termination_clears_arrays(self):self.assertFalse(self.check(True))


if __name__=='__main__':unittest.main()

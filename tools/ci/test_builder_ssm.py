import base64
import json
from pathlib import Path
import tempfile
import unittest

import builder_journal as journal
import builder_namespace as names
import builder_ssm as ssm


class NativeDispatch(unittest.TestCase):
    def setUp(self):
        self.plan={'runId':'sancus-builder-20261030-8762d11-1234567890abcdef','payloadSource':'a'*40,
                   'fixedCampaignExpiryUtc':'2026-10-30T15:00:00Z','account':names.ACCOUNT,'region':names.REGION,
                   'instances':[{'os':'Linux','ami':'ami-owned'}]}
        self.instance='i-1234567890abcdef0';self.calls=[];self.comment=None;self.lose=False

    def aws(self,service,operation,*args):
        self.calls.append((service,operation,args))
        if operation=='get-caller-identity':return {'Account':names.ACCOUNT}
        if operation=='describe-instances':return {'Reservations':[{'Instances':[{
            'InstanceId':self.instance,'ImageId':'ami-owned','InstanceType':'g4dn.xlarge',
            'Tags':[{'Key':names.RUN_TAG,'Value':self.plan['runId']}],'State':{'Name':'running'}}]}]}
        if operation=='send-command':
            self.comment=args[args.index('--comment')+1]
            if self.lose:raise TimeoutError('Accepted response lost')
            return {'Command':{'CommandId':'command-1','Comment':self.comment,'InstanceIds':[self.instance]}}
        if operation=='list-commands':return {'Commands':[{'CommandId':'command-1','Comment':self.comment,
                    'InstanceIds':[self.instance],'DocumentName':'AWS-RunShellScript'}]if self.comment else[]}
        raise AssertionError(operation)

    def test_success_binds_data_to_exact_instance_and_has_no_github_token(self):
        with tempfile.TemporaryDirectory()as root:
            path=Path(root)/'journal.jsonl'
            self.assertEqual(ssm.dispatch(self.plan,path,self.aws,self.instance,'Linux','b'*64),'command-1')
            row=journal.read_journal(path,self.plan)[0]
            self.assertEqual(row['details']['descriptor']['instanceId'],self.instance)
            self.assertEqual(row['details']['descriptor']['executionContext'],'aws-ssm-interactive-desktop')
            self.assertNotIn('GITHUB_TOKEN',path.read_text())
            with self.assertRaisesRegex(ValueError,'reconcile'):ssm.dispatch(self.plan,path,self.aws,self.instance,'Linux','b'*64)

    def test_accepted_response_loss_reconciles_without_a_second_send(self):
        with tempfile.TemporaryDirectory()as root:
            path=Path(root)/'journal.jsonl';self.lose=True
            with self.assertRaises(TimeoutError):ssm.dispatch(self.plan,path,self.aws,self.instance,'Linux','b'*64)
            self.assertEqual(ssm.reconcile(self.plan,path,self.aws,self.instance),'command-1')
            self.assertEqual(ssm.reconcile(self.plan,path,self.aws,self.instance),'command-1')
            self.assertEqual(sum(operation=='send-command'for _,operation,_ in self.calls),1)

    def test_not_observed_remains_unresolved(self):
        with tempfile.TemporaryDirectory()as root:
            path=Path(root)/'journal.jsonl';self.lose=True
            with self.assertRaises(TimeoutError):ssm.dispatch(self.plan,path,self.aws,self.instance,'Linux','b'*64)
            self.comment=None
            self.assertIsNone(ssm.reconcile(self.plan,path,self.aws,self.instance))
            self.assertEqual(len(journal.read_journal(path,self.plan)),1)

    def test_protected_instance_cannot_receive_command(self):
        self.plan['runId']=names.run_id('20261030','8762d11','ffffffff')
        original=self.aws
        def wrong(*args):
            result=original(*args)
            if args[1]=='describe-instances':result['Reservations'][0]['Instances'][0]['Tags']=[]
            return result
        with tempfile.TemporaryDirectory()as root:
            with self.assertRaisesRegex(ValueError,'ownership'):ssm.dispatch(self.plan,Path(root)/'j',wrong,self.instance,'Linux','b'*64)
        self.assertFalse(any(operation=='send-command'for _,operation,_ in self.calls))

    def test_windows_only_fixed_dispatcher_receives_encoded_data(self):
        descriptor=ssm.task(self.plan,self.instance,'windows-x64','b'*64)
        command=ssm.commands('Windows',descriptor)[0]
        self.assertTrue(command.startswith('& C:\\sancus-builder\\bin\\dispatch.ps1 -Descriptor '))
        self.assertEqual(json.loads(base64.b64decode(command.split()[-1])),descriptor)


if __name__=='__main__':unittest.main()

import unittest
from unittest.mock import patch
import datetime
import json
from pathlib import Path
import tempfile

import builder_supervisor as supervisor


class IndependentCleanup(unittest.TestCase):
    def test_evidence_console_and_sync_errors_do_not_prevent_later_owned_cleanup(self):
        calls=[]
        def fail(name):
            def run():calls.append(name);raise TimeoutError(name)
            return run
        def success(name):return lambda:calls.append(name)
        result=supervisor.run_cleanup_stages([
            ('sync',fail('sync')),('console',fail('console')),('evidence',fail('evidence')),
            ('terminate',success('terminate')),('bucket',success('bucket')),
            ('delete',success('delete')),('verify',success('verify'))])
        self.assertEqual(calls,['sync','console','evidence','terminate','bucket','delete','verify'])
        self.assertEqual(result['completedStages'],['terminate','bucket','delete','verify'])
        self.assertEqual(len(result['stageErrors']),3)

    def test_delete_failure_does_not_skip_final_inventory(self):
        calls=[]
        def denied():raise ValueError('Ownership differs')
        result=supervisor.run_cleanup_stages([('delete',denied),('verify',lambda:calls.append('verify'))])
        self.assertEqual(calls,['verify']);self.assertEqual(result['stageErrors'][0]['error'],'Ownership differs')

    def test_interrupt_during_evidence_does_not_skip_cleanup(self):
        calls=[]
        def interrupted():raise KeyboardInterrupt('cancelled during evidence')
        result=supervisor.run_cleanup_stages([('evidence',interrupted),('cleanup',lambda:calls.append('cleanup'))])
        self.assertEqual(calls,['cleanup']);self.assertEqual(result['stageErrors'][0]['stage'],'evidence')

    def test_wait_stopped_never_means_terminated(self):
        plan={}
        with patch.object(supervisor.time,'time',side_effect=[0,0,2]),patch.object(supervisor.campaign,'aws',return_value={
                'Reservations':[{'Instances':[{'State':{'Name':'stopped'}}]}]}),patch.object(supervisor.threading,'Event'):
            with self.assertRaises(TimeoutError):supervisor.wait_terminated(plan,['i-owned'],1)

    def test_cancel_latency_is_recorded_for_the_actual_request_and_preserved(self):
        plan={'account':'524329886851','region':'us-east-1','runId':'sancus-builder-20261030-120b573-1234567890abcdef'}
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);path=root/'creation-manifest.jsonl'
            supervisor.journal.append(path,plan,'CREATED','AWS::EC2::Instance','i-owned',{},'owned-stack')
            (root/'cancellation.json').write_text(json.dumps({'signalObservedAtUtc':(datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(seconds=2)).isoformat()}))
            with (patch.object(supervisor.campaign,'ROOT',root),patch.object(supervisor.campaign,'JOURNAL',path),
                  patch.object(supervisor.campaign,'authorize_instance',side_effect=['running','shutting-down']),
                  patch.object(supervisor.campaign,'aws')as api):
                first=supervisor.request_termination(plan);second=supervisor.request_termination(plan)
            api.assert_called_once()
            self.assertGreaterEqual(first['terminationRequests'][0]['cancelToTerminationRequestSeconds'],2)
            self.assertEqual(first['terminationRequests'],second['terminationRequests'])

    def test_journaled_termination_precedes_slow_evidence_on_cleanup_path(self):
        plan={'runId':'sancus-builder-20261030-120b573-1234567890abcdef'};calls=[]
        rows=[{'event':'CREATED','type':'AWS::CloudFormation::Stack','id':'owned-stack','details':{}}]
        with tempfile.TemporaryDirectory()as root:
            with (patch.object(supervisor.campaign,'ROOT',Path(root)),patch.object(supervisor.campaign,'rows',return_value=rows),
                  patch.object(supervisor.campaign,'reconcile'),patch.object(supervisor.campaign,'sync'),
                  patch.object(supervisor,'request_termination',side_effect=lambda plan:calls.append('terminate')),
                  patch.object(supervisor,'wait_terminated'),patch.object(supervisor.builder_s3,'verify_absent',return_value=True),
                  patch.object(supervisor.campaign,'delete'),patch.object(supervisor.campaign,'wait_stack'),
                  patch.object(supervisor.cleanup,'final',return_value={'allCreatedResourcesGone':True,'evidenceComplete':False})):
                supervisor.cleanup_owned(plan,supervisor.time.time()+1,lambda:calls.append('evidence'))
        self.assertLess(calls.index('terminate'),calls.index('evidence'))


if __name__=='__main__':unittest.main()

import unittest
from unittest.mock import patch

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


if __name__=='__main__':unittest.main()

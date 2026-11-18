import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import builder_guest_exit as reporter


class EarlyExitEvidence(unittest.TestCase):
    def test_full_log_precedes_exit_marker_and_never_claims_hardware_pass(self):
        calls=[]
        class Client:
            def call(self,operation,*args):calls.append(args[1])
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);manifest=root/'manifest.json';log=root/'worker.log';log.write_text('early import failure')
            manifest.write_text(json.dumps({'runId':'owned-run','source':'a'*40,'account':'524329886851','region':'us-east-1'}))
            with (patch.object(reporter.native_execution,'instance_identity',return_value={'accountId':'524329886851','region':'us-east-1','instanceId':'i-0123456789abcdef0'}),
                  patch.object(reporter.builder_guest,'object_client',return_value=Client())):
                result=reporter.publish(manifest,'windows-x64',1,log,root/'out.json')
            # Both lane instances share user data (2026-11-17): keyed by the IMDS instance ID.
            self.assertEqual(calls,['results/instances/i-0123456789abcdef0/worker-log.bin','results/instances/i-0123456789abcdef0/worker-exit.json'])
            self.assertEqual(result['logSize'],len(log.read_bytes()));self.assertFalse(result['hardwareQualified'])


class ReadinessKey(unittest.TestCase):
    def test_both_lane_instances_publish_readiness_under_their_own_instance_id(self):
        # 2026-11-17: identical user data on both lanes; the lane is unknown before dispatch.
        import types
        import builder_guest_ready as ready
        calls=[]
        class Client:
            def call(self,operation,*args):calls.append(args[1])
        windll=types.SimpleNamespace(kernel32=types.SimpleNamespace(ProcessIdToSessionId=lambda pid,ref:setattr(ref._obj,'value',1)or 1),
                                     shell32=types.SimpleNamespace(IsUserAnAdmin=lambda:0))
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);manifest=root/'manifest.json'
            manifest.write_text(json.dumps({'schemaVersion':1,'runId':'sancus-builder-20261030-8762d11-1234567890abcdef','source':'a'*40,
                'account':'524329886851','region':'us-east-1','files':[
                {'role':'source','target':'all','objectKey':'payload/input-000.bin','fileName':'source.zip','sha256':'b'*64,'size':1},
                {'role':'artifact','target':'windows-x64','objectKey':'payload/input-001.bin','fileName':'package.zip','sha256':'c'*64,'size':1}]}))
            for instance in ('i-0000000000000c0e0','i-0000000000000aea0'):
                with (patch.object(ready.native_execution,'instance_identity',return_value={'accountId':'524329886851','region':'us-east-1','instanceId':instance}),
                      patch.object(ready.builder_guest,'object_client',return_value=Client()),patch.object(ready,'os',types.SimpleNamespace(name='nt',getpid=lambda:1)),
                      patch.object(ready.ctypes,'windll',windll,create=True),patch.object(ready.getpass,'getuser',return_value='BlastQa')):
                    receipt=ready.publish(manifest,'windows-x64',root/(instance+'.json'))
                self.assertEqual((receipt['target'],receipt['instanceId'],receipt['sessionId']),('windows-x64',instance,1))
        self.assertEqual(calls,['results/instances/i-0000000000000c0e0/bootstrap-ready.json','results/instances/i-0000000000000aea0/bootstrap-ready.json'])


if __name__=='__main__':unittest.main()

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
            with (patch.object(reporter.native_execution,'instance_identity',return_value={'accountId':'524329886851','region':'us-east-1','instanceId':'i-owned'}),
                  patch.object(reporter.builder_guest,'object_client',return_value=Client())):
                result=reporter.publish(manifest,'linux-x64',1,log,root/'out.json')
            self.assertEqual(calls,['results/linux-x64/worker-log.bin','results/linux-x64/worker-exit.json'])
            self.assertEqual(result['logSize'],len(log.read_bytes()));self.assertFalse(result['hardwareQualified'])


if __name__=='__main__':unittest.main()

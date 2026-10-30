import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SPEC=importlib.util.spec_from_file_location('verify_desktop',Path(__file__).with_name('verify-desktop.py'))
verifier=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(verifier)


class DiagnosticFailureLatch(unittest.TestCase):
    def test_rotated_success_cannot_erase_original_native_failure(self):
        with tempfile.TemporaryDirectory()as temporary:
            failures=['original capacity failure'];validated=[]
            with patch.object(verifier.subprocess,'run')as run:
                result=verifier.rotated_peer_probe(Path('owned-installed.exe'),Path(temporary),'http://127.0.0.1:1234',failures,lambda *args:validated.append(args))
            self.assertTrue(result['passed']);self.assertEqual(failures,['original capacity failure'])
            self.assertEqual(len(validated),1)
            self.assertEqual(run.call_args.kwargs['env']['BLAST_QA_PEER_JOIN_ORDER'],'reverse')

    def test_rotated_failure_and_incomplete_receipt_both_remain_failures(self):
        for child_fails in (True,False):
            with self.subTest(child_fails=child_fails),tempfile.TemporaryDirectory()as temporary:
                failures=['first failure']
                def validate(*args):raise RuntimeError('missing actual evidence')
                with patch.object(verifier.subprocess,'run',side_effect=subprocess.CalledProcessError(1,['probe'])if child_fails else None):
                    result=verifier.rotated_peer_probe(Path('owned.exe'),Path(temporary),'http://127.0.0.1:1234',failures,validate)
                self.assertFalse(result['passed']);self.assertEqual(len(failures),2);self.assertEqual(failures[0],'first failure')

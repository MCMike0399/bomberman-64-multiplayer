import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SPEC=importlib.util.spec_from_file_location('verify_desktop',Path(__file__).with_name('verify-desktop.py'))
verifier=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(verifier)


class DiagnosticRepeat(unittest.TestCase):
    """The repeat loop counts crashes separately and can only fail, never qualify."""

    def loop(self,outcomes):
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
        output=Path(temporary.name)/'repeat';receipt=Path(temporary.name)/'repeat.json';calls=iter(outcomes)
        def fake_run(command,**kwargs):
            outcome=next(calls);folder=output/('run-'+str(len(list(output.iterdir()))));folder.mkdir()
            passed=outcome=='pass'
            runner={'passed':passed,'exitCode':{'pass':0,'crash':127,'fail':0}[outcome],'errors':[]if passed else['x'],
                    'assertions':23,'seconds':9.0,'linuxCrashCapture':{'inferredSignal':{'signal':'SIGSEGV'}}if outcome=='crash'else{}}
            (folder/'runner-summary.json').write_text(json.dumps(runner))
            if outcome!='crash':(folder/'summary.json').write_text('{}')
            return subprocess.CompletedProcess(command,0 if passed else 1)
        with patch.object(verifier.subprocess,'run',side_effect=fake_run):
            try:verifier.repeat(output,'controller-popups',len(outcomes),lambda scenario,destination:(['run.py'],{},60),receipt,'a'*40);error=None
            except RuntimeError as caught:error=caught
        return json.loads(receipt.read_text()),error

    def test_all_passing_runs_pass(self):
        receipt,error=self.loop(['pass']*3)
        self.assertIsNone(error);self.assertTrue(receipt['allPassed'])
        self.assertEqual((receipt['completed'],receipt['passed'],receipt['crashed'],receipt['failed']),(3,3,0,0))

    def test_one_crash_fails_the_loop_and_is_counted_as_a_crash(self):
        receipt,error=self.loop(['pass','crash','pass'])
        self.assertIsNotNone(error);self.assertFalse(receipt['allPassed'])
        self.assertEqual((receipt['passed'],receipt['crashed'],receipt['failed']),(2,1,0))
        self.assertEqual(receipt['runs'][1]['inferredSignal'],{'signal':'SIGSEGV'})
        self.assertEqual(receipt['runs'][1]['exitCode'],127)

    def test_ordinary_failure_also_fails_but_is_not_a_crash(self):
        receipt,error=self.loop(['fail'])
        self.assertIsNotNone(error);self.assertEqual((receipt['crashed'],receipt['failed']),(0,1))


if __name__=='__main__':
    unittest.main()

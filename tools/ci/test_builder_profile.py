import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest


class CredentialWrapperCancellation(unittest.TestCase):
    def test_cancel_is_forwarded_and_owned_temporary_credentials_are_removed(self):
        with tempfile.TemporaryDirectory()as directory:
            root=Path(directory);pid=root/'child.pid'
            script=Path(__file__).with_name('builder_profile.py')
            command=[sys.executable,str(script),'--',sys.executable,'-c',
                     'import os,time;from pathlib import Path;Path('+repr(str(pid))+').write_text(str(os.getpid()));time.sleep(10)']
            env={**os.environ,'AWS_ACCESS_KEY_ID':'fixture-access','AWS_SECRET_ACCESS_KEY':'fixture-secret',
                 'AWS_SESSION_TOKEN':'fixture-session','RUNNER_TEMP':str(root)}
            process=subprocess.Popen(command,env=env)
            try:
                deadline=time.monotonic()+2
                while not pid.exists()and time.monotonic()<deadline:time.sleep(.01)
                self.assertTrue(pid.exists(),'Owned child did not reach the cancellation seam')
                child=int(pid.read_text());process.send_signal(signal.SIGTERM);process.wait(timeout=3)
                with self.assertRaises(ProcessLookupError):os.kill(child,0)
                self.assertEqual(list(root.glob('builder-credentials-*')),[])
            finally:
                if process.poll()is None:process.kill();process.wait(timeout=2)

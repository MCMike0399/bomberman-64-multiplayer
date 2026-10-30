import os
from pathlib import Path
import signal
import sys
import tempfile
import threading
import unittest

import builder_phase as phase


class BoundedPhase(unittest.TestCase):
    def test_phase_timeout_reaps_its_owned_child_for_the_always_cleanup_step(self):
        with tempfile.TemporaryDirectory()as root:
            result=phase.run([sys.executable,'-c','import time;time.sleep(10)'],.05,Path(root)/'phase.log')
        self.assertEqual(result['exitCode'],124);self.assertTrue(result['timedOut']);self.assertTrue(result['childReaped'])

    def test_actual_cancel_signal_reaps_child_and_returns_control(self):
        timers=[]
        def started(process):
            timer=threading.Timer(.05,lambda:os.kill(os.getpid(),signal.SIGTERM));timers.append(timer);timer.start()
        with tempfile.TemporaryDirectory()as root:
            try:result=phase.run([sys.executable,'-c','import time;time.sleep(10)'],2,Path(root)/'phase.log',started)
            finally:
                for timer in timers:timer.cancel();timer.join()
        self.assertEqual(result['exitCode'],130);self.assertTrue(result['cancelled']);self.assertTrue(result['childReaped'])

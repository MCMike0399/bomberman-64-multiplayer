"""A timed-out AppImage launcher must not leave its game using the owned display."""
import importlib.util
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest

SPEC = importlib.util.spec_from_file_location('startup_desktop', Path(__file__).with_name('startup-desktop.py'))
startup = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(startup)


class StartupProcessTests(unittest.TestCase):
    @unittest.skipUnless(os.name == 'posix', 'POSIX AppImage descendant lifetime')
    def test_timeout_reaps_the_game_descendant_before_display_teardown(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            pid_file, marker = folder / 'child.pid', folder / 'survived'
            child = 'import time;from pathlib import Path;time.sleep(1);Path(' + repr(str(marker)) + ').touch()'
            parent = ('import subprocess,sys,time;from pathlib import Path;'
                      'p=subprocess.Popen([sys.executable,"-c",' + repr(child) + ']);'
                      'Path(' + repr(str(pid_file)) + ').write_text(str(p.pid));time.sleep(30)')
            try:
                with (folder / 'runtime.log').open('w') as log, self.assertRaises(subprocess.TimeoutExpired):
                    startup.run_startup([sys.executable, '-c', parent], folder, os.environ.copy(), log, timeout=.5)
                self.assertTrue(pid_file.exists(), 'The witness must actually create a game descendant')
                time.sleep(1)
                self.assertFalse(marker.exists(), 'Game descendant survived the launcher timeout')
            finally:
                if pid_file.exists():
                    try: os.kill(int(pid_file.read_text()), signal.SIGKILL)
                    except ProcessLookupError: pass

    def test_success_preserves_exit_and_log(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            with (folder / 'runtime.log').open('w') as log:
                result = startup.run_startup([sys.executable, '-c', 'print("captured");raise SystemExit(7)'],
                                             folder, os.environ.copy(), log, timeout=5)
            self.assertEqual(7, result.returncode)
            self.assertEqual('captured', (folder / 'runtime.log').read_text().strip())


if __name__ == '__main__': unittest.main()

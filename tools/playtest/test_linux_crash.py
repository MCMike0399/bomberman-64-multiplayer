import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import linux_crash


class LinuxCrashCapture(unittest.TestCase):
    STATE = {'startEpoch': 1000.0, 'memoryKiBBefore': {'MemAvailable': 9}, 'corePattern': 'core', 'coreLimitHard': -1}

    def test_kernel_lines_name_the_hidden_signal(self):
        text = ('2026-11-01T08:51:52+0000 ip kernel: Out of memory: Killed process 4242 (BlastCircuit.x8) total-vm:1kB\n'
                '2026-11-01T08:51:52+0000 ip kernel: BlastCircuit.x8[4243]: segfault at 0 ip 00007f sp 00007f error 4\n'
                '2026-11-01T08:51:52+0000 ip kernel: unrelated line\n')
        found = linux_crash.kernel_signals(text)
        self.assertEqual([(row['signal'], row['pid']) for row in found], [('SIGKILL', 4242), ('SIGSEGV', 4243)])

    def test_coredumpctl_rows_are_parsed(self):
        text = ('TIME                         PID  UID  GID SIG     COREFILE EXE                       SIZE\n'
                'Sun 2026-11-01 08:51:52 UTC 4243 1000 1000 SIGSEGV present  /tmp/x/BlastCircuit.x86_64 12M\n')
        self.assertEqual(linux_crash.coredumps(text), [{'pid': 4243, 'signal': 'SIGSEGV', 'exe': '/tmp/x/BlastCircuit.x86_64'}])

    def test_normal_exit_records_memory_only(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(linux_crash, 'run') as run:
            record = linux_crash.after(directory, self.STATE, 0, True)
            self.assertFalse(record['abnormal']);run.assert_not_called()
            self.assertEqual(list(Path(directory).iterdir()), [])
            self.assertIn('memoryKiBAfter', record)

    def test_abnormal_exit_saves_kernel_and_coredump_text_and_prefers_the_core_signal(self):
        outputs = {
            'journalctl': {'command': ['journalctl'], 'output': 'kernel: BlastCircuit.x8[7]: segfault at 0 ip 1\n'},
            'coredumpctl': {'command': ['coredumpctl'], 'output': 'Sun 2026-11-01 08:51:52 UTC 7 1000 1000 SIGABRT present /tmp/BlastCircuit.x86_64 1M\n'},
            'tail': {'command': ['tail'], 'error': 'no apport log'}}
        def fake(command, timeout=60):
            if command[:2] == ['coredumpctl', 'info']:
                return {'command': command, 'output': 'Stack trace of thread 7:\n#0 abort\n'}
            return outputs[command[0]]
        with tempfile.TemporaryDirectory() as directory, patch.object(linux_crash, 'run', side_effect=fake):
            record = linux_crash.after(directory, self.STATE, 127, False)
            folder = Path(directory)
            self.assertIn('segfault', (folder / 'linux-kernel-log.txt').read_text())
            self.assertIn('Stack trace of thread 7', (folder / 'linux-coredumps.txt').read_text())
            saved = json.loads((folder / 'linux-crash-capture.json').read_text())
        self.assertEqual(record['inferredSignal'], {'signal': 'SIGABRT', 'source': 'coredumpctl', 'pid': 7})
        self.assertEqual(saved['kernelSignals'][0]['signal'], 'SIGSEGV')

    def test_unavailable_tools_leave_an_explicit_unknown(self):
        with tempfile.TemporaryDirectory() as directory, \
                patch.object(linux_crash, 'run', return_value={'command': ['x'], 'error': 'not installed'}):
            record = linux_crash.after(directory, self.STATE, None, False)
        self.assertTrue(record['abnormal']);self.assertIsNone(record['inferredSignal'])


if __name__ == '__main__':
    unittest.main()

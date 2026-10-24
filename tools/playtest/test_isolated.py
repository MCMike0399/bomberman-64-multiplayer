"""Isolation and evidence failures must never fall back to the user's desktop."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import Mock, patch

import isolated
import isolated_guest


class IsolatedTests(unittest.TestCase):
    def test_guest_launcher_refuses_accidental_execution_on_the_physical_host(self):
        with patch.object(isolated_guest.subprocess, 'check_output', return_value='Mac17,3\n'):
            with self.assertRaisesRegex(RuntimeError, 'physical Mac'):
                isolated_guest.main()
        with patch.object(isolated_guest.subprocess, 'check_output', return_value='VirtualMac2,1\n'):
            self.assertEqual('VirtualMac2,1', isolated_guest.ensure_guest())

    def test_vm_keeps_graphics_but_disconnects_host_input_clipboard_audio_and_lan(self):
        command = isolated.vm_command('tart', 'qa', Path('/payload'), Path('/evidence'))
        for flag in ('--no-graphics', '--no-clipboard', '--no-audio', '--no-usb-accessories', '--net-host'):
            self.assertIn(flag, command)
        self.assertIn('--dir=payload:/payload:ro', command)
        self.assertNotIn('--headless', command)
        self.assertNotIn('--capture-system-keys', command)
        self.assertNotIn('--vnc', command)

    def test_relocated_receipt_preserves_original_and_capture_integrity_checks(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            original = json.dumps({'screenshots': [str(isolated.GUEST_OUTPUT / 'scene.png')], 'assertions': 8})
            (folder / 'summary.json').write_text(original)
            isolated.relocate_summary(folder)
            self.assertEqual(original, (folder / 'guest-summary.json').read_text())
            result = json.loads((folder / 'summary.json').read_text())
            self.assertEqual([str(folder / 'scene.png')], result['screenshots'])
            self.assertEqual(8, result['assertions'])

    def test_capture_paths_cannot_escape_the_shared_evidence(self):
        with tempfile.TemporaryDirectory() as temporary, tempfile.TemporaryDirectory() as outside:
            folder = Path(temporary)
            (folder / 'link').symlink_to(outside)
            for capture in ('/tmp/elsewhere.png', str(isolated.GUEST_OUTPUT / '../secret'),
                            str(isolated.GUEST_OUTPUT / 'link/outside.png')):
                raw = json.dumps({'screenshots': [capture]})
                (folder / 'summary.json').write_text(raw)
                with self.subTest(capture=capture), self.assertRaises(ValueError):
                    isolated.relocate_summary(folder)
                self.assertEqual(raw, (folder / 'summary.json').read_text())

    def test_busy_or_missing_vm_does_not_build_launch_or_stop_anything(self):
        args = argparse.Namespace(server='http://127.0.0.1:18496', vm='fixture-qa')
        for machines in ([], [{'Source': 'local', 'Name': args.vm, 'State': 'running'}]):
            with tempfile.TemporaryDirectory() as temporary:
                with patch.object(isolated.platform, 'system', return_value='Darwin'), \
                     patch.object(isolated.platform, 'machine', return_value='arm64'), \
                     patch.object(isolated, 'tart_path', return_value=sys.executable), \
                     patch.object(isolated.subprocess, 'check_output', return_value=json.dumps(machines)), \
                     patch.object(isolated.subprocess, 'Popen') as launch, \
                     patch.object(isolated, 'prepare') as prepare:
                    with self.assertRaisesRegex(RuntimeError, 'stopped VM'):
                        isolated.run(args, Path(temporary), {}, False, True)
                    prepare.assert_not_called()
                    launch.assert_not_called()

    def test_unavailable_vm_emits_failure_without_launching_host_game(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            sentinel = folder / 'host-game-launched'
            game = folder / 'fake-godot'
            game.write_text('#!' + sys.executable + '\nfrom pathlib import Path\nPath(' + repr(str(sentinel)) + ').touch()\n')
            game.chmod(0o755)
            process = subprocess.run([sys.executable, str(isolated.ROOT / 'tools/playtest/run.py'),
                                      '--scenario', 'menu', '--desktop', 'isolated', '--godot', str(game),
                                      '--output', str(folder / 'out')],
                                     env=dict(os.environ, TART=str(folder / 'missing-tart')),
                                     capture_output=True, text=True, timeout=15)
            self.assertNotEqual(0, process.returncode)
            self.assertFalse(sentinel.exists())
            receipt = json.loads(next((folder / 'out').glob('*/runner-summary.json')).read_text())
            self.assertFalse(receipt['passed'])
            self.assertTrue(receipt['errors'])

    def test_failed_vm_boot_reaps_only_the_owned_process_and_finishes_observation(self):
        args = argparse.Namespace(server='http://127.0.0.1:18496', vm='fixture-qa')
        machines = [{'Source': 'local', 'Name': args.vm, 'State': 'stopped'}]
        config = {'OS': 'darwin', 'CPU': 4, 'Memory': 8192, 'Display': '1600x1000', 'MACAddress': 'not-for-report'}
        process = Mock(pid=123)
        process.poll.return_value = 1
        observer = Mock()
        with tempfile.TemporaryDirectory() as temporary:
            result = {}
            with patch.object(isolated.platform, 'system', return_value='Darwin'), \
                 patch.object(isolated.platform, 'machine', return_value='arm64'), \
                 patch.object(isolated, 'tart_path', return_value=sys.executable), \
                 patch.object(isolated.subprocess, 'check_output', side_effect=[json.dumps(machines), json.dumps(config)]), \
                 patch.object(isolated.subprocess, 'Popen', return_value=process), \
                 patch.object(isolated, 'prepare'), \
                 patch('host_observer.HostObserver', return_value=observer), \
                 patch('run.stop') as stop:
                with self.assertRaisesRegex(RuntimeError, 'exited during startup'):
                    isolated.run(args, Path(temporary), result, False, True)
                stop.assert_called_once_with(process)
                observer.finish.assert_called_once()
            self.assertNotIn('MACAddress', result['desktop']['configuration'])

    def test_payload_inventory_rejects_symlink_to_host_files(self):
        with tempfile.TemporaryDirectory() as temporary, tempfile.TemporaryDirectory() as outside:
            root = Path(temporary)
            secret = Path(outside) / 'private.txt'
            secret.write_text('fixture')
            (root / 'escape').symlink_to(secret)
            with self.assertRaises(ValueError):
                isolated.inventory(root)

    @unittest.skipUnless(os.name == 'posix', 'POSIX cancellation')
    def test_agent_termination_writes_failure_and_reaps_the_owned_runtime(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            ready = root / 'ready'
            game = root / 'fake-godot'
            game.write_text('#!' + sys.executable + '\nimport os,time\nfrom pathlib import Path\n'
                            'Path(' + repr(str(ready)) + ').write_text(str(os.getpid()))\ntime.sleep(30)\n')
            game.chmod(0o755)
            process = subprocess.Popen([sys.executable, str(isolated.ROOT / 'tools/playtest/run.py'),
                                        '--scenario', 'movement', '--headless', '--godot', str(game),
                                        '--output', str(root / 'out')], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            try:
                deadline = time.monotonic() + 5
                while not ready.exists() and time.monotonic() < deadline:
                    time.sleep(.01)
                self.assertTrue(ready.exists())
                child = int(ready.read_text())
                process.terminate()
                process.communicate(timeout=10)
                self.assertNotEqual(0, process.returncode)
                result = json.loads(next((root / 'out').glob('*/runner-summary.json')).read_text())
                self.assertFalse(result['passed'])
                self.assertIn('cancelled by signal', ' '.join(result['errors']))
                with self.assertRaises(ProcessLookupError):
                    os.kill(child, 0)
            finally:
                if process.poll() is None:
                    process.kill()
                    process.communicate()


if __name__ == '__main__':
    unittest.main()

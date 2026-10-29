"""Shutdown controls retain failure semantics and exclude credentials from dumps."""
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import startup_lifecycle as diagnostic


VALID_SHUTDOWN = {"schemaVersion": 1, "savedStateFlushed": True, "watchdogEnabled": False, "watchdogFired": False, "drain": {"completed": True}, "error": None}


class StartupLifecycleTests(unittest.TestCase):
    def test_forced_exit_or_missing_flush_drain_proof_never_counts_as_clean_exit(self):
        record = {'timedOut': False, 'exitCode': 0, 'earlyQuit': True, 'preset': 'low', 'headless': False}
        invalid = [None, {}, dict(VALID_SHUTDOWN, savedStateFlushed=False),
                   dict(VALID_SHUTDOWN, watchdogFired=True), dict(VALID_SHUTDOWN, watchdogEnabled=True),
                   dict(VALID_SHUTDOWN, drain=None), dict(VALID_SHUTDOWN, drain={'completed': False}),
                   dict(VALID_SHUTDOWN, error='failed')]
        for shutdown in invalid:
            with self.subTest(shutdown=shutdown):
                self.assertFalse(diagnostic.inspect_case(record, '', Path('/missing'), shutdown))
        self.assertFalse(diagnostic.inspect_case(record, 'BLAST_SHUTDOWN_WATCHDOG_FIRED\n', Path('/missing'), VALID_SHUTDOWN))
        self.assertFalse(diagnostic.inspect_case(record, 'BLAST_SHUTDOWN_SAVE_FAILED\n', Path('/missing'), VALID_SHUTDOWN))

    def test_success_marker_without_exit_still_fails(self):
        record = {'timedOut': True, 'exitCode': None, 'earlyQuit': False, 'preset': 'low', 'headless': False}
        text = 'GODOT_SMOKE_OK menu snapshots=0 graphics=low window=fullscreen size=1920x1080 adapter=T4\n'
        self.assertFalse(diagnostic.inspect_case(record, text, Path('/missing'), VALID_SHUTDOWN))
        self.assertEqual(1, len(record['smokeLines']))

    def test_native_smoke_requires_capture_and_matching_preset(self):
        with tempfile.TemporaryDirectory() as temporary:
            capture = Path(temporary) / 'menu.png'; capture.write_bytes(b'\x89PNG\r\n\x1a\n' + b'x' * 2048)
            record = {'timedOut': False, 'exitCode': 0, 'earlyQuit': False, 'preset': 'high', 'headless': False}
            text = 'GODOT_SMOKE_OK menu snapshots=0 graphics=high window=fullscreen size=1920x1080 adapter=T4\n'
            self.assertTrue(diagnostic.inspect_case(record, text, capture, VALID_SHUTDOWN))
            self.assertFalse(diagnostic.inspect_case(record, text.replace('graphics=high', 'graphics=low'), capture, VALID_SHUTDOWN))
            capture.unlink(); self.assertFalse(diagnostic.inspect_case(record, text, capture, VALID_SHUTDOWN))

    def test_headless_and_early_quit_controls_cannot_hide_native_errors(self):
        record = {'timedOut': False, 'exitCode': 0, 'earlyQuit': True, 'preset': 'low', 'headless': False}
        self.assertTrue(diagnostic.inspect_case(record, '', Path('/missing'), VALID_SHUTDOWN))
        self.assertFalse(diagnostic.inspect_case(record, 'ERROR: native teardown failed\n', Path('/missing'), VALID_SHUTDOWN))
        record.update(earlyQuit=False, headless=True)
        self.assertFalse(diagnostic.inspect_case(record, '', Path('/missing'), VALID_SHUTDOWN))
        text = 'GODOT_SMOKE_OK menu snapshots=0 graphics=low window=windowed size=64x64 adapter=Dummy\n'
        self.assertTrue(diagnostic.inspect_case(record, text, Path('/missing'), VALID_SHUTDOWN))

    def test_dump_environment_excludes_provider_ci_and_signing_secrets(self):
        environment = {'PATH': '/bin', 'VK_DRIVER_FILES': '/icd.json', 'GITHUB_TOKEN': 'secret', 'AWS_PROFILE': 'private',
                       'RELEASE_SIGNING_KEY': 'secret', 'ACTIONS_RUNTIME_TOKEN': 'secret', 'MY_PASSWORD': 'secret'}
        with patch.dict(os.environ, environment, clear=True):
            self.assertEqual({'PATH': '/bin', 'VK_DRIVER_FILES': '/icd.json'}, diagnostic.game_environment())

    def test_actual_native_headless_footer_has_no_graphics_adapter(self):
        record = {'timedOut': False, 'exitCode': 0, 'earlyQuit': False, 'preset': 'low', 'headless': True}
        text = 'GODOT_SMOKE_OK menu snapshots=0 graphics=low window=minimized size=64x64 adapter=\n'
        self.assertTrue(diagnostic.inspect_case(record, text, Path('/missing'), VALID_SHUTDOWN))
        record.update(headless=False)
        with tempfile.TemporaryDirectory() as temporary:
            capture = Path(temporary) / 'menu.png'; capture.write_bytes(b'\x89PNG\r\n\x1a\n' + b'x' * 2048)
            self.assertFalse(diagnostic.inspect_case(record, text.replace('minimized size=64x64', 'fullscreen size=1920x1080'), capture, VALID_SHUTDOWN))


if __name__ == '__main__': unittest.main()

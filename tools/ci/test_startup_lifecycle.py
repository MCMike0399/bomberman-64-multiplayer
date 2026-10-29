"""Shutdown controls retain failure semantics and exclude credentials from dumps."""
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import startup_lifecycle as diagnostic


class StartupLifecycleTests(unittest.TestCase):
    def test_success_marker_without_exit_still_fails(self):
        record = {'timedOut': True, 'exitCode': None, 'earlyQuit': False, 'preset': 'low', 'headless': False}
        text = 'GODOT_SMOKE_OK menu snapshots=0 graphics=low window=fullscreen size=1920x1080 adapter=T4\n'
        self.assertFalse(diagnostic.inspect_case(record, text, Path('/missing')))
        self.assertEqual(1, len(record['smokeLines']))

    def test_native_smoke_requires_capture_and_matching_preset(self):
        with tempfile.TemporaryDirectory() as temporary:
            capture = Path(temporary) / 'menu.png'; capture.write_bytes(b'\x89PNG\r\n\x1a\n' + b'x' * 2048)
            record = {'timedOut': False, 'exitCode': 0, 'earlyQuit': False, 'preset': 'high', 'headless': False}
            text = 'GODOT_SMOKE_OK menu snapshots=0 graphics=high window=fullscreen size=1920x1080 adapter=T4\n'
            self.assertTrue(diagnostic.inspect_case(record, text, capture))
            self.assertFalse(diagnostic.inspect_case(record, text.replace('graphics=high', 'graphics=low'), capture))
            capture.unlink(); self.assertFalse(diagnostic.inspect_case(record, text, capture))

    def test_headless_and_early_quit_controls_cannot_hide_native_errors(self):
        record = {'timedOut': False, 'exitCode': 0, 'earlyQuit': True, 'preset': 'low', 'headless': False}
        self.assertTrue(diagnostic.inspect_case(record, '', Path('/missing')))
        self.assertFalse(diagnostic.inspect_case(record, 'ERROR: native teardown failed\n', Path('/missing')))
        record.update(earlyQuit=False, headless=True)
        self.assertFalse(diagnostic.inspect_case(record, '', Path('/missing')))
        text = 'GODOT_SMOKE_OK menu snapshots=0 graphics=low window=windowed size=64x64 adapter=Dummy\n'
        self.assertTrue(diagnostic.inspect_case(record, text, Path('/missing')))

    def test_dump_environment_excludes_provider_ci_and_signing_secrets(self):
        environment = {'PATH': '/bin', 'VK_DRIVER_FILES': '/icd.json', 'GITHUB_TOKEN': 'secret', 'AWS_PROFILE': 'private',
                       'RELEASE_SIGNING_KEY': 'secret', 'ACTIONS_RUNTIME_TOKEN': 'secret', 'MY_PASSWORD': 'secret'}
        with patch.dict(os.environ, environment, clear=True):
            self.assertEqual({'PATH': '/bin', 'VK_DRIVER_FILES': '/icd.json'}, diagnostic.game_environment())


if __name__ == '__main__': unittest.main()

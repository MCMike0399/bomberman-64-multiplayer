import argparse
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch

import gpu_health


class GameHealthContractTests(unittest.TestCase):
    """Exercise the guest command/stage contract without allocating a GPU."""

    def exercise(self, driver, *, exit_code=0, write_health=True, default_driver=False):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            output = root / 'output'
            executable = output / 'installed/BlastCircuit.exe'
            protected = output / '.blast-update-existing'
            calls = []

            def install(*_):
                executable.parent.mkdir(parents=True)
                executable.write_bytes(b'unchanged frozen game')
                (executable.parent / 'unins000.exe').write_bytes(b'uninstaller')
                protected.mkdir()
                (protected / 'keep').write_text('existing owner')
                return executable

            def launch(command, **kwargs):
                calls.append(command)
                if command[0].endswith('unins000.exe'):
                    return types.SimpleNamespace(returncode=0)
                if default_driver:
                    self.assertNotIn('--rendering-driver',command[:command.index('--')])
                else:
                    self.assertEqual(command[1:3],['--rendering-driver',driver])
                # This is the shipped updater's flag, not the former helper typo.
                parser = argparse.ArgumentParser()
                parser.add_argument('--update-token', required=True)
                arguments, _ = parser.parse_known_args(command[command.index('--') + 1:])
                stage = output / ('.blast-update-' + arguments.update_token)
                self.assertTrue(stage.is_dir())
                if write_health:
                    (stage / 'health.json').write_text(json.dumps({
                        'driver': driver, 'adapter': 'NVIDIA Tesla T4',
                        'method': 'forward_plus', 'graphics': 'high'}))
                (output / 'shutdown.json').write_text(json.dumps({
                    'sourceRevision': 'frozen-source', 'savedStateFlushed': True,
                    'drain': {'completed': True}}))
                kwargs['stdout'].write('GODOT_SMOKE_OK\n')
                return types.SimpleNamespace(returncode=exit_code)

            with patch.object(gpu_health.crossplay_runtime, 'metadata', return_value={'revision': 'frozen-source'}), \
                    patch.object(gpu_health.crossplay_runtime, 'install', side_effect=install), \
                    patch.object(gpu_health.subprocess, 'run', side_effect=launch):
                if write_health:
                    code = gpu_health.run(root, output, 'frozen-source', driver, default_driver)
                    report = json.loads((output / 'gpu-health.json').read_text())
                    self.assertEqual(code, 0 if exit_code == 0 else 1)
                    self.assertEqual(report['passed'], exit_code == 0)
                    self.assertEqual(report['driverOverride'],None if default_driver else driver)
                else:
                    with self.assertRaises(FileNotFoundError):
                        gpu_health.run(root, output, 'frozen-source', driver)
            self.assertEqual(list(output.glob('.blast-update-*')), [protected])
            self.assertEqual((protected / 'keep').read_text(), 'existing owner')
            self.assertEqual(executable.read_bytes(), b'unchanged frozen game')
            self.assertTrue(calls[-1][0].endswith('unins000.exe'))

    def test_real_health_flag_and_stage_cleanup_under_both_apis(self):
        for driver in ('d3d12', 'vulkan'):
            with self.subTest(driver=driver):
                self.exercise(driver)

    def test_failed_game_exit_is_not_a_pass_and_still_cleans_owned_stage(self):
        self.exercise('d3d12', exit_code=1)

    def test_vulkan_default_is_observed_without_an_api_override(self):
        self.exercise('vulkan',default_driver=True)

    def test_missing_health_preserves_failure_and_still_cleans_owned_stage(self):
        self.exercise('vulkan', write_health=False)


if __name__ == '__main__':
    unittest.main()

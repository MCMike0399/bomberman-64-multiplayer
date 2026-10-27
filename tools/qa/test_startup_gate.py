"""Startup gate verdicts; launches are simulated by their log, exit code and capture."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('startup_gate', Path(__file__).with_name('startup_gate.py'))
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)
REPORT = ' graphics=high window=fullscreen size=1920x1200 adapter=Apple Paravirtual device'


class StartupGateTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.capture = Path(temporary.name) / 'menu.png'
        self.capture.write_bytes(gate.PNG + b'\0' * 2048)

    def verdict(self, log, case=('high', 'fullscreen', 'menu'), exit_code=0, legacy=False):
        return gate.evaluate(case, log, exit_code, self.capture, legacy)

    def test_matrix_covers_every_saved_preset_and_display_mode(self):
        self.assertEqual(9, len(set(gate.CASES)))
        for preset in gate.PRESETS:
            self.assertIn((preset, 'fullscreen', 'menu'), gate.CASES)
            self.assertIn((preset, 'windowed', 'menu'), gate.CASES)
            self.assertIn((preset, 'fullscreen', 'gameplay'), gate.CASES)

    def test_saved_preferences_use_the_products_keys(self):
        text = gate.preferences('high', 'fullscreen')
        self.assertIn('graphicsPreset="high"', text)
        self.assertIn('fullscreen=true', text)
        self.assertIn('automaticUpdates=false', text)
        self.assertIn('fullscreen=false', gate.preferences('low', 'windowed'))

    def test_matching_report_passes(self):
        report = self.verdict('boot\nGODOT_SMOKE_OK menu snapshots=0' + REPORT + '\n')
        self.assertEqual(('high', 'fullscreen', [1920, 1200]), (report['graphics'], report['window'], report['size']))
        self.assertEqual('Apple Paravirtual device', report['adapter'])

    def test_v0350_native_crash_fails(self):
        # The real v0.35.0 symptom on hosted CI: SIGSEGV before any smoke line.
        with self.assertRaisesRegex(RuntimeError, 'exited -11'):
            self.verdict('Too many mipmaps requested\n', exit_code=-11)

    def test_errors_missing_line_and_unapplied_settings_fail(self):
        line = 'GODOT_SMOKE_OK menu snapshots=0'
        failures = (('SCRIPT ERROR: boom\n' + line + REPORT, 'engine/script error'),
                    ('nothing rendered', 'exactly one'),
                    (line + REPORT + '\n' + line + REPORT, 'exactly one'),
                    (line + REPORT.replace('graphics=high', 'graphics=medium'), 'preset was not applied'),
                    (line + REPORT.replace('window=fullscreen', 'window=windowed'), 'display mode was not applied'),
                    (line + REPORT.replace('1920x1200', '2x2'), 'implausibly small'),
                    (line + REPORT.replace('Apple Paravirtual device', 'llvmpipe'), 'GPU adapter'),
                    (line, 'lacks the applied'))
        for log, message in failures:
            with self.subTest(message=message), self.assertRaisesRegex(RuntimeError, message):
                self.verdict(log)

    def test_gameplay_requires_authority_snapshots_and_capture(self):
        with self.assertRaisesRegex(RuntimeError, 'no authority snapshots'):
            self.verdict('GODOT_SMOKE_OK gameplay snapshots=0' + REPORT, case=('high', 'fullscreen', 'gameplay'))
        self.capture.write_bytes(b'not a png')
        with self.assertRaisesRegex(RuntimeError, 'No rendered viewport capture'):
            self.verdict('GODOT_SMOKE_OK menu snapshots=0' + REPORT)

    def test_legacy_reports_are_explicit_and_marked(self):
        self.assertTrue(self.verdict('GODOT_SMOKE_OK menu snapshots=0', legacy=True)['legacyReport'])

    def test_only_disposable_desktops_run_without_explicit_consent(self):
        self.assertTrue(gate.disposable_desktop('Mac16,7', {'GITHUB_ACTIONS': 'true'}))
        self.assertTrue(gate.disposable_desktop('VirtualMac2,1', {}))
        self.assertFalse(gate.disposable_desktop('Mac16,7', {}))


if __name__ == '__main__':
    unittest.main()

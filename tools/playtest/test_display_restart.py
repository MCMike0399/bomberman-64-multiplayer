"""The fresh process must follow parent exit and retain its own authenticated fixture and receipt."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock

import display_restart


class DisplayRestartTests(unittest.TestCase):
    def test_live_parent_is_rejected_before_starting_another_renderer(self):
        with self.assertRaisesRegex(RuntimeError, 'parent process to exit'):
            display_restart.run(Path('/unused'), [], {'displayRestartRequested': True}, Mock(poll=lambda: None))

    def test_actual_serial_process_uses_the_saved_fixture_and_cleans_only_its_copy(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            output = root / 'restart'
            output.mkdir()
            identifier = 'd' * 32
            preference = root / ('qa-preferences-' + identifier + '.cfg')
            preference.write_text('fullscreen=false')
            digest = hashlib.sha256(preference.read_bytes()).hexdigest()
            request = {'fixtureId': identifier, 'preferencesPath': str(preference), 'preferencesSha256': digest}
            (output / 'request.json').write_text(json.dumps(request))
            parent = {'displayRestartRequested': True, 'catalogSha256': 'c' * 64, 'release': {'version': '1.2.3'},
                      'platform': 'macOS', 'renderingDriver': 'metal'}
            child = dict(parent, schemaVersion=1, scenario='display-settings', completedScenarios=['display-settings'],
                         passed=True, displayRestoration=True, renderingMethod='forward_plus', adapter='Fixture GPU',
                         screenshotMode='graphical', assertions=6, checks=[{'passed': True}] * 6, screenshots=['fixture.png'])
            script = root / 'child.py'
            script.write_text('import json,sys\nfrom pathlib import Path\n' +
                              'output=Path(next(a.split("=",1)[1] for a in sys.argv if a.startswith("--playtest-output=")))\n' +
                              'assert "--playtest-restore-display" in sys.argv\n' +
                              'assert "--playtest-preferences=' + identifier + '" in sys.argv\n' +
                              '(output/"summary.json").write_text(' + repr(json.dumps(child)) + ')\n')
            parent_process = subprocess.Popen([sys.executable, '-c', 'pass'])
            parent_process.wait(timeout=5)
            receipt = display_restart.run(root, [sys.executable, str(script), '--', '--display-windowed'], parent, parent_process)
            self.assertTrue(receipt['passed'])
            self.assertEqual(90, receipt['timeout'])
            self.assertNotIn('--display-windowed', receipt['command'])
            self.assertFalse(preference.exists())
            self.assertEqual(child, json.loads((root / 'display-restart.json').read_text())['summary'])
            preference.write_text('tampered')
            with self.assertRaisesRegex(RuntimeError, 'does not match'):
                display_restart.run(root, [], parent, parent_process)


if __name__ == '__main__': unittest.main()

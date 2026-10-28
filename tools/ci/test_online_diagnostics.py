"""A recorded failing child must fail CI even when a later journey passes."""
from contextlib import nullcontext, redirect_stdout
import io
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import online_diagnostics as diagnostic


class OnlineDiagnosticExitTests(unittest.TestCase):
    def run_diagnostic(self, outcomes):
        class Suite:
            def __init__(self, *args, **kwargs): pass
            def server(self, **kwargs): return nullcontext('http://owned-fixture.invalid')

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); artifacts = root / 'artifacts'; artifacts.mkdir()
            item = diagnostic.desktop.names('windows-x64')
            (artifacts / item['metadata']).write_text(json.dumps({
                'revision': 'a' * 40, 'release': {'version': '1.0.0'}, 'files': {}}))

            def run(command, **kwargs):
                if command[0] == 'git': return subprocess.CompletedProcess(command, 0, 'b' * 40)
                if '--scenario' in command:
                    scenario = command[command.index('--scenario') + 1]
                    output = Path(command[command.index('--output') + 1]) / 'child'
                    output.mkdir()
                    code, passed, assertions = outcomes[scenario]
                    (output / 'summary.json').write_text(json.dumps({
                        'passed': passed, 'assertions': assertions,
                        'failure': None if passed else 'retained failing predicate'}))
                    return subprocess.CompletedProcess(command, code)
                return subprocess.CompletedProcess(command, 0)  # installer fixture

            def load(name, path):
                return SimpleNamespace(extract=lambda *args: None) if name == 'online_verifier' else SimpleNamespace(Suite=Suite)

            with patch.object(diagnostic, 'ROOT', root), patch.object(diagnostic, 'load', side_effect=load), \
                 patch.object(diagnostic.desktop, 'inspect_archive'), patch.object(diagnostic, 'inspect_server'), \
                 patch.object(diagnostic.subprocess, 'run', side_effect=run), \
                 patch.object(diagnostic.sys, 'argv', ['online_diagnostics.py', 'windows-x64', *outcomes]), \
                 redirect_stdout(io.StringIO()):
                code = diagnostic.main()
            report = json.loads((artifacts / 'online-diagnostics/online-diagnostic.json').read_text())
            self.assertEqual(list(outcomes), [row['scenario'] for row in report['results']])
            return code

    def test_failed_first_journey_is_retained_and_fails_ci_after_a_passing_journey(self):
        self.assertEqual(1, self.run_diagnostic({'first': (1, False, 6), 'last': (0, True, 20)}))

    def test_missing_or_false_positive_completion_cannot_succeed(self):
        for outcome in [(1, True, 20), (0, False, 20), (0, None, None), (0, True, 0)]:
            with self.subTest(outcome=outcome):
                self.assertEqual(1, self.run_diagnostic({'incomplete': outcome}))

    def test_every_completed_journey_succeeds(self):
        self.assertEqual(0, self.run_diagnostic({'first': (0, True, 20), 'last': (0, True, 6)}))

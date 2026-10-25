"""Release preparation and crash recovery exercise actual files and adapter processes."""
import json
import io
import contextlib
import os
from pathlib import Path
import tempfile
import unittest
import subprocess
import traceback
import urllib.error
import verify_deployment
from unittest.mock import patch

from deployment_state import Journal, locked, recover, write_json
import release


class ReleaseOperationsTests(unittest.TestCase):
    def test_prepare_increments_build_preserves_schema_and_refuses_downgrade(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / 'Release.props').write_text('<Project><PropertyGroup><BlastVersion>1.2.3</BlastVersion><BlastBuild>42</BlastBuild><BlastProtocolRevision>5</BlastProtocolRevision></PropertyGroup></Project>')
            (root / 'export_presets.cfg').write_text('application/short_version="1.2.3"\napplication/version="42"\n')
            result = release.prepare('1.3.0', root=root)
            self.assertEqual('5/1.3.0/43', result['admissionToken'])
            with self.assertRaises(ValueError): release.prepare('1.3.0', root=root)
            with self.assertRaises(ValueError): release.prepare('1.4.0', protocol=4, root=root)
            self.assertEqual(result, release.check_exports(root))

    def test_dispatch_does_not_mutate_github_when_prerequisites_fail(self):
        with patch.object(release, 'doctor', return_value={'operator adapter': False}), patch.object(release, 'gh') as github:
            with self.assertRaisesRegex(RuntimeError, 'operator adapter'):
                release.dispatch('example/game', '12', True)
        github.assert_not_called()

    def test_doctor_reports_missing_infrastructure_without_disclosing_values(self):
        replies = [json.dumps([{'variables': [{'name': 'BLAST_RELEASE_SERVER_URLS', 'value': '{"private":"https://secret.example"}'}]}]),
                   '[{"secrets":[{"name":"BLAST_RELEASE_SERVER_URLS"}]}]', '{"environments":[]}', '[{"runners":[]}]']
        with patch.object(release, 'gh', side_effect=replies), patch.object(release, 'check_exports', return_value={'version': '1.0.0'}):
            checks = release.doctor('example/game', True)
        self.assertFalse(checks['production environment exists'])
        self.assertFalse(checks['online dedicated deployment runner'])
        self.assertTrue(checks['deployment target secret configured'])
        self.assertFalse(checks['worker placement secret configured'])
        self.assertNotIn('secret.example', json.dumps(checks))

    def test_interrupted_apply_recovers_from_disk_with_a_real_adapter(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            run = root / '12'
            run.mkdir()
            adapter = root / 'adapter'
            adapter.write_text('#!/usr/bin/env python3\nimport pathlib,sys\np=pathlib.Path(sys.argv[2]).parent\nwith (p/"calls").open("a") as f: f.write(sys.argv[1]+"\\n")\n')
            adapter.chmod(0o700)
            write_json(run / 'deployment.json', {'artifacts': str(run)})
            journal = Journal(run / 'journal.json', adapter)
            journal.record('drain', 'completed')
            journal.record('apply', 'started')
            recover(root, '12')
            self.assertEqual(['rollback', 'resume'], (run / 'calls').read_text().splitlines())
            self.assertEqual('recovered', Journal(run / 'journal.json').value['status'])
            self.assertEqual(0o600, (run / 'journal.json').stat().st_mode & 0o777)
            with self.assertRaisesRegex(RuntimeError, 'already complete'): recover(root, '12')

    def test_failed_rollback_never_resumes_and_remains_recoverable(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            run = root / '12'
            run.mkdir()
            adapter = root / 'adapter'
            adapter.write_text('#!/bin/sh\nexit 1\n')
            adapter.chmod(0o700)
            write_json(run / 'deployment.json', {})
            journal = Journal(run / 'journal.json', adapter)
            journal.record('apply', 'started')
            with self.assertRaisesRegex(RuntimeError, 'rollback'): recover(root, '12')
            disk = Journal(run / 'journal.json').value
            self.assertEqual('incomplete', disk['status'])
            self.assertNotIn('resume', [item['phase'] for item in disk['events']])

    def test_probe_timeout_never_exposes_the_private_origin_in_tracebacks(self):
        private = 'https://private-worker.example'
        failure = subprocess.TimeoutExpired(['dotnet', 'run', '--', private], 125, output=b'private diagnostics')
        with patch.object(verify_deployment, 'build_probe', return_value=(Path('/fixture'), Path('/fixture/probe'))), patch.object(verify_deployment.subprocess, 'run', side_effect=failure), patch.object(verify_deployment, 'private_failure_log', return_value='/private/failure.log') as log:
            try:
                verify_deployment.verify_connections({'fallback': private})
            except Exception:
                output = traceback.format_exc()
            else:
                self.fail('A timed-out probe must fail')
        self.assertNotIn(private, output)
        log.assert_called_once()

    def test_actions_masks_individual_origins_and_hostnames_with_command_escaping(self):
        output = io.StringIO()
        origin = 'https://private.example/path%25\n::warning::fixture'
        with patch.dict(os.environ, {'BLAST_RELEASE_SERVER_URLS': json.dumps({'fallback': origin})}), contextlib.redirect_stdout(output):
            verify_deployment.mask_configuration()
        commands = output.getvalue().splitlines()
        self.assertEqual(2, len(commands))
        self.assertEqual('::add-mask::https://private.example/path%2525%0A::warning::fixture', commands[0])
        self.assertEqual('::add-mask::private.example', commands[1])

    def test_https_failure_never_exposes_private_origins_in_tracebacks(self):
        private = 'private-worker.example'
        failure = urllib.error.URLError('certificate hostname mismatch: ' + private)
        with patch.object(verify_deployment.urllib.request, 'build_opener') as opener, patch.object(verify_deployment, 'private_failure_log', return_value='/private/failure.log') as log:
            opener.return_value.open.side_effect = failure
            try:
                verify_deployment.request('https://' + private, '/game')
            except Exception:
                output = traceback.format_exc()
            else:
                self.fail('TLS validation failure must fail readiness')
        self.assertNotIn(private, output)
        log.assert_called_once()

    def test_recovery_without_started_drain_calls_no_adapter(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            run = root / '12'
            run.mkdir()
            write_json(run / 'deployment.json', {})
            journal = Journal(run / 'journal.json', Path('/missing/operator/adapter'))
            journal.record('transaction', 'prepared')
            with patch.object(Journal, 'call') as command:
                recover(root, '12')
            command.assert_not_called()
            self.assertEqual('recovered', Journal(run / 'journal.json').value['status'])

    def test_recovery_rejects_checkout_adapter_before_execution(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            run = root / '12'
            run.mkdir()
            write_json(run / 'deployment.json', {})
            journal = Journal(run / 'journal.json', Path(__file__).with_name('deploy_release.py').resolve())
            journal.record('drain', 'started')
            with patch.object(Journal, 'call') as command, self.assertRaisesRegex(RuntimeError, 'outside the checkout'):
                recover(root, '12')
            command.assert_not_called()

    def test_lock_excludes_a_second_deployment_and_rejects_public_state(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with locked(root):
                with self.assertRaisesRegex(RuntimeError, 'Another deployment'):
                    with locked(root): pass
            root.chmod(0o755)
            with self.assertRaisesRegex(RuntimeError, '0700'):
                with locked(root): pass


if __name__ == '__main__':
    unittest.main()

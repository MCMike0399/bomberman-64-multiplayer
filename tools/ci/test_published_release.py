"""Published-release verification and the update gate's decisions, with real fixture signatures."""
import base64
import contextlib
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import update_from_published
import verify_published
import desktop_packages
import crossplay
import desktop_validation
from test_promotion import RELEASE, SHA, write_package, write_server
from test_release_signing import signer


class PublishedReleaseTests(unittest.TestCase):
    def test_historical_windows_only_release_remains_verifiable_after_linux_is_added(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            assets, trusted = self.signed_release(root)
            for name in desktop_packages.asset_names('linux-x64'):
                (assets / name).unlink()
            for name in (crossplay.RECEIPT, desktop_validation.MAC_RECEIPT,
                         'Blast-Circuit-linux-x64-update.json', 'Blast-Circuit-linux-x64-update.json.sig'):
                (assets / name).unlink()
            with patch.object(signer, 'ROOT', root), contextlib.redirect_stdout(io.StringIO()):
                signer.sign(assets / verify_published.ARCHIVE, root / 'fixture.pem', assets, assets / verify_published.SERVER_ARCHIVE)
            with patch.object(verify_published, 'PUBLIC_KEY', trusted):
                delivery = verify_published.verify_assets(assets, '0.15.0')
            self.assertIn(desktop_packages.names('windows-x64')['archive'], delivery['assets'])
            self.assertNotIn(desktop_packages.names('linux-x64')['archive'], delivery['assets'])

    def test_desktop_downloads_and_native_smoke_are_bound_by_the_delivery_signature(self):
        with tempfile.TemporaryDirectory() as temporary:
            assets, trusted = self.signed_release(Path(temporary))
            with patch.object(verify_published, 'PUBLIC_KEY', trusted):
                delivery = verify_published.verify_assets(assets, '0.15.0')
                for target in desktop_packages.TARGETS:
                    for name in desktop_packages.asset_names(target):
                        self.assertIn(name, delivery['assets'])
                target = next(iter(desktop_packages.TARGETS))
                smoke = assets / desktop_packages.names(target)['smoke']
                smoke.write_text(smoke.read_text().replace('"assertions": 200', '"assertions": 0'))
                with self.assertRaisesRegex(RuntimeError, 'signed hash/size'):
                    verify_published.verify_assets(assets, '0.15.0')

    def signed_release(self, root):
        key = root / 'fixture.pem'
        subprocess.run(['openssl', 'genpkey', '-algorithm', 'EC', '-pkeyopt', 'ec_paramgen_curve:P-256', '-out', str(key)],
                       check=True, capture_output=True)
        trusted = root / 'src/Bomber.Client/Updates/UpdatePublicKey.txt'
        trusted.parent.mkdir(parents=True)
        trusted.write_text(base64.b64encode(subprocess.check_output(['openssl', 'pkey', '-in', str(key), '-pubout', '-outform', 'DER'])).decode())
        assets = root / 'assets'
        assets.mkdir()
        write_package(assets)
        write_server(assets)
        with patch.object(signer, 'ROOT', root), contextlib.redirect_stdout(io.StringIO()):
            signer.sign(assets / verify_published.ARCHIVE, key, assets, assets / verify_published.SERVER_ARCHIVE)
        return assets, trusted

    def test_signed_release_verifies_and_any_tampering_fails(self):
        with tempfile.TemporaryDirectory() as temporary:
            assets, trusted = self.signed_release(Path(temporary))
            with patch.object(verify_published, 'PUBLIC_KEY', trusted):
                delivery = verify_published.verify_assets(assets, '0.15.0')
                self.assertEqual((SHA, RELEASE), (delivery['revision'], delivery['release']))
                with self.assertRaisesRegex(RuntimeError, 'updater manifest'):
                    verify_published.verify_assets(assets, '0.15.1')
                archive = assets / verify_published.ARCHIVE
                original = archive.read_bytes()
                archive.write_bytes(original + b'tampered')
                with self.assertRaisesRegex(RuntimeError, 'updater manifest'):
                    verify_published.verify_assets(assets, '0.15.0')
                archive.write_bytes(original)
                envelope = assets / 'Blast-Circuit-release.json'
                envelope.write_text(envelope.read_text().replace(SHA, 'b' * 40))
                with self.assertRaisesRegex(RuntimeError, 'Signature verification failed'):
                    verify_published.verify_assets(assets, '0.15.0')

    def test_wrong_trusted_key_rejects_genuine_signatures(self):
        with tempfile.TemporaryDirectory() as temporary:
            assets, _ = self.signed_release(Path(temporary))
            other = Path(temporary) / 'other.pem'
            subprocess.run(['openssl', 'genpkey', '-algorithm', 'EC', '-pkeyopt', 'ec_paramgen_curve:P-256', '-out', str(other)],
                           check=True, capture_output=True)
            wrong = Path(temporary) / 'wrong.txt'
            wrong.write_text(base64.b64encode(subprocess.check_output(['openssl', 'pkey', '-in', str(other), '-pubout', '-outform', 'DER'])).decode())
            with patch.object(verify_published, 'PUBLIC_KEY', wrong), self.assertRaisesRegex(RuntimeError, 'Signature'):
                verify_published.verify_assets(assets, '0.15.0')


class UpdateGateTests(unittest.TestCase):
    def run_gate(self, previous, candidate='0.15.0', cases=None):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / 'receipt'
            run = patch.object(update_from_published, 'run_update', return_value=cases or [])
            with patch.object(update_from_published, 'identity', return_value={'version': candidate, 'build': '10'}), \
                    patch.object(update_from_published, 'fetch_previous', return_value={'version': previous, 'sha256': 'c' * 64}), \
                    patch.object(update_from_published.sys, 'platform', 'darwin'), \
                    patch.object(update_from_published, 'no_background_updates'), \
                    patch.object(update_from_published.subprocess, 'run', return_value=type('Result', (), {'stdout': 'VirtualMac2,1'})()), \
                    run as transactions, contextlib.redirect_stdout(io.StringIO()):
                code = update_from_published.main(['--app', '/fixture/Blast Circuit.app', '--output', str(output)])
            return code, json.loads((output / 'summary.json').read_text()), transactions.called

    def test_release_candidate_runs_actual_transactions(self):
        code, receipt, ran = self.run_gate('0.14.0', cases=[{'name': name} for name in update_from_published.CASES])
        self.assertEqual((0, True, True), (code, receipt['passed'], ran))
        self.assertEqual('0.14.0', receipt['previous']['version'])

    def test_only_crash_free_failures_are_retried(self):
        passing = [{'name': name} for name in update_from_published.CASES]
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            calm = {'gameLogs': {}, 'crashReports': {}}
            crashed = {'gameLogs': {}, 'crashReports': {'Blast Circuit-2026.ips': ['"type" : "EXC_BAD_ACCESS"']}}
            with patch.object(update_from_published, 'run_transactions', side_effect=[RuntimeError('rolled back'), passing]), \
                    patch.object(update_from_published, 'diagnostics', return_value=calm), contextlib.redirect_stdout(io.StringIO()):
                receipt = {}
                self.assertEqual(passing, update_from_published.run_update(None, None, output, 1, None, receipt))
                self.assertEqual(1, len(receipt['failedAttempts']))
            with patch.object(update_from_published, 'run_transactions', side_effect=[RuntimeError('rolled back'), passing]) as runs, \
                    patch.object(update_from_published, 'diagnostics', return_value=crashed):
                with self.assertRaisesRegex(RuntimeError, 'crashed'):
                    update_from_published.run_update(None, None, output, 1, None, {})
                self.assertEqual(1, runs.call_count)
            with patch.object(update_from_published, 'run_transactions', side_effect=RuntimeError('slow')), \
                    patch.object(update_from_published, 'diagnostics', return_value=calm), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaisesRegex(RuntimeError, 'all 3 attempts'):
                    update_from_published.run_update(None, None, output, 1, None, {})
            # A late crash report from a retried attempt still fails a later pass.
            with patch.object(update_from_published, 'run_transactions', side_effect=[RuntimeError('rolled back'), passing]), \
                    patch.object(update_from_published, 'diagnostics', side_effect=[calm, crashed]), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaisesRegex(RuntimeError, 'earlier update attempt'):
                    update_from_published.run_update(None, None, output, 1, None, {})

    def test_crash_reports_are_awaited_and_scoped_to_this_gate(self):
        with tempfile.TemporaryDirectory() as temporary:
            home = Path(temporary)
            reports = home / 'Library/Logs/DiagnosticReports'
            reports.mkdir(parents=True)
            old = reports / 'Blast Circuit-old.ips'
            old.write_text('{"exception" : {"type":"EXC_BAD_ACCESS"}}')
            os.utime(old, (100, 100))
            with patch.object(update_from_published.Path, 'home', return_value=home):
                self.assertEqual({}, update_from_published.diagnostics(200, 200, wait=0)['crashReports'])
                (reports / 'Blast Circuit-new.ips').write_text('"exception" : {"type":"EXC_BAD_ACCESS","signal":"SIGSEGV"}')
                found = update_from_published.diagnostics(200, 200, wait=0)['crashReports']
            self.assertEqual(['Blast Circuit-new.ips'], list(found))
            self.assertIn('SIGSEGV', found['Blast Circuit-new.ips'][0])

    def test_already_published_version_skips_without_passing(self):
        code, receipt, ran = self.run_gate('0.15.0')
        self.assertEqual((0, False, False), (code, receipt['passed'], ran))
        self.assertIn('already', receipt['skipped'])

    def test_older_candidate_fails(self):
        code, receipt, ran = self.run_gate('0.16.0')
        self.assertEqual((1, False, False), (code, receipt['passed'], ran))
        self.assertIn('older than published', receipt['error'])


if __name__ == '__main__':
    unittest.main()

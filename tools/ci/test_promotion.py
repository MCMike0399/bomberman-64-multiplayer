"""Promotion failure fixtures; all GitHub operations are mocked."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import plistlib
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SPEC = importlib.util.spec_from_file_location('promotion', Path(__file__).with_name('promote_release.py'))
promotion = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(promotion)
SHA = 'a' * 40
REPO = 'example/game'
RELEASE = {'version': '0.15.0', 'build': '10', 'protocolRevision': 4, 'admissionToken': '4/0.15.0/10'}


def write_package(directory, version='0.15.0', revision=SHA):
    archive = directory / promotion.ARCHIVE
    with zipfile.ZipFile(archive, 'w') as output:
        output.writestr('Blast Circuit.app/Contents/Info.plist', plistlib.dumps({
            'CFBundleIdentifier': 'io.github.mcmike0399.blastcircuit', 'CFBundleShortVersionString': version, 'CFBundleVersion': RELEASE['build']}))
        for name in ('libSDL3.dylib', 'libusb-1.0.dylib', 'SDL-LICENSE.txt', 'libusb-LICENSE.txt'):
            output.writestr('Blast Circuit.app/Contents/Frameworks/Controllers/' + name, 'fixture')
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    (directory / (promotion.ARCHIVE + '.sha256')).write_text(digest + '  ' + promotion.ARCHIVE + '\n')
    (directory / 'build-metadata.json').write_text(json.dumps({'revision': revision, 'version': version, 'release': RELEASE, 'features': {'switch2UsbBackend': True}}))


def write_server(directory):
    files = {name: b'fixture' for name in ('Bomber.Server.dll', 'Bomber.Server.deps.json', 'Bomber.Server.runtimeconfig.json')}
    archive = directory / promotion.SERVER_ARCHIVE
    with zipfile.ZipFile(archive, 'w') as output:
        for name, contents in files.items():
            output.writestr('Blast-Circuit-server-dotnet10/' + name, contents)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    (directory / (promotion.SERVER_ARCHIVE + '.sha256')).write_text(digest + '  ' + promotion.SERVER_ARCHIVE + '\n')
    (directory / 'server-build-metadata.json').write_text(json.dumps({'revision': SHA, 'version': '0.15.0', 'release': RELEASE,
        'kind': 'framework-dependent-server', 'targetFramework': 'net10.0',
        'files': {name: hashlib.sha256(contents).hexdigest() for name, contents in files.items()}}))


class PromotionTests(unittest.TestCase):
    def setUp(self):
        fixture = patch.object(promotion, 'check_exports', return_value=RELEASE)
        fixture.start()
        self.addCleanup(fixture.stop)
        environment = patch.dict(os.environ, {}, clear=True)
        environment.start()
        self.addCleanup(environment.stop)
        main = patch.object(promotion, 'require_current_main')
        main.start()
        self.addCleanup(main.stop)
        upload = patch.object(promotion, 'verify_uploaded')
        upload.start()
        self.addCleanup(upload.stop)
        probe = patch.object(promotion, 'verify_connections')
        self.connection_probe = probe.start()
        self.addCleanup(probe.stop)

    def test_new_release_upload_failure_never_exposes_incomplete_public_release(self):
        calls = []
        def github(*arguments):
            calls.append(arguments)
            if arguments[0] == 'api': return '[]'
            if arguments[:2] == ('run', 'download'):
                destination = Path(arguments[arguments.index('--dir') + 1])
                write_package(destination) if arguments[arguments.index('--name') + 1] == promotion.ARTIFACT else write_server(destination)
            if arguments[:2] == ('release', 'create'):
                raise RuntimeError('upload interrupted')
            return ''
        with patch.object(promotion, 'inspect_run', return_value=SHA), patch.object(promotion, 'gh', side_effect=github), patch.object(promotion.subprocess, 'run'), patch.object(promotion, 'verify'), patch.dict(os.environ, {'BLAST_RELEASE_SERVER_URLS': '{"public":"https://game.example"}', 'BLAST_RELEASE_PLACEMENTS': '{"public":{"serverId":"primary","region":"primary"}}'}):
            with self.assertRaisesRegex(RuntimeError, 'upload interrupted'):
                promotion.main(['--run-id', '12', '--version', '0.15.0', '--repository', REPO, '--publish', '--signing-key', '/fixture/private-key'])
        created = next(call for call in calls if call[:2] == ('release', 'create'))
        self.assertIn('--draft', created, 'Interrupted upload must remain invisible to players')
        self.assertFalse(any(call[:2] == ('release', 'edit') for call in calls))

    def test_publication_requires_matching_deployed_servers(self):
        def github(*arguments):
            if arguments[0] == 'api': return '[]'
            if arguments[:2] == ('run', 'download'):
                destination = Path(arguments[arguments.index('--dir') + 1])
                write_package(destination) if arguments[arguments.index('--name') + 1] == promotion.ARTIFACT else write_server(destination)
            return ''
        with patch.object(promotion, 'inspect_run', return_value=SHA), patch.object(promotion, 'gh', side_effect=github), patch.object(promotion.subprocess, 'run'):
            with self.assertRaisesRegex(RuntimeError, 'deployment'):
                promotion.main(['--run-id', '12', '--version', '0.15.0', '--repository', REPO, '--publish', '--signing-key', '/fixture/key'])

    def test_missing_controller_feature_manifest_prevents_promotion(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            write_package(directory)
            (directory / 'build-metadata.json').write_text(json.dumps({'revision': SHA, 'version': '0.15.0', 'release': RELEASE}))
            with self.assertRaisesRegex(RuntimeError, 'feature manifest'):
                promotion.inspect_package(directory, SHA, '0.15.0')

    def test_claimed_controller_feature_requires_real_archived_libraries(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            write_package(directory)
            archive = directory / promotion.ARCHIVE
            with zipfile.ZipFile(archive) as source:
                entries = {name: source.read(name) for name in source.namelist() if not name.endswith('libusb-1.0.dylib')}
            with zipfile.ZipFile(archive, 'w') as output:
                for name, contents in entries.items():
                    output.writestr(name, contents)
            digest = hashlib.sha256(archive.read_bytes()).hexdigest()
            (directory / (promotion.ARCHIVE + '.sha256')).write_text(digest + '  ' + promotion.ARCHIVE + '\n')
            with self.assertRaisesRegex(RuntimeError, 'backend is incomplete'):
                promotion.inspect_package(directory, SHA, '0.15.0')

    def test_rejects_fork_nonmain_failed_and_wrong_workflow_runs(self):
        valid = {'head_repository': {'full_name': REPO}, 'head_branch': 'main', 'status': 'completed',
                 'conclusion': 'success', 'event': 'push', 'path': '.github/workflows/ci.yml', 'head_sha': SHA}
        changes = ({'head_repository': {'full_name': 'fork/game'}}, {'head_branch': 'topic'},
                   {'conclusion': 'failure'}, {'path': '.github/workflows/other.yml'}, {'event': 'pull_request'})
        for change in changes:
            with self.subTest(change=change), patch.object(promotion, 'gh', return_value=json.dumps(dict(valid, **change))):
                with self.assertRaisesRegex(RuntimeError, 'successful completed Verification'):
                    promotion.inspect_run(REPO, '12')

    def test_rejects_successful_run_with_skipped_package(self):
        valid = {'head_repository': {'full_name': REPO}, 'head_branch': 'main', 'status': 'completed',
                 'conclusion': 'success', 'event': 'push', 'path': '.github/workflows/ci.yml', 'head_sha': SHA}
        with patch.object(promotion, 'gh', side_effect=[json.dumps(valid), '{"jobs":[]}']):
            with self.assertRaisesRegex(RuntimeError, 'jobs did not all pass'):
                promotion.inspect_run(REPO, '12')

    def test_rejects_checksum_revision_and_version_mismatches(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            write_package(directory)
            with self.assertRaisesRegex(RuntimeError, 'revision or version'):
                promotion.inspect_package(directory, 'b' * 40, '0.15.0')
            with self.assertRaisesRegex(RuntimeError, 'revision or version'):
                promotion.inspect_package(directory, SHA, '0.14.0')
            (directory / promotion.ARCHIVE).write_bytes(b'tampered')
            with self.assertRaisesRegex(RuntimeError, 'SHA-256 mismatch'):
                promotion.inspect_package(directory, SHA, '0.15.0')

    def test_existing_tag_is_never_moved(self):
        with patch.object(promotion, 'inspect_run', return_value=SHA), patch.object(promotion, 'gh', return_value='[{"ref":"refs/tags/v0.15.0"}]') as github:
            with self.assertRaisesRegex(RuntimeError, 'already exists'):
                promotion.main(['--run-id', '12', '--version', '0.15.0', '--repository', REPO])
            self.assertEqual(1, github.call_count)

    def test_only_named_checked_artifact_becomes_exact_sha_draft(self):
        calls = []
        def github(*arguments):
            calls.append(arguments)
            if arguments[0] == 'api':
                return '[]'
            if arguments[:2] == ('run', 'download'):
                name = arguments[arguments.index('--name') + 1]
                self.assertIn(name, (promotion.ARTIFACT, promotion.SERVER_ARTIFACT))
                destination = Path(arguments[arguments.index('--dir') + 1])
                write_package(destination) if name == promotion.ARTIFACT else write_server(destination)
            return ''
        with patch.object(promotion, 'inspect_run', return_value=SHA), patch.object(promotion, 'gh', side_effect=github), contextlib.redirect_stdout(io.StringIO()):
            promotion.main(['--run-id', '12', '--version', 'v0.15.0', '--repository', REPO, '--expected-sha', SHA])
        created = calls[-1]
        self.assertEqual(('release', 'create', 'v0.15.0'), created[:3])
        self.assertIn('--draft', created)
        self.assertEqual(SHA, created[created.index('--target') + 1])
        self.assertNotIn('--latest', created)


    def test_publication_requires_a_signing_key(self):
        calls = []
        def github(*arguments):
            calls.append(arguments)
            if arguments[0] == 'api': return '[]'
            if arguments[:2] == ('run', 'download'):
                destination = Path(arguments[arguments.index('--dir') + 1])
                write_package(destination) if arguments[arguments.index('--name') + 1] == promotion.ARTIFACT else write_server(destination)
            return ''
        with patch.object(promotion, 'inspect_run', return_value=SHA), patch.object(promotion, 'gh', side_effect=github), patch.dict(os.environ, {'BLAST_RELEASE_SERVER_URLS': '{"public":"https://game.example"}', 'BLAST_RELEASE_PLACEMENTS': '{"public":{"serverId":"primary","region":"primary"}}'}):
            with self.assertRaisesRegex(RuntimeError, 'signing-key'):
                promotion.main(['--run-id', '12', '--version', '0.15.0', '--repository', REPO, '--publish'])
        self.assertFalse(any(call[:2] == ('release', 'create') for call in calls))

    def test_explicit_signed_publication_attaches_manifest_and_signature(self):
        calls = []
        def github(*arguments):
            calls.append(arguments)
            if arguments[0] == 'api': return '[]'
            if arguments[:2] == ('run', 'download'):
                destination = Path(arguments[arguments.index('--dir') + 1])
                write_package(destination) if arguments[arguments.index('--name') + 1] == promotion.ARTIFACT else write_server(destination)
            return ''
        with patch.object(promotion, 'inspect_run', return_value=SHA), patch.object(promotion, 'gh', side_effect=github), patch.object(promotion.subprocess, 'run') as signer, patch.dict(os.environ, {'BLAST_RELEASE_SERVER_URLS': '{"public":"https://game.example"}', 'BLAST_RELEASE_PLACEMENTS': '{"public":{"serverId":"primary","region":"primary"}}'}), patch.object(promotion, 'verify') as deployed, patch.object(promotion, 'require_current_main', side_effect=RuntimeError('main advanced')), contextlib.redirect_stdout(io.StringIO()):
            promotion.main(['--run-id', '12', '--version', '0.15.0', '--repository', REPO, '--publish', '--signing-key', '/fixture/private-key'])
        self.connection_probe.assert_called_once()
        self.assertEqual(2, deployed.call_count)
        deployed.assert_called_with({'public': 'https://game.example'}, RELEASE, SHA)
        signer.assert_called_once()
        self.assertEqual(('release', 'edit', 'v0.15.0'), calls[-1][:3])
        self.assertIn('--latest', calls[-1])
        created = next(call for call in calls if call[:2] == ('release', 'create'))
        self.assertIn('--draft', created)
        self.assertTrue(any(value.endswith('/Blast-Circuit-update.json') for value in created))
        self.assertTrue(any(value.endswith('/Blast-Circuit-update.json.sig') for value in created))


if __name__ == '__main__':
    unittest.main()

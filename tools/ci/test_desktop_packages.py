"""Reject incomplete runtimes, tampered inventories, unsafe ZIPs and unrelated smoke evidence."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import desktop_packages as desktop
import desktop_validation as validation
import download_formats
import promote_release

SHA = 'a' * 40
RELEASE = {'version': '1.2.3', 'build': '42', 'protocolRevision': 6, 'admissionToken': '6/1.2.3/42'}


def write_desktop(directory, revision=SHA, release=None):
    release = release or RELEASE
    for target, settings in desktop.TARGETS.items():
        item = desktop.names(target)
        files = {name: b'fixture runtime' for name in (
            settings['executable'], settings['runtime'], settings['webrtc'], 'Bomberman64Multiplayer.dll',
            'BlastCircuit.pck', 'Notices/Blast-Circuit-MIT.txt', 'Notices/WebRTC-SOURCE.txt', 'Notices/DotNet-LICENSE.TXT')}
        files['release.json'] = json.dumps({'revision': revision, 'release': release, 'target': target}).encode()
        files['data/Bomberman64Multiplayer.runtimeconfig.json'] = b'{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}'
        with zipfile.ZipFile(directory / item['archive'], 'w') as bundle:
            for name, data in files.items():
                entry = zipfile.ZipInfo(item['root'] + '/' + name)
                entry.external_attr = 0o100755 << 16
                bundle.writestr(entry, data)
        (directory / (item['archive'] + '.sha256')).write_text(desktop.digest(directory / item['archive']) + '  ' + item['archive'])
        (directory / item['metadata']).write_text(json.dumps({
            'schemaVersion': 1, 'target': target, 'revision': revision, 'release': release, 'driver': settings['driver'],
            'automaticUpdates': True, 'files': {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}}))
        cases = []
        for scenario in validation.HEADLESS:
            cases.append({'passed': True, 'platform': settings['os'], 'release': dict(release, revision=revision),
                          'scenario': scenario, 'catalogSha256': validation.catalog_digest(),
                          'completedScenarios': validation.expanded(scenario, validation.catalog()),
                          'assertions': 200, 'runtimeErrors': [], 'screenshotMode': 'disabled-headless'})
        (directory / item['smoke']).write_text(json.dumps({
            'schemaVersion': 2, 'passed': True, 'target': target, 'revision': revision, 'release': release,
            'archiveSha256': desktop.digest(directory / item['archive']), 'os': settings['os'],
            'suite': 'headless-regression', 'cases': cases}))
        (directory / download_formats.FILES[target]).write_bytes(b'MZ fixture installer' if target == 'windows-x64' else b'\x7fELF' + b'\x00'*4 + b'AI\x02 fixture AppImage')
        download_formats.record(directory, target, revision, release, item['archive'])
        parts = release['version'].split('.')
        future = dict(release, version='.'.join([*parts[:2], str(int(parts[2]) + 1)]))
        cases = []
        for name in ('successful-install', 'failed-launch-rollback'):
            cases.append({'name': name, 'result': ('installed ' + future['version']) if name == 'successful-install' else 'failed ' + future['version'] + ' launch-rollback',
                          'fromVersion': release['version'], 'toVersion': future['version'], 'seconds': 5,
                          'oldGameplay': True, 'inventoryMatched': True, 'restoredGameplay': name != 'successful-install',
                          'uninstallVerified': True, 'health': {'version': future['version'], 'driver': settings['driver'], 'method': 'forward_plus', 'adapter': 'Fixture GPU'}})
        (directory / item['updater_test']).write_text(json.dumps({'schemaVersion': 1, 'passed': True, 'platform': target,
            'candidate': {'release': release, 'revision': revision}, 'fixture': {'release': future, 'revision': revision},
            'candidateArchiveSha256': desktop.digest(directory / item['archive']), 'cases': cases, 'runtimeErrors': []}))



class DesktopPackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        write_desktop(self.root)
        self.target = next(iter(desktop.TARGETS))
        self.item = desktop.names(self.target)

    def test_complete_runtime_and_native_exported_smoke_pass(self):
        desktop.inspect(self.root, SHA, RELEASE)

    def test_linux_executable_permission_is_part_of_the_archive_contract(self):
        target = 'linux-x64'
        item = desktop.names(target)
        archive = self.root / item['archive']
        with zipfile.ZipFile(archive) as bundle:
            entries = [(entry, bundle.read(entry)) for entry in bundle.infolist()]
        with zipfile.ZipFile(archive, 'w') as bundle:
            for entry, data in entries:
                if entry.filename.endswith('/' + desktop.TARGETS[target]['executable']):
                    entry.external_attr = 0o100644 << 16
                bundle.writestr(entry, data)
        (self.root / (archive.name + '.sha256')).write_text(desktop.digest(archive) + '  ' + archive.name)
        with self.assertRaisesRegex(RuntimeError, 'executable permission'):
            desktop.inspect_archive(self.root, target, SHA, RELEASE)

    def test_archive_tampering_fails_before_extraction(self):
        with (self.root / self.item['archive']).open('ab') as stream:
            stream.write(b'tampered')
        with self.assertRaisesRegex(RuntimeError, 'checksum mismatch'):
            desktop.inspect(self.root, SHA, RELEASE)

    def test_smoke_from_other_os_archive_or_release_never_qualifies(self):
        path = self.root / self.item['smoke']
        original = path.read_text()
        for changes in ({'passed': False}, {'os': 'Darwin'}, {'archiveSha256': 'b' * 64},
                        {'revision': 'b' * 40}, {'release': {}}, {'schemaVersion': 1}, {'suite': 'unknown'}):
            value = json.loads(original)
            value.update(changes)
            path.write_text(json.dumps(value))
            with self.subTest(changes=changes), self.assertRaises(RuntimeError):
                desktop.inspect(self.root, SHA, RELEASE)

    def test_matching_metadata_cannot_authorize_traversal_or_links(self):
        for name, mode in (('../escape', 0o100644), ('/absolute', 0o100644),
                           (self.item['root'] + '/escape', 0o120777), (self.item['root'] + '/C:drive', 0o100644)):
            write_desktop(self.root)
            archive = self.root / self.item['archive']
            with zipfile.ZipFile(archive, 'a') as bundle:
                entry = zipfile.ZipInfo(name)
                entry.external_attr = mode << 16
                bundle.writestr(entry, b'unsafe')
            (self.root / (archive.name + '.sha256')).write_text(desktop.digest(archive) + '  ' + archive.name)
            with self.subTest(name=name), self.assertRaisesRegex(RuntimeError, 'Unsafe'):
                desktop.inspect_archive(self.root, self.target, SHA, RELEASE)

    def test_inventory_cannot_omit_the_native_runtime(self):
        archive = self.root / self.item['archive']
        with zipfile.ZipFile(archive) as bundle:
            entries = {name: bundle.read(name) for name in bundle.namelist() if not name.endswith(desktop.TARGETS[self.target]['runtime'])}
        with zipfile.ZipFile(archive, 'w') as bundle:
            for name, data in entries.items():
                bundle.writestr(name, data)
        (self.root / (archive.name + '.sha256')).write_text(desktop.digest(archive) + '  ' + archive.name)
        metadata_path = self.root / self.item['metadata']
        metadata = json.loads(metadata_path.read_text())
        del metadata['files'][desktop.TARGETS[self.target]['runtime']]
        metadata_path.write_text(json.dumps(metadata))
        with self.assertRaisesRegex(RuntimeError, 'runtime is incomplete'):
            desktop.inspect_archive(self.root, self.target, SHA, RELEASE)

    def test_successful_main_run_requires_desktop_jobs_and_artifacts(self):
        run = {'head_repository': {'full_name': 'example/game'}, 'head_branch': 'main',
               'status': 'completed', 'conclusion': 'success', 'event': 'push',
               'path': '.github/workflows/ci.yml', 'head_sha': SHA}
        jobs = {'Public contracts and audio', 'Trusted managed and network', 'Trusted Godot headless',
                'Trusted Apple Silicon package', promote_release.release_gates.STARTUP_JOB, promote_release.release_gates.UPDATE_JOB}
        artifacts = [promote_release.ARTIFACT, promote_release.SERVER_ARTIFACT,
                     promote_release.release_gates.STARTUP_ARTIFACT, promote_release.release_gates.UPDATE_ARTIFACT]
        def responses(enabled, complete=True):
            return [json.dumps(run), json.dumps({'jobs': [{'name': name, 'conclusion': 'success'} for name in jobs | (desktop.jobs() if enabled else set())]}),
                    json.dumps({'artifacts': [{'name': name} for name in artifacts + (desktop.artifacts() if complete else [])]})]
        with patch.object(promote_release, 'gh', side_effect=responses(False)), self.assertRaisesRegex(RuntimeError, 'jobs did not all pass'):
            promote_release.inspect_run('example/game', '12')
        with patch.object(promote_release, 'gh', side_effect=responses(True, False)), self.assertRaisesRegex(RuntimeError, 'tested artifact'):
            promote_release.inspect_run('example/game', '12')
        with patch.object(promote_release, 'gh', side_effect=responses(True)):
            self.assertEqual(SHA, promote_release.inspect_run('example/game', '12'))


if __name__ == '__main__':
    unittest.main()

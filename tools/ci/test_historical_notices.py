"""Immutable published MIT archives remain valid without weakening candidate GPL checks."""
import hashlib
import json
from pathlib import Path
import plistlib
import shutil
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import desktop_packages as desktop
import network_blackout_prepare as prepare
import promote_release as promotion
from test_desktop_packages import write_desktop

MIT = 'Blast-Circuit-MIT.txt'
GPL = 'Blast-Circuit-GPL-3.0.txt'
SHIPPED = 'af93089f385290de2f30954ef2822bd4051324da'
LATEST_MIT = '68002f2c28314d2ed91301cf37088bea1d408d1a'


def release(version='0.45.0', build='56'):
    return dict(version=version, build=build, protocolRevision=6, admissionToken=f'6/{version}/{build}')


def desktop_fixture(root, revision, identity, notice=MIT):
    write_desktop(root, revision, identity)
    target = 'linux-x64'; item = desktop.names(target)
    path = root / item['archive']
    with zipfile.ZipFile(path) as bundle:
        files = {entry.filename.removeprefix(item['root'] + '/'): bundle.read(entry) for entry in bundle.infolist()}
    files['Notices/' + notice] = files.pop('Notices/' + GPL)
    for name in ('Bomber.Client.dll', 'Bomber.Game.dll', 'Burbuja.Realtime.Abstractions.dll'):
        files[name] = b'fixture managed runtime'
    with zipfile.ZipFile(path, 'w') as bundle:
        for name, data in files.items():
            entry = zipfile.ZipInfo(item['root'] + '/' + name); entry.external_attr = 0o100755 << 16
            bundle.writestr(entry, data)
    (root / (item['archive'] + '.sha256')).write_text(desktop.digest(path) + '  ' + item['archive'])
    metadata_path = root / item['metadata']; metadata = json.loads(metadata_path.read_text())
    metadata['files'] = {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}
    metadata_path.write_text(json.dumps(metadata))


def server_fixture(root, revision, identity, notice=MIT):
    files = {name: b'fixture runtime' for name in ('Bomber.Server.dll', 'Bomber.Server.deps.json', 'Bomber.Server.runtimeconfig.json', 'Notices/Simulation-MIT.txt')}
    files['Notices/' + notice] = b'fixture project notice'
    archive = root / promotion.SERVER_ARCHIVE
    with zipfile.ZipFile(archive, 'w') as bundle:
        for name, data in files.items(): bundle.writestr('Blast-Circuit-server-dotnet10/' + name, data)
    (root / (archive.name + '.sha256')).write_text(desktop.digest(archive) + '  ' + archive.name + '\n')
    (root / 'server-build-metadata.json').write_text(json.dumps(dict(revision=revision, version=identity['version'], release=identity,
        kind='framework-dependent-server', targetFramework='net10.0', files={name: hashlib.sha256(data).hexdigest() for name, data in files.items()})))


class HistoricalNoticeTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def test_pinned_baseline_requires_explicit_legacy_notice_for_both_archives(self):
        for revision, identity in ((SHIPPED, release()), (LATEST_MIT, release('0.45.1', '57'))):
            with self.subTest(revision=revision):
                desktop_fixture(self.root, revision, identity); server_fixture(self.root, revision, identity)
                self.assertEqual(MIT, desktop.published_project_notice(revision, identity))
                with self.assertRaisesRegex(RuntimeError, 'GPL'):
                    desktop.inspect_archive(self.root, 'linux-x64', revision, identity)
                with self.assertRaisesRegex(RuntimeError, 'allowlist'):
                    promotion.inspect_server(self.root, revision, identity['version'], identity)
                desktop.inspect_archive(self.root, 'linux-x64', revision, identity, expected_project_notice=MIT)
                promotion.inspect_server(self.root, revision, identity['version'], identity, expected_project_notice=MIT)

    def test_unknown_or_new_source_cannot_choose_legacy_even_with_old_version(self):
        identity = release('0.45.1', '57'); revision = 'b' * 40
        desktop_fixture(self.root, revision, identity); server_fixture(self.root, revision, identity)
        self.assertEqual(GPL, desktop.published_project_notice(revision, identity))
        for inspect in (lambda: desktop.inspect_archive(self.root, 'linux-x64', revision, identity, expected_project_notice=MIT),
                        lambda: promotion.inspect_server(self.root, revision, identity['version'], identity, expected_project_notice=MIT)):
            with self.assertRaisesRegex(RuntimeError, 'historical'): inspect()

    def test_absent_identity_cannot_select_a_historical_notice(self):
        self.assertEqual(GPL, desktop.published_project_notice('b' * 40, None))
        with self.assertRaisesRegex(RuntimeError, 'historical'):
            desktop.project_notice('b' * 40, None, MIT)

    def test_legacy_notice_cannot_be_selected_for_wrong_pinned_version(self):
        identity = release('0.45.1', '57')
        desktop_fixture(self.root, SHIPPED, identity); server_fixture(self.root, SHIPPED, identity)
        with self.assertRaisesRegex(RuntimeError, 'historical'):
            desktop.inspect_archive(self.root, 'linux-x64', SHIPPED, identity, expected_project_notice=MIT)
        with self.assertRaisesRegex(RuntimeError, 'historical'):
            promotion.inspect_server(self.root, SHIPPED, identity['version'], identity, expected_project_notice=MIT)

    def test_legacy_selection_requires_exact_build_protocol_and_admission_identity(self):
        for field, value in (('build', '57'), ('protocolRevision', 7), ('admissionToken', '7/0.45.0/56')):
            with self.subTest(field=field):
                identity = release() | {field: value}
                desktop_fixture(self.root, SHIPPED, identity); server_fixture(self.root, SHIPPED, identity)
                with self.assertRaisesRegex(RuntimeError, 'historical'):
                    desktop.inspect_archive(self.root, 'linux-x64', SHIPPED, identity, expected_project_notice=MIT)
                with self.assertRaisesRegex(RuntimeError, 'historical'):
                    promotion.inspect_server(self.root, SHIPPED, identity['version'], identity, expected_project_notice=MIT)
                self.assertEqual(GPL, desktop.published_project_notice(SHIPPED, identity))

    def test_explicit_legacy_contract_requires_mit_instead_of_accepting_either_notice(self):
        identity = release()
        desktop_fixture(self.root, SHIPPED, identity, GPL); server_fixture(self.root, SHIPPED, identity, GPL)
        with self.assertRaisesRegex(RuntimeError, 'Blast-Circuit-MIT'):
            desktop.inspect_archive(self.root, 'linux-x64', SHIPPED, identity, expected_project_notice=MIT)
        with self.assertRaisesRegex(RuntimeError, 'allowlist'):
            promotion.inspect_server(self.root, SHIPPED, identity['version'], identity, expected_project_notice=MIT)

    def test_current_gpl_archive_still_passes_default_and_rejects_legacy_override(self):
        identity = release('0.46.0', '58'); revision = 'c' * 40
        desktop_fixture(self.root, revision, identity, GPL); server_fixture(self.root, revision, identity, GPL)
        desktop.inspect_archive(self.root, 'linux-x64', revision, identity)
        promotion.inspect_server(self.root, revision, identity['version'], identity)
        with self.assertRaisesRegex(RuntimeError, 'historical'):
            desktop.inspect_archive(self.root, 'linux-x64', revision, identity, expected_project_notice=MIT)
        with self.assertRaisesRegex(RuntimeError, 'historical'):
            promotion.inspect_server(self.root, revision, identity['version'], identity, expected_project_notice=MIT)

    def test_legacy_mode_keeps_file_inventory_checks(self):
        identity = release(); desktop_fixture(self.root, SHIPPED, identity)
        metadata_path = self.root / desktop.names('linux-x64')['metadata']
        metadata = json.loads(metadata_path.read_text()); metadata['files']['Bomber.Game.dll'] = '0' * 64
        metadata_path.write_text(json.dumps(metadata))
        with self.assertRaisesRegex(RuntimeError, 'inventory mismatch'):
            desktop.inspect_archive(self.root, 'linux-x64', SHIPPED, identity, expected_project_notice=MIT)
        server_fixture(self.root, SHIPPED, identity)
        path = self.root / 'server-build-metadata.json'; metadata = json.loads(path.read_text())
        metadata['files']['Bomber.Server.dll'] = '0' * 64; path.write_text(json.dumps(metadata))
        with self.assertRaisesRegex(RuntimeError, 'file checksum'):
            promotion.inspect_server(self.root, SHIPPED, identity['version'], identity, expected_project_notice=MIT)

    def test_mac_baseline_inspection_keeps_its_existing_controller_and_identity_contract(self):
        identity = release('0.45.1', '57'); archive = self.root / promotion.ARCHIVE
        with zipfile.ZipFile(archive, 'w') as bundle:
            bundle.writestr('Blast Circuit.app/Contents/Info.plist', plistlib.dumps(dict(
                CFBundleIdentifier='io.github.mcmike0399.blastcircuit', CFBundleShortVersionString='0.45.1', CFBundleVersion='57')))
            for name in ('libSDL3.dylib', 'libusb-1.0.dylib', 'SDL-LICENSE.txt', 'libusb-LICENSE.txt'):
                bundle.writestr('Blast Circuit.app/Contents/Frameworks/Controllers/' + name, b'fixture')
            bundle.writestr('Blast Circuit.app/Contents/Resources/Notices/' + MIT, b'published project notice')
        (self.root/(archive.name+'.sha256')).write_text(desktop.digest(archive)+'  '+archive.name+'\n')
        (self.root/'build-metadata.json').write_text(json.dumps(dict(revision=LATEST_MIT, version='0.45.1', release=identity,
            features=dict(switch2UsbBackend=True))))
        self.assertEqual(archive,promotion.inspect_package(self.root,LATEST_MIT,'0.45.1',identity))

    def test_real_baseline_preparation_authenticates_before_legacy_inspection(self):
        origin = self.root / 'origin'; origin.mkdir(); identity = release()
        desktop_fixture(origin, SHIPPED, identity); server_fixture(origin, SHIPPED, identity)
        required = ['Blast-Circuit-linux-x64.zip', 'Blast-Circuit-linux-x64.zip.sha256', 'Blast-Circuit-linux-x64-metadata.json',
                    promotion.SERVER_ARCHIVE, promotion.SERVER_ARCHIVE + '.sha256', 'server-build-metadata.json']
        delivery = dict(revision=SHIPPED, release=identity, assets={name:dict(sha256=desktop.digest(origin/name),size=(origin/name).stat().st_size)
                         for name in required if name != promotion.SERVER_ARCHIVE + '.sha256'})
        (origin / 'Blast-Circuit-release.json').write_text(json.dumps(delivery)); (origin / 'Blast-Circuit-release.json.sig').write_text('fixture signature')
        assets = [dict(name=p.name,browser_download_url=p.name) for p in origin.iterdir() if p.name in (*required, 'Blast-Circuit-release.json','Blast-Circuit-release.json.sig')]
        description = dict(draft=False, target_commitish=SHIPPED, assets=assets)
        authenticated = []
        def signature(path): authenticated.append(path.name)
        def inspect(*args, **kwargs):
            self.assertEqual(['Blast-Circuit-release.json'], authenticated)
            return desktop_inspect(*args, **kwargs)
        desktop_inspect = desktop.inspect_archive
        with patch.object(prepare.public,'api',return_value=description), patch.object(prepare.public,'download',side_effect=lambda name,path:shutil.copyfile(origin/name,path)), \
             patch.object(prepare.public,'verify_signature',side_effect=signature), patch.object(desktop,'inspect_archive',side_effect=inspect):
            client = prepare.baseline(self.root / 'success')
            self.assertEqual({'Bomber.Client.dll','Bomber.Game.dll','Burbuja.Realtime.Abstractions.dll'}, {p.name for p in client.iterdir()})
        with patch.object(prepare.public,'api',return_value=description), patch.object(prepare.public,'download',side_effect=lambda name,path:shutil.copyfile(origin/name,path)), \
             patch.object(prepare.public,'verify_signature',side_effect=RuntimeError('bad signature')), patch.object(desktop,'inspect_archive') as inspect:
            with self.assertRaisesRegex(RuntimeError,'bad signature'):prepare.baseline(self.root/'bad-signature')
            inspect.assert_not_called()
        for label, changed, error in (
                ('wrong-source', delivery | dict(revision='d' * 40), 'Signed baseline source'),
                ('wrong-hash', delivery | dict(assets=delivery['assets'] | {
                    'Blast-Circuit-linux-x64.zip': dict(sha256='0' * 64, size=(origin/'Blast-Circuit-linux-x64.zip').stat().st_size)}), 'Signed baseline asset')):
            with self.subTest(label=label):
                (origin/'Blast-Circuit-release.json').write_text(json.dumps(changed))
                with patch.object(prepare.public,'api',return_value=description), patch.object(prepare.public,'download',side_effect=lambda name,path:shutil.copyfile(origin/name,path)), \
                     patch.object(prepare.public,'verify_signature'), patch.object(desktop,'inspect_archive') as inspect:
                    with self.assertRaisesRegex(RuntimeError,error):prepare.baseline(self.root/label)
                    inspect.assert_not_called()


if __name__ == '__main__': unittest.main()

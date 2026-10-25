"""Server delivery boundaries using miniature runtime fixtures, never product builds."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile


def load(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


packager = load('package_server')
promotion = load('promote_release')
SHA = 'a' * 40


class ServerPackageTests(unittest.TestCase):
    def setUp(self):
        release = {'version': '0.15.0', 'build': '10', 'protocolRevision': 4, 'admissionToken': '4/0.15.0/10'}
        for module in (packager, promotion):
            fixture = patch.object(module, 'check_exports', return_value=release)
            fixture.start()
            self.addCleanup(fixture.stop)

    def test_cluster_plugins_are_runtime_only_and_frozen_before_fleet_testing(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / 'plugins'
            destination = root / 'staged'
            destination.mkdir()
            inventory = {}
            for name in packager.REQUIRED:
                (destination / name).write_bytes(b'fixture')
                inventory[name] = hashlib.sha256(b'fixture').hexdigest()
            for plugin in packager.PLUGINS:
                origin = source / plugin
                origin.mkdir(parents=True)
                (origin / 'plugin.json').write_text(json.dumps({'id': plugin, 'assembly': plugin + '.dll'}))
                (origin / (plugin + '.dll')).write_bytes(b'plugin-runtime')
                (origin / 'host-secret.json').write_text('must not ship')
                (origin / 'private.cs').write_text('must not ship')
            inventory.update(packager.stage_plugins(source, destination))
            self.assertFalse(any(name.endswith(('host-secret.json', '.cs')) for name in inventory))
            packager.package(destination, root, SHA, '0.15.0', inventory)
            promotion.inspect_server(root, SHA, '0.15.0')
            (destination / 'plugins/Blast.Cluster/Blast.Cluster.dll').write_bytes(b'mutated after fleet test')
            with self.assertRaisesRegex(RuntimeError, 'changed during smoke'):
                packager.package(destination, root, SHA, '0.15.0', inventory)

    def test_archive_rejects_config_even_if_added_to_inventory(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            staged = root / 'staged'
            staged.mkdir()
            inventory = {}
            for name in (*packager.REQUIRED, 'appsettings.Production.json'):
                (staged / name).write_bytes(b'fixture')
                inventory[name] = hashlib.sha256(b'fixture').hexdigest()
            with self.assertRaisesRegex(RuntimeError, 'allowlist'):
                packager.package(staged, root, SHA, '0.15.0', inventory)

    def test_modified_runtime_file_after_smoke_prevents_archive(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            staged = root / 'staged'
            staged.mkdir()
            inventory = {}
            for name in packager.REQUIRED:
                (staged / name).write_bytes(b'fixture')
                inventory[name] = hashlib.sha256(b'fixture').hexdigest()
            (staged / 'Bomber.Server.dll').write_bytes(b'changed after smoke')
            with self.assertRaisesRegex(RuntimeError, 'changed during smoke'):
                packager.package(staged, root, SHA, '0.15.0', inventory)

    def test_staged_runtime_excludes_private_source_symbols_and_host_settings(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / 'published'
            source.mkdir()
            for name in (*packager.REQUIRED, 'Dependency.dll', 'private.cs', 'persona.md',
                         'Dependency.pdb', 'appsettings.json', 'appsettings.Production.json', 'secret.pem'):
                (source / name).write_text('fixture')
            (source / 'linked.dll').symlink_to(source / 'Dependency.dll')
            staged = root / 'staged'
            inventory = packager.stage(source, staged)
            files = {path.relative_to(staged).as_posix() for path in staged.rglob('*') if path.is_file()}
            self.assertTrue(set(packager.REQUIRED).issubset(files))
            self.assertTrue({'Dependency.dll', 'README.txt'}.issubset(files))
            for name in ('private.cs', 'persona.md', 'Dependency.pdb', 'appsettings.Production.json', 'secret.pem', 'linked.dll'):
                self.assertNotIn(name, files)
            public_settings = (packager.ROOT / 'src/Bomber.Server/appsettings.json').read_text()
            self.assertEqual(public_settings, (staged / 'appsettings.json').read_text())
            config = json.loads(public_settings)
            self.assertEqual('worker', config['Kernel']['Role'])
            self.assertTrue(config['Realtime']['Enabled'])
            self.assertTrue(config['Realtime']['Guests']['Enabled'])
            (staged / 'runtime-auth-tokens.json').write_text('must never ship')
            packager.package(staged, root, SHA, '0.15.0', inventory)
            with zipfile.ZipFile(root / packager.ARCHIVE) as output:
                self.assertFalse(any('runtime-auth-tokens' in name for name in output.namelist()))
            self.assertEqual(root / packager.ARCHIVE, promotion.inspect_server(root, SHA, '0.15.0'))
            with self.assertRaisesRegex(RuntimeError, 'revision/version'):
                promotion.inspect_server(root, 'b' * 40, '0.15.0')

    def test_missing_entrypoint_fails_before_archive(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / 'published'
            source.mkdir()
            with self.assertRaisesRegex(RuntimeError, 'incomplete'):
                packager.stage(source, root / 'staged')

    def test_server_file_manifest_detects_tampering_even_with_new_archive_checksum(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            staged = root / 'staged'
            staged.mkdir()
            for name in packager.REQUIRED:
                (staged / name).write_text('original')
            inventory = {name: hashlib.sha256(b'original').hexdigest() for name in packager.REQUIRED}
            packager.package(staged, root, SHA, '0.15.0', inventory)
            archive = root / packager.ARCHIVE
            with zipfile.ZipFile(archive, 'w') as output:
                for name in packager.REQUIRED:
                    output.writestr('Blast-Circuit-server-dotnet10/' + name, 'tampered')
            digest = hashlib.sha256(archive.read_bytes()).hexdigest()
            (root / (packager.ARCHIVE + '.sha256')).write_text(digest + '  ' + packager.ARCHIVE + '\n')
            with self.assertRaisesRegex(RuntimeError, 'file checksum'):
                promotion.inspect_server(root, SHA, '0.15.0')

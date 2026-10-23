import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

SPEC = importlib.util.spec_from_file_location('party_server', Path(__file__).resolve().parents[1] / 'prepare-party-server.py')
party = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(party)


class PartyContextChecks(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        original = party.ROOT
        self.addCleanup(setattr, party, 'ROOT', original)
        party.ROOT = self.root
        (self.root / 'artifacts').mkdir()
        (self.root / 'deploy/party').mkdir(parents=True)
        for name in ('Dockerfile', 'compose.yaml', '.dockerignore'):
            (self.root / 'deploy/party' / name).write_text(name)
        self.archive = self.root / 'artifacts/Blast-Circuit-server-dotnet10.zip'
        self.output = self.root / 'context'
        self.make_archive({'Bomber.Server.dll': b'runtime'})

    def make_archive(self, files, extra=None):
        metadata = {'version': '0.19.1', 'revision': 'a' * 40,
                    'files': {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}}
        (self.root / 'artifacts/server-build-metadata.json').write_text(json.dumps(metadata))
        with zipfile.ZipFile(self.archive, 'w') as output:
            for name, data in files.items():
                output.writestr('Blast-Circuit-server-dotnet10/' + name, data)
            if extra:
                output.writestr(extra, b'private configuration')
        self.archive.with_suffix('.zip.sha256').write_text(hashlib.sha256(self.archive.read_bytes()).hexdigest())

    def test_copies_only_verified_runtime(self):
        (self.root / 'artifacts/appsettings.Local.json').write_text('not for deployment')
        party.prepare(self.output)
        self.assertEqual((self.output / 'server/Bomber.Server.dll').read_bytes(), b'runtime')
        self.assertFalse((self.output / 'server/appsettings.Local.json').exists())
        with self.assertRaisesRegex(RuntimeError, 'existing deliveries'):
            party.prepare(self.output)

    def test_rejects_uninventoried_configuration(self):
        self.make_archive({'Bomber.Server.dll': b'runtime'}, 'Blast-Circuit-server-dotnet10/appsettings.Local.json')
        with self.assertRaisesRegex(RuntimeError, 'inventory'):
            party.prepare(self.output)
        self.assertFalse(self.output.exists())

    def test_rejects_tampered_archive(self):
        self.archive.write_bytes(self.archive.read_bytes() + b'tampered')
        with self.assertRaisesRegex(RuntimeError, 'checksum'):
            party.prepare(self.output)
        self.assertFalse(self.output.exists())

    def test_rejects_path_traversal_even_with_matching_inventory(self):
        self.make_archive({'../escape': b'bad'})
        with self.assertRaisesRegex(RuntimeError, 'path'):
            party.prepare(self.output)
        self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main()

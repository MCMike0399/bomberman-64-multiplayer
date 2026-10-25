"""Real fixture keys exercise release signatures; never use or publish the production key."""
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import plistlib
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import promote_release

SPEC = importlib.util.spec_from_file_location('sign_release', Path(__file__).parents[1] / 'updates/sign_release.py')
signer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(signer)


class SigningTests(unittest.TestCase):
    def test_real_signed_envelope_binds_both_archives_and_tampering_fails(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            key = root / 'fixture.pem'
            subprocess.run(['openssl', 'genpkey', '-algorithm', 'EC', '-pkeyopt', 'ec_paramgen_curve:P-256', '-out', str(key)], check=True, capture_output=True)
            public = subprocess.check_output(['openssl', 'pkey', '-in', str(key), '-pubout', '-outform', 'DER'])
            trusted = root / 'src/Bomber.Client/Updates/UpdatePublicKey.txt'
            trusted.parent.mkdir(parents=True)
            trusted.write_text(base64.b64encode(public).decode())
            archive = root / promote_release.ARCHIVE
            with zipfile.ZipFile(archive, 'w') as bundle:
                bundle.writestr('Blast Circuit.app/Contents/Info.plist', plistlib.dumps({
                    'CFBundleIdentifier': 'io.github.mcmike0399.blastcircuit', 'CFBundleShortVersionString': '1.2.3'}))
            server = root / promote_release.SERVER_ARCHIVE
            server.write_bytes(b'fixture server payload')
            metadata = {'revision': 'a' * 40, 'version': '1.2.3', 'release': {'version': '1.2.3', 'build': '42'}}
            for name in ('build-metadata.json', 'server-build-metadata.json'):
                (root / name).write_text(json.dumps(metadata))
            with patch.object(signer, 'ROOT', root):
                signer.sign(archive, key, root, server)
            envelope = root / 'Blast-Circuit-release.json'
            signed = json.loads(envelope.read_text())
            self.assertEqual(hashlib.sha256(server.read_bytes()).hexdigest(), signed['assets'][server.name]['sha256'])
            self.assertEqual(5, len(signed['assets']))
            signature = root / 'signature.der'
            signature.write_bytes(base64.b64decode(envelope.with_suffix('.json.sig').read_text()))
            public_path = root / 'public.der'
            public_path.write_bytes(public)
            command = ['openssl', 'dgst', '-sha256', '-verify', str(public_path), '-keyform', 'DER', '-signature', str(signature), str(envelope)]
            self.assertEqual(0, subprocess.run(command, capture_output=True).returncode)
            envelope.write_text(envelope.read_text().replace('1.2.3', '1.2.4'))
            self.assertNotEqual(0, subprocess.run(command, capture_output=True).returncode)
            trusted.write_text(base64.b64encode(b'wrong public key').decode())
            with patch.object(signer, 'ROOT', root), self.assertRaisesRegex(RuntimeError, 'does not match'):
                signer.sign(archive, key, root, server)

    def test_uploaded_byte_mismatch_blocks_publication(self):
        with tempfile.TemporaryDirectory() as temporary:
            asset = Path(temporary) / 'fixture.zip'
            asset.write_bytes(b'tested bytes')
            def download(*arguments):
                destination = Path(arguments[arguments.index('--dir') + 1])
                (destination / asset.name).write_bytes(b'wrong bytes')
                return ''
            with patch.object(promote_release, 'gh', side_effect=download):
                with self.assertRaisesRegex(RuntimeError, 'Uploaded release bytes differ'):
                    promote_release.verify_uploaded('example/game', '1.2.3', [str(asset)])

    def test_published_version_and_build_cannot_regress(self):
        published = {'tag_name': 'v1.2.3', 'draft': False, 'prerelease': False}
        for prerelease in (False, True):
            # A bad stable release may be marked prerelease to stop new installs;
            # users who already installed it still cannot accept a downgrade.
            prior = dict(published, prerelease=prerelease)
            with self.subTest(prerelease=prerelease), patch.object(promote_release, 'gh', side_effect=['[]', json.dumps([[prior]])]):
                with self.assertRaisesRegex(RuntimeError, 'refuse downgrades'):
                    promote_release.inspect_existing_release('example/game', '1.2.2', 'a' * 40, True)
        with patch.object(promote_release, 'gh', side_effect=['[]', json.dumps([[published]]), '{"release":{"build":"42"}}']), patch.object(promote_release, 'check_exports', return_value={'build': '42'}):
            with self.assertRaisesRegex(RuntimeError, 'build must increase'):
                promote_release.inspect_existing_release('example/game', '1.2.4', 'a' * 40, True)


if __name__ == '__main__':
    unittest.main()

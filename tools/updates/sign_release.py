#!/usr/bin/env python3
"""Sign a versioned update manifest for the verified Mac release archive."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import plistlib
import re
import subprocess
import tempfile
import zipfile
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import desktop_packages
import download_formats
import crossplay
import desktop_validation
import release_gpu_policy
import gpu_release_gates


def sign_bytes(path, key, trusted):
    signature = subprocess.check_output(['openssl', 'dgst', '-sha256', '-sign', str(key), str(path)])
    # Verify every generated signature before anything can be deployed or published.
    with tempfile.TemporaryDirectory(prefix='blast-sign-check-') as temporary:
        public = Path(temporary) / 'public.der'
        public.write_bytes(base64.b64decode(trusted, validate=True))
        signed = Path(temporary) / 'signature.der'
        signed.write_bytes(signature)
        subprocess.run(['openssl', 'dgst', '-sha256', '-verify', str(public), '-keyform', 'DER',
                        '-signature', str(signed), str(path)], check=True, capture_output=True)
    path.with_suffix('.json.sig').write_text(base64.b64encode(signature).decode() + '\n')


def sign(archive, key, output, server=None):
    trusted = (ROOT / 'src/Bomber.Client/Updates/UpdatePublicKey.txt').read_text().strip()
    public = subprocess.check_output(['openssl', 'pkey', '-in', str(key), '-pubout', '-outform', 'DER'])
    if base64.b64encode(public).decode() != trusted:
        raise RuntimeError('Signing key does not match the public key shipped in the client.')
    with zipfile.ZipFile(archive) as zipped:
        info = plistlib.loads(zipped.read('Blast Circuit.app/Contents/Info.plist'))
    version = info['CFBundleShortVersionString']
    if info['CFBundleIdentifier'] != 'io.github.mcmike0399.blastcircuit' or not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', version):
        raise RuntimeError('Incorrect app identity/version.')
    manifest = {'schemaVersion': 1, 'version': version, 'sha256': hashlib.sha256(archive.read_bytes()).hexdigest(), 'size': archive.stat().st_size}
    output.mkdir(parents=True, exist_ok=True)
    path = output / 'Blast-Circuit-update.json'
    path.write_text(json.dumps(manifest, separators=(',', ':')) + '\n')
    sign_bytes(path, key, trusted)
    if server is not None:
        metadata = json.loads((output / 'build-metadata.json').read_text())
        server_metadata = json.loads((output / 'server-build-metadata.json').read_text())
        if (metadata.get('release') != server_metadata.get('release') or
                metadata.get('revision') != server_metadata.get('revision') or
                metadata.get('version') != version or server_metadata.get('version') != version):
            raise RuntimeError('Client/server signing identities differ')
        assets = [archive, server, output / 'build-metadata.json', output / 'server-build-metadata.json', path]
        # Preserve verification of historical Mac-only releases and the existing updater format.
        if desktop_packages.present(output):
            targets = desktop_packages.present_targets(output)
            policy=release_gpu_policy.read(output)
            desktop_packages.inspect(output, metadata['revision'], metadata['release'], targets=targets,
                                     include_graphics=policy is None or policy['requiresGpu'])
            if policy:
                assets.append(output/release_gpu_policy.RECEIPT)
                if policy['requiresGpu']:
                    gpu_release_gates.inspect_all(output,metadata['revision'],fingerprint=policy['renderFingerprint'],targets=targets)
                    assets.append(output/gpu_release_gates.CLEANUP_RECEIPT)
            assets += [output / name for target in targets for name in release_gpu_policy.asset_names(target,policy)]
            for target in targets:
                download_formats.inspect(output, target, metadata['revision'], metadata['release'])
                update_archive = output / (desktop_packages.names(target)['archive'] if target == 'windows-x64' else download_formats.FILES[target])
                platform_manifest = output / ('Blast-Circuit-' + target + '-update.json')
                platform_manifest.write_text(json.dumps({'schemaVersion': 1, 'version': version,
                    'sha256': hashlib.sha256(update_archive.read_bytes()).hexdigest(), 'size': update_archive.stat().st_size}, separators=(',', ':')) + '\n')
                sign_bytes(platform_manifest, key, trusted)
                assets += [platform_manifest]
        if 'macos-arm64' in download_formats.present(output):
            download_formats.inspect(output, 'macos-arm64', metadata['revision'], metadata['release'])
            assets += [output / name for name in download_formats.names('macos-arm64')]
        if (output / crossplay.RECEIPT).exists():
            crossplay.inspect_summary(json.loads((output / crossplay.RECEIPT).read_text()), metadata['revision'], metadata['release'], output)
            assets += [output / crossplay.RECEIPT, output / desktop_validation.MAC_RECEIPT]
        inventory = {asset.name: {'sha256': hashlib.sha256(asset.read_bytes()).hexdigest(), 'size': asset.stat().st_size} for asset in assets}
        delivery = output / 'Blast-Circuit-release.json'
        delivery.write_text(json.dumps({'schemaVersion': 1, 'revision': metadata['revision'],
                                       'release': metadata['release'], 'assets': inventory}, sort_keys=True, separators=(',', ':')) + '\n')
        sign_bytes(delivery, key, trusted)
    print('Signed update manifest for v' + version)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--key', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--server', type=Path, help='Also sign client/server archives and matching metadata as one release')
    args = parser.parse_args()
    sign(args.archive, args.key, args.output, args.server)

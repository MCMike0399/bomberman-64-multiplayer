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
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def sign(archive, key, output):
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
    signature = subprocess.check_output(['openssl', 'dgst', '-sha256', '-sign', str(key), str(path)])
    path.with_suffix('.json.sig').write_text(base64.b64encode(signature).decode() + '\n')
    print('Signed update manifest for v' + version)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--key', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    sign(args.archive, args.key, args.output)

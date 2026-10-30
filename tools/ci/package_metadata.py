#!/usr/bin/env python3
"""Write the exact source/bundle identity alongside the distributable artifact."""
import json
import os
from pathlib import Path
import plistlib
import re
from release_identity import check_exports
import render_fingerprint


def main():
    release = check_exports()
    revision = os.environ.get('GITHUB_SHA', '')
    if not re.fullmatch(r'[0-9a-f]{40}', revision):
        raise SystemExit('GITHUB_SHA must identify the source commit')
    app = Path('artifacts/exports/Blast Circuit.app')
    required = ('libSDL3.dylib', 'libusb-1.0.dylib', 'SDL-LICENSE.txt', 'libusb-LICENSE.txt')
    native = app / 'Contents/Frameworks/Controllers'
    if any(not (native / name).is_file() or (native / name).stat().st_size == 0 for name in required):
        raise SystemExit('Refusing package metadata: full controller backend/libraries/licenses are required')
    with (app / 'Contents/Info.plist').open('rb') as stream:
        plist = plistlib.load(stream)
    if plist['CFBundleShortVersionString'] != release['version'] or plist['CFBundleVersion'] != release['build']:
        raise SystemExit('Built app differs from canonical release identity')
    fingerprint=render_fingerprint.calculate()
    metadata = {'schemaVersion': 2, 'revision': revision, 'release': release,
                'renderFingerprint':fingerprint['sha256'],
                'version': plist['CFBundleShortVersionString'],
                'bundleIdentifier': plist['CFBundleIdentifier'], 'architecture': 'arm64',
                'signature': 'ad-hoc', 'graphicalValidation': 'separate-optional-job',
                'features': {'switch2UsbBackend': True, 'controllerBackendFiles': list(required)}}
    Path('artifacts/build-metadata.json').write_text(json.dumps(metadata, indent=2) + '\n')
    Path('artifacts/render-fingerprint.json').write_text(json.dumps(fingerprint,indent=2)+'\n')


if __name__ == '__main__':
    main()

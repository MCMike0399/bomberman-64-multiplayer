#!/usr/bin/env python3
"""Install the hash-pinned official Godot desktop WebRTC extension (MIT/MPL-2.0)."""
import hashlib
from pathlib import Path
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
VERSION = '1.2.2-stable'
SHA256 = '98e9446921740d995bd9ca1be48798dc3c2ceed51e044a25ce18b3cff11f56e5'
URL = f'https://github.com/godotengine/webrtc-native/releases/download/{VERSION}/godot-extension-webrtc_native.zip'


def install():
    archive = ROOT / 'artifacts/dependencies/webrtc-native.zip'
    archive.parent.mkdir(parents=True, exist_ok=True)
    if not archive.exists() or hashlib.sha256(archive.read_bytes()).hexdigest() != SHA256:
        with urllib.request.urlopen(URL, timeout=60) as response:
            data = response.read(64 * 1024 * 1024)
        if hashlib.sha256(data).hexdigest() != SHA256:
            raise RuntimeError('WebRTC archive checksum mismatch')
        archive.write_bytes(data)
    with zipfile.ZipFile(archive) as bundle:
        for entry in bundle.infolist():
            target = (ROOT / entry.filename).resolve()
            if not target.is_relative_to(ROOT.resolve() / 'addons/webrtc_native') and entry.filename != 'addons/':
                raise RuntimeError('Unexpected WebRTC archive entry')
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                data = bundle.read(entry)
                if not target.exists() or target.read_bytes() != data:
                    target.write_bytes(data)
    print(f'WebRTC {VERSION} verified: {SHA256}')


if __name__ == '__main__':
    install()

#!/usr/bin/env python3
"""Create native player downloads from the exact inventoried client payload."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import desktop_packages as desktop
import download_formats as downloads


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=downloads.FILES, required=True)
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    args = parser.parse_args()
    directory = args.directory.resolve()
    target = args.target
    name = downloads.names(target)[0]
    if target == 'macos-arm64':
        if platform.system() != 'Darwin':
            raise RuntimeError('DMG packaging requires macOS')
        metadata = json.loads((directory / 'build-metadata.json').read_text())
        with tempfile.TemporaryDirectory(prefix='blast-dmg-') as temporary:
            staging = Path(temporary) / 'Blast Circuit'
            staging.mkdir()
            subprocess.run(['ditto', '-x', '-k', str(directory / 'Blast-Circuit-macos-arm64.zip'), str(staging)], check=True)
            (staging / 'Applications').symlink_to('/Applications')
            subprocess.run(['hdiutil', 'create', '-volname', 'Blast Circuit', '-srcfolder', str(staging),
                            '-format', 'UDZO', '-ov', str(directory / name)], check=True, timeout=300)
        subprocess.run(['hdiutil', 'verify', str(directory / name)], check=True, timeout=120)
        with tempfile.TemporaryDirectory(prefix='blast-dmg-inspect-') as temporary:
            mount = Path(temporary) / 'volume'
            subprocess.run(['hdiutil', 'attach', '-readonly', '-nobrowse', '-noautoopen', '-mountpoint',
                            str(mount), str(directory / name)], check=True, timeout=120)
            try:
                with zipfile.ZipFile(directory / 'Blast-Circuit-macos-arm64.zip') as archive:
                    for entry in archive.infolist():
                        if entry.is_dir() or not entry.filename.startswith('Blast Circuit.app/'):
                            continue
                        path = mount / entry.filename
                        if hashlib.sha256(path.read_bytes()).digest() != hashlib.sha256(archive.read(entry)).digest():
                            raise RuntimeError('DMG payload differs from the verified app archive: ' + entry.filename)
            finally:
                subprocess.run(['hdiutil', 'detach', str(mount)], check=True, timeout=120)
    elif target == 'windows-x64':
        if platform.system() != 'Windows':
            raise RuntimeError('EXE installer packaging requires Windows')
        item = desktop.names(target)
        metadata = json.loads((directory / item['metadata']).read_text())
        desktop.inspect_archive(directory, target, metadata['revision'], metadata['release'])
        # The same safe extraction path is used by the native regression runner.
        spec = importlib.util.spec_from_file_location('verify_desktop', ROOT / 'tools/packaging/verify-desktop.py')
        verifier = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(verifier)
        with tempfile.TemporaryDirectory(prefix='blast-inno-') as temporary:
            verifier.extract(directory / item['archive'], Path(temporary))
            compiler = shutil.which('ISCC') or str(Path(os.environ.get('ProgramFiles(x86)', 'C:/Program Files (x86)')) / 'Inno Setup 6/ISCC.exe')
            subprocess.run([compiler, '/DGameDirectory=' + str(Path(temporary) / item['root']),
                            '/DOutputDirectory=' + str(directory), '/DGameVersion=' + metadata['release']['version'],
                            '/DGameBuild=' + str(metadata['release']['build']),
                            str(ROOT / 'tools/packaging/windows-installer.iss')], check=True, timeout=300)
    else:
        raise RuntimeError('Linux AppImage packaging is added by the Linux platform PR')
    downloads.record(directory, target, metadata['revision'], metadata['release'], 'Blast-Circuit-' + target + '.zip')
    downloads.inspect(directory, target, metadata['revision'], metadata['release'])
    print('Packaged native download: ' + name)


if __name__ == '__main__':
    main()

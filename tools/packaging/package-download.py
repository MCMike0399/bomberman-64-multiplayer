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
import time
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import desktop_packages as desktop
import download_formats as downloads



def create_dmg(staging, destination):
    # Hosted DiskImages can transiently report EBUSY. Never overwrite or reuse
    # the partial image; every bounded attempt owns a fresh temporary output.
    with tempfile.TemporaryDirectory(prefix='blast-dmg-output-',dir=destination.parent) as temporary:
        for attempt in range(2):
            candidate=Path(temporary)/f'attempt-{attempt}.dmg'
            command=['hdiutil','create','-volname','Blast Circuit','-srcfolder',str(staging),
                     '-format','UDZO',str(candidate)]
            result=subprocess.run(command,capture_output=True,text=True,timeout=300)
            if result.stdout:print(result.stdout,end='')
            if result.stderr:print(result.stderr,end='',file=sys.stderr)
            if result.returncode==0:
                candidate.replace(destination)
                return
            if attempt==0 and 'Resource busy' in result.stderr:
                print('DMG_CREATE_RETRY_RESOURCE_BUSY: one fresh-output retry')
                time.sleep(2)
                continue
            raise subprocess.CalledProcessError(result.returncode,command,result.stdout,result.stderr)


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
            create_dmg(staging,directory/name)
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
                        actual = os.readlink(path).encode() if path.is_symlink() else path.read_bytes()
                        if hashlib.sha256(actual).digest() != hashlib.sha256(archive.read(entry)).digest():
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
        if platform.system() != 'Linux' or platform.machine().lower() != 'x86_64':
            raise RuntimeError('AppImage packaging requires Linux x64')
        item = desktop.names(target)
        metadata = json.loads((directory / item['metadata']).read_text())
        desktop.inspect_archive(directory, target, metadata['revision'], metadata['release'])
        spec = importlib.util.spec_from_file_location('verify_desktop', ROOT / 'tools/packaging/verify-desktop.py')
        verifier = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(verifier)
        with tempfile.TemporaryDirectory(prefix='blast-appimage-') as temporary:
            temporary = Path(temporary)
            verifier.extract(directory / item['archive'], temporary)
            appdir = temporary / 'BlastCircuit.AppDir'
            payload = appdir / 'usr/lib/blast-circuit'
            payload.parent.mkdir(parents=True)
            shutil.move(str(temporary / item['root']), payload)
            (appdir / 'AppRun').write_text('#!/bin/sh\nset -eu\nAPP_ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)\nexec "$APP_ROOT/usr/lib/blast-circuit/BlastCircuit.x86_64" "$@"\n')
            (appdir / 'AppRun').chmod(0o755)
            (appdir / 'blast-circuit.desktop').write_text('[Desktop Entry]\nType=Application\nName=Blast Circuit\nExec=BlastCircuit\nIcon=blast-circuit\nCategories=Game;ArcadeGame;\nStartupWMClass=Blast Circuit\nTerminal=false\n')
            shutil.copy2(ROOT / 'icon.svg', appdir / 'blast-circuit.svg')
            runtime_notices = appdir / 'usr/share/doc/blast-circuit'
            runtime_notices.mkdir(parents=True)
            for notice in (ROOT / 'docs/licenses').glob('AppImage-*.txt'):
                shutil.copy2(notice, runtime_notices / notice.name)
            (runtime_notices / 'AppImage-RUNTIME-SOURCE.txt').write_text('Unmodified AppImage type-2 runtime, SHA-256 617aa77314d57c8b8e4fae9c3f0b35af7b30d5529cb69e98f64f92510ed98a46.\nSource and rebuild instructions: https://github.com/AppImage/type2-runtime/tree/fedfad51acc2860b2916a1eef32ba4a500ff31ef\nUse appimagetool --runtime-file to replace or relink the runtime.\n')
            # Use a versioned build tool, with an independently checked asset digest.
            # The separately pinned shipped runtime remains unchanged.
            pinned = (
                ('https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage',
                 'ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0', 'appimagetool.AppImage'),
                ('https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-x86_64',
                 '617aa77314d57c8b8e4fae9c3f0b35af7b30d5529cb69e98f64f92510ed98a46', 'runtime-x86_64'))
            for url, digest, filename in pinned:
                path = temporary / filename
                urllib.request.urlretrieve(url, path)
                if desktop.digest(path) != digest:
                    raise RuntimeError('AppImage build tool/runtime checksum mismatch: ' + filename)
                path.chmod(0o755)
            environment = dict(os.environ, ARCH='x86_64', APPIMAGE_EXTRACT_AND_RUN='1')
            subprocess.run([str(temporary / 'appimagetool.AppImage'), '--runtime-file', str(temporary / 'runtime-x86_64'),
                            str(appdir), str(directory / name)], env=environment, check=True, timeout=300)
        (directory / name).chmod(0o755)
    downloads.record(directory, target, metadata['revision'], metadata['release'], 'Blast-Circuit-' + target + '.zip')
    downloads.inspect(directory, target, metadata['revision'], metadata['release'])
    print('Packaged native download: ' + name)


if __name__ == '__main__':
    main()

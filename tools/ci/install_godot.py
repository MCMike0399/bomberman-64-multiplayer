#!/usr/bin/env python3
"""Install checksum-pinned official Godot .NET editors for CI."""
import argparse
import hashlib
import os
from pathlib import Path
import platform
import stat
import tempfile
import urllib.request
import zipfile

VERSION = '4.7.2'
HASHES = {
    'mono_win64.zip': '79229fd112b0c9cbeab82363a4ef7be18ea70f1caf86bf912789335b136fbe7e01db0053a33461438c5da1c680c17bbb10040bd09bedc51221cd4423d0367757',
    'mono_linux_x86_64.zip': '1855960b27ee3ef5e66e5e228cced69d55637b24334a7411162687dcd077d8f9f645348cdb8eae984bec8135d49ed855a1e3a16476786b8bce60774fd8402d13',
    'mono_macos.universal.zip': '0862c53d7158c7a67f745e2e46f90b68cf5343cbe8b95d6d4333c469e42ca104af9c121d1746a50e5d221a99d09d82ef7016495f8e0d09255842884ed0502795',
    'mono_export_templates.tpz': 'bb5c41d72370ed743660361f6228006f808ab04ca33abdc545d740b044f3fe057f32ae8cb7873a1bc86ddcd82ae683b9f6dfdfe4179852f2c0f1acde2ff6bd5a',
}


def extract(asset, destination):
    name = f'Godot_v{VERSION}-stable_{asset}'
    url = f'https://github.com/godotengine/godot/releases/download/{VERSION}-stable/{name}'
    with tempfile.TemporaryDirectory(prefix='blast-godot-') as temporary:
        archive = Path(temporary) / name
        urllib.request.urlretrieve(url, archive)
        with archive.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha512').hexdigest()
        if digest != HASHES[asset]:
            raise RuntimeError('Godot archive checksum mismatch')
        destination.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(archive) as source:
            for member in source.infolist():
                target = (destination / member.filename).resolve()
                if not target.is_relative_to(destination.resolve()):
                    raise RuntimeError('Unsafe archive entry')
                source.extract(member, destination)
                mode = member.external_attr >> 16
                if mode and not target.is_dir():
                    target.chmod(stat.S_IMODE(mode))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--destination', type=Path, required=True)
    parser.add_argument('--templates', action='store_true')
    args = parser.parse_args()
    system = platform.system()
    if system == 'Darwin':
        asset = 'mono_macos.universal.zip'
        executable = args.destination / 'Godot_mono.app/Contents/MacOS/Godot'
        templates = Path.home() / 'Library/Application Support/Godot/export_templates' / f'{VERSION}.stable.mono'
    elif system == 'Linux' and platform.machine() in ('x86_64', 'AMD64'):
        asset = 'mono_linux_x86_64.zip'
        executable = args.destination / f'Godot_v{VERSION}-stable_mono_linux_x86_64/Godot_v{VERSION}-stable_mono_linux.x86_64'
        templates = Path.home() / '.local/share/godot/export_templates' / f'{VERSION}.stable.mono'
    elif system == 'Windows' and platform.machine().lower() in ('amd64', 'x86_64'):
        asset = 'mono_win64.zip'
        executable = args.destination / f'Godot_v{VERSION}-stable_mono_win64/Godot_v{VERSION}-stable_mono_win64_console.exe'
        templates = Path(os.environ['APPDATA']) / 'Godot/export_templates' / f'{VERSION}.stable.mono'
    else:
        parser.error('Supported CI hosts: Linux/Windows x86_64 and macOS')
    extract(asset, args.destination)
    if not executable.is_file():
        raise RuntimeError('Godot archive layout changed')
    if args.templates:
        with tempfile.TemporaryDirectory(prefix='blast-templates-') as temporary:
            unpacked = Path(temporary)
            extract('mono_export_templates.tpz', unpacked)
            import shutil
            shutil.copytree(unpacked / 'templates', templates, dirs_exist_ok=True)
    if os.environ.get('GITHUB_ENV'):
        with open(os.environ['GITHUB_ENV'], 'a') as stream:
            stream.write(f'GODOT={executable.resolve()}\n')
    if os.environ.get('GITHUB_OUTPUT'):
        with open(os.environ['GITHUB_OUTPUT'], 'a') as stream:
            stream.write(f'godot={executable.resolve()}\n')
    print(executable.resolve())


if __name__ == '__main__':
    main()

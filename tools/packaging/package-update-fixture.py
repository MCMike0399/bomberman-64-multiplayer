#!/usr/bin/env python3
"""Build a deliberately newer test-only identity from unchanged game code, then restore source files."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import desktop_packages
import release_identity
import desktop_update_gates


def add_windows_files(output):
    """Require uninstall coverage for product files absent from the first install log."""
    item = desktop_packages.names('windows-x64')
    archive = output / item['archive']
    metadata_path = output / item['metadata']
    metadata = json.loads(metadata_path.read_text())
    additions = {'update-added.txt': b'Test-only file introduced by an update.\n',
                 'extra-runtime/update-added.txt': b'Test-only new product directory.\n'}
    owned = set(metadata['files']) | set(additions)
    additions['installed-files.txt'] = ('\n'.join(sorted(owned)) + '\n').encode()
    replacement = archive.with_suffix('.next.zip')
    with zipfile.ZipFile(archive) as source, zipfile.ZipFile(replacement, 'w', zipfile.ZIP_DEFLATED) as destination:
        for entry in source.infolist():
            relative = entry.filename.removeprefix(item['root'] + '/')
            destination.writestr(entry, additions.pop(relative) if relative in additions else source.read(entry))
        for relative, data in additions.items(): destination.writestr(item['root'] + '/' + relative, data)
    replacement.replace(archive)
    with zipfile.ZipFile(archive) as source:
        metadata['files'] = {entry.filename.removeprefix(item['root'] + '/'): hashlib.sha256(source.read(entry)).hexdigest()
                             for entry in source.infolist()}
    metadata_path.write_text(json.dumps(metadata, indent=2) + '\n')
    archive.with_suffix('.zip.sha256').write_text(desktop_packages.digest(archive) + '  ' + archive.name + '\n')
    desktop_packages.inspect_archive(output, 'windows-x64', metadata['revision'], metadata['release'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=desktop_packages.TARGETS, required=True)
    parser.add_argument('--godot', default=os.environ.get('GODOT','godot'))
    parser.add_argument('--unpack', action='store_true', help='Inspect and unpack the prebuilt fixture without any private build')
    args = parser.parse_args()
    if args.unpack:
        desktop_update_gates.unpack(ROOT / 'artifacts', args.target)
        return
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    if subprocess.run(['git', 'diff', '--quiet'], cwd=ROOT).returncode:
        raise RuntimeError('Update-fixture export requires a clean source checkout')
    source = release_identity.check_exports()
    parts = source['version'].split('.')
    newer = '.'.join([*parts[:2], str(int(parts[2]) + 1)])
    files = {ROOT / name: (ROOT / name).read_bytes() for name in ('Release.props', 'export_presets.cfg')}
    output = ROOT / 'artifacts' / 'update-fixture'
    try:
        props = ROOT / 'Release.props'
        text = props.read_text().replace('<BlastVersion>' + source['version'] + '</BlastVersion>', '<BlastVersion>' + newer + '</BlastVersion>')
        text = text.replace('<BlastBuild>' + source['build'] + '</BlastBuild>', '<BlastBuild>' + str(int(source['build']) + 1) + '</BlastBuild>')
        props.write_text(text)
        release_identity.check_exports(write=True)
        subprocess.run([sys.executable, str(ROOT / 'tools/packaging/package-desktop.py'), '--target', args.target,
                        '--godot', args.godot, '--output', str(output)], cwd=ROOT, check=True, timeout=1200)
        if args.target == 'windows-x64': add_windows_files(output)
        (output / 'fixture.json').write_text(json.dumps({'testOnly': True, 'baselineRelease': source,
            'fixtureRelease': release_identity.check_exports(), 'purpose': 'Actual signed updater transaction; never publish this synthetic identity.'}, indent=2) + '\n')
    finally:
        for path, content in files.items():
            path.write_bytes(content)
    # The synthetic identity must not leak into later source playtests.
    subprocess.run(['dotnet', 'build', 'Bomberman64Multiplayer.csproj', '--nologo', '-m:1'], cwd=ROOT, check=True, timeout=180)
    if release_identity.check_exports() != source:
        raise RuntimeError('Update fixture did not restore the candidate release identity')
    item = desktop_packages.names(args.target)
    archive = ROOT / 'artifacts' / (item['root'] + '-update-fixture.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as bundle:
        for name in (item['archive'], item['archive'] + '.sha256', item['metadata'], 'fixture.json'):
            bundle.write(output / name, name)
    archive.with_suffix('.zip.sha256').write_text(desktop_packages.digest(archive) + '  ' + archive.name + '\n')


if __name__ == '__main__':
    main()

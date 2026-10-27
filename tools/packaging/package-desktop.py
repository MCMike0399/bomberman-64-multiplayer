#!/usr/bin/env python3
"""Export a self-contained desktop client and inventory the exact distributable bytes."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import desktop_packages as desktop
from release_identity import check_exports


def run(command):
    result = subprocess.run([str(arg) for arg in command], cwd=ROOT, capture_output=True, text=True, timeout=600)
    print(result.stdout, end='')
    print(result.stderr, end='', file=sys.stderr)
    if result.returncode or re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception)', result.stdout + result.stderr):
        raise RuntimeError('Desktop build/export failed')


def notices(destination, target, published):
    destination.mkdir()
    sources = [ROOT / 'LICENSE', ROOT / 'src/Bomber.Game/Simulation/LICENSE',
               *sorted((ROOT / 'docs/licenses').glob('Godot-*.txt')),
               *sorted((ROOT / 'assets/fonts').glob('OFL-*.txt')),
               *sorted((ROOT / 'addons/webrtc_native').glob('LICENSE*'))]
    for source in sources:
        name = 'Blast-Circuit-MIT.txt' if source == ROOT / 'LICENSE' else 'Simulation-MIT.txt' if source.name == 'LICENSE' else source.name
        shutil.copy2(source, destination / name)
    config = next(published.rglob('Bomberman64Multiplayer.runtimeconfig.json'))
    version = json.loads(config.read_text())['runtimeOptions']['includedFrameworks'][0]['version']
    rid = 'win-x64' if target == 'windows-x64' else 'linux-x64'
    packages = Path(os.environ.get('NUGET_PACKAGES', str(Path.home() / '.nuget/packages')))
    runtime = packages / ('microsoft.netcore.app.runtime.' + rid) / version
    for name in ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT'):
        shutil.copy2(runtime / name, destination / ('DotNet-' + name))
    (destination / 'WebRTC-SOURCE.txt').write_text(
        'Godot webrtc-native 1.2.2-stable (MIT), libdatachannel (MPL-2.0), usrsctp (BSD).\n'
        'Corresponding source, submodules and rebuild instructions:\n'
        'https://github.com/godotengine/webrtc-native/tree/1.2.2-stable\n'
        'The dynamic extension can be rebuilt and replaced.\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=desktop.TARGETS, required=True)
    parser.add_argument('--godot', default=os.environ.get('GODOT', 'godot'))
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts')
    args = parser.parse_args()
    release = check_exports()
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    if os.environ.get('GITHUB_SHA', revision) != revision:
        raise RuntimeError('Checkout differs from the CI source revision')
    settings, item = desktop.TARGETS[args.target], desktop.names(args.target)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    run([sys.executable, ROOT / 'tools/networking/bootstrap_webrtc.py'])
    run(['dotnet', 'build', ROOT / 'Bomberman64Multiplayer.csproj', '--nologo', '-m:1'])
    run([args.godot, '--headless', '--path', ROOT, '--editor', '--import', '--quit'])
    with tempfile.TemporaryDirectory(prefix='blast-desktop-') as temporary:
        published = Path(temporary) / item['root']
        published.mkdir()
        run([args.godot, '--headless', '--path', ROOT, '--export-release', settings['preset'], published / settings['executable']])
        run(['dotnet', 'publish', ROOT / 'src/Bomber.Updater/Bomber.Updater.csproj', '-c', 'Release',
             '-r', 'win-x64' if args.target == 'windows-x64' else 'linux-x64', '--nologo', '-m:1',
             '-o', published / 'updater'])
        (published / 'release.json').write_text(json.dumps({'revision': revision, 'release': release, 'target': args.target}, indent=2) + '\n')
        notices(published / 'Notices', args.target, published)
        (published / 'README.txt').write_text(
            'Blast Circuit v' + release['version'] + '\n\n'
            'Extract the complete folder, then run ' + settings['executable'] + '.\n'
            'The .NET runtime is included. Keep the data folder and libraries beside the game.\n'
            'Updates: enable automatic checks in Settings, then Restart to update when offered.\n'
            'Windows installed and portable folders, and Linux AppImages, support signed updates.\n'
            'Your settings are stored separately.\n'
            'https://github.com/MCMike0399/bomberman-64-multiplayer/releases/latest\n'
            'Online play requires the exact current release version/build.\n'
            'Rendering API: ' + settings['driver'] + '. GPU drivers and OS libraries are required.\n'
            'Physical controller coverage and native GPU performance are not certified by headless smoke.\n')
        if args.target == 'windows-x64':
            owned = sorted({path.relative_to(published).as_posix() for path in published.rglob('*') if path.is_file()} | {'installed-files.txt'})
            (published / 'installed-files.txt').write_text('\n'.join(owned) + '\n')
        files = {}
        archive = output / item['archive']
        with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as bundle:
            for path in sorted(published.rglob('*')):
                if path.is_symlink():
                    raise RuntimeError('Unexpected symlink in desktop export')
                if path.is_file():
                    relative = path.relative_to(published).as_posix()
                    if path.suffix.lower() in ('.cs', '.pdb', '.key', '.pem'):
                        raise RuntimeError('Source/debug/private material in desktop export: ' + relative)
                    files[relative] = desktop.digest(path)
                    bundle.write(path, item['root'] + '/' + relative)
        (output / (item['archive'] + '.sha256')).write_text(desktop.digest(archive) + '  ' + item['archive'] + '\n')
        (output / item['metadata']).write_text(json.dumps({
            'schemaVersion': 1, 'target': args.target, 'revision': revision, 'release': release,
            'driver': settings['driver'], 'automaticUpdates': True, 'files': files,
            'graphicalValidation': 'requires-target-hardware', 'signature': 'release-manifest-only',
        }, indent=2) + '\n')
        desktop.inspect_archive(output, args.target, revision, release)
    print('Packaged ' + str(archive))


if __name__ == '__main__':
    main()

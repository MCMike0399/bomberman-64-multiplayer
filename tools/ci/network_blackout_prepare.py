#!/usr/bin/env python3
"""Stage signed shipped baseline binaries and an unversioned candidate for fault QA."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import zipfile

import desktop_packages
import verify_published as public

ROOT = Path(__file__).resolve().parents[2]
SHIPPED = 'af93089f385290de2f30954ef2822bd4051324da'
REPOSITORY = 'MCMike0399/bomberman-64-multiplayer'


def run(*command):
    subprocess.run(command, cwd=ROOT, check=True)


def baseline(destination):
    cache = destination / 'downloads'; cache.mkdir(parents=True)
    release = public.api('repos/' + REPOSITORY + '/releases/tags/v0.45.0', os.environ.get('GH_TOKEN'))
    if release['draft'] or release['target_commitish'] != SHIPPED: raise RuntimeError('Baseline must be the immutable shipped v0.45.0')
    assets = {item['name']: item for item in release['assets']}
    required = ['Blast-Circuit-release.json', 'Blast-Circuit-release.json.sig', 'Blast-Circuit-linux-x64.zip',
                'Blast-Circuit-linux-x64.zip.sha256', 'Blast-Circuit-linux-x64-metadata.json',
                'Blast-Circuit-server-dotnet10.zip', 'Blast-Circuit-server-dotnet10.zip.sha256', 'server-build-metadata.json']
    for name in required: public.download(assets[name]['browser_download_url'], cache / name)
    public.verify_signature(cache / 'Blast-Circuit-release.json')
    delivery = json.loads((cache / 'Blast-Circuit-release.json').read_text())
    if delivery['revision'] != SHIPPED: raise RuntimeError('Signed baseline source mismatch')
    for name in required[2:]:
        path = cache / name
        if name in delivery['assets']:
            entry = delivery['assets'][name]
            if public.digest(path) != entry['sha256'] or path.stat().st_size != entry['size']: raise RuntimeError('Signed baseline asset mismatch: ' + name)
        elif name == 'Blast-Circuit-server-dotnet10.zip.sha256':
            archive = name.removesuffix('.sha256')
            if path.read_text() != delivery['assets'][archive]['sha256'] + '  ' + archive + '\n': raise RuntimeError('Baseline sidecar differs from signed archive')
        else: raise RuntimeError('Unexpected unsigned baseline input')
    desktop_packages.inspect_archive(cache, 'linux-x64', SHIPPED, delivery['release'])
    public.inspect_server(cache, SHIPPED, '0.45.0', release=delivery['release'])
    server = destination / 'baseline/server'; server.mkdir(parents=True)
    with zipfile.ZipFile(cache / 'Blast-Circuit-server-dotnet10.zip') as bundle:
        for member in bundle.infolist():
            relative = PurePosixPath(member.filename).relative_to('Blast-Circuit-server-dotnet10')
            if member.is_dir(): continue
            if '..' in relative.parts: raise RuntimeError('Unsafe server archive member')
            target = server / str(relative); target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(bundle.read(member))
    client = destination / 'baseline/client'; client.mkdir(parents=True)
    with zipfile.ZipFile(cache / 'Blast-Circuit-linux-x64.zip') as bundle:
        for name in ('Bomber.Client.dll', 'Bomber.Game.dll', 'Burbuja.Realtime.Abstractions.dll'):
            matches = [item for item in bundle.infolist() if PurePosixPath(item.filename).name == name]
            if len(matches) != 1: raise RuntimeError('Baseline managed assembly is ambiguous: ' + name)
            (client / name).write_bytes(bundle.read(matches[0]))
    return client


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(); output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    client = baseline(output)
    run('dotnet', 'build', 'tests/Bomber.NetworkResilience.Probe', '--nologo', '-m:1', '-c', 'Release',
        '-p:BaselineAssemblies=' + str(client), '--output', str(output / 'baseline/probe'))
    run('dotnet', 'publish', 'src/Bomber.Server', '-c', 'Release', '--self-contained', 'false', '-p:UseAppHost=false',
        '--nologo', '-m:1', '--output', str(ROOT / 'artifacts/server-release'))
    run('python3', 'tools/ci/package_server.py', '--stage-only')
    shutil.copytree(ROOT / 'artifacts/server-staged', output / 'candidate/server')
    run('dotnet', 'build', 'tests/Bomber.NetworkResilience.Probe', '--nologo', '-m:1', '-c', 'Release', '--output', str(output / 'candidate/probe'))
    for variant in ('baseline', 'candidate'):
        root = output / variant
        # Runtime-only fixtures: no source, symbols, user settings or credential files.
        for path in root.rglob('*'):
            if path.is_file() and path.suffix.lower() in ('.pdb', '.cs'): path.unlink()
        manifest = {'variant': variant, 'productSource': SHIPPED if variant == 'baseline' else
                    subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                    'baselineUsesSignedPublishedBinaries': variant == 'baseline',
                    'probeSource': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                    'files': {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob('*') if p.is_file()}}
        (root / 'identity.json').write_text(json.dumps(manifest, indent=2) + '\n')


if __name__ == '__main__': main()

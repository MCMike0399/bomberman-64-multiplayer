#!/usr/bin/env python3
"""Stage and archive the tested portable server without publishing its source/config."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
ARCHIVE = 'Blast-Circuit-server-dotnet10.zip'
REQUIRED = ('Bomber.Server.dll', 'Bomber.Server.deps.json', 'Bomber.Server.runtimeconfig.json')
NOTICES = ('Notices/Blast-Circuit-MIT.txt', 'Notices/Simulation-MIT.txt', 'README.txt', 'appsettings.json')


def runtime_file(path):
    return (path.suffix.lower() in ('.dll', '.dylib') or re.search(r'\.so(?:\.\d+)*$', path.name)
            or (len(path.parts) == 1 and path.name in REQUIRED))


def stage(source, destination):
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    for path in sorted(source.rglob('*')):
        if not path.is_file() or path.is_symlink() or not path.resolve().is_relative_to(source.resolve()):
            continue
        relative = path.relative_to(source)
        if any(part.startswith('.') for part in relative.parts) or not runtime_file(relative):
            continue
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
    for name in REQUIRED:
        if not (destination / name).is_file() or (destination / name).stat().st_size == 0:
            raise RuntimeError('Published server is incomplete: ' + name)
    # Use only the game's tracked public defaults. Never copy arbitrary host or
    # transitive kernel settings from the publish tree.
    shutil.copyfile(ROOT / 'src/Bomber.Server/appsettings.json', destination / 'appsettings.json')
    notices = destination / 'Notices'
    notices.mkdir()
    for source_name, target in [('LICENSE', 'Blast-Circuit-MIT.txt'),
                                ('src/Bomber.Game/Simulation/LICENSE', 'Simulation-MIT.txt')]:
        shutil.copyfile(ROOT / source_name, notices / target)
    (destination / 'README.txt').write_text(
        'Requires ASP.NET Core Runtime 10 (or .NET SDK 10) for this host.\n'
        'Run: dotnet Bomber.Server.dll --urls http://127.0.0.1:18496\n'
        'Configure service lifetime, external bind/proxy, authentication and persistence on your own host.\n'
        'No deployment, credentials, private source or host configuration is included.\n')
    return {path.relative_to(destination).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in sorted(destination.rglob('*')) if path.is_file()}


def package(destination, artifacts, revision, version, inventory):
    archive = artifacts / ARCHIVE
    if not set(REQUIRED).issubset(inventory):
        raise RuntimeError('Server delivery must be staged and smoke-tested before packaging')
    files = []
    for name, digest in sorted(inventory.items()):
        path = destination / name
        if not runtime_file(Path(name)) and name not in NOTICES:
            raise RuntimeError('Server archive file is outside the delivery allowlist: ' + name)
        if (Path(name).is_absolute() or any(part.startswith('.') for part in Path(name).parts)
                or path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(destination.resolve())
                or hashlib.sha256(path.read_bytes()).hexdigest() != digest):
            raise RuntimeError('Staged server runtime changed during smoke verification: ' + name)
        files.append(path)
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as output:
        for path in files:
            output.write(path, 'Blast-Circuit-server-dotnet10/' + path.relative_to(destination).as_posix())
    with archive.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    (artifacts / (ARCHIVE + '.sha256')).write_text(digest + '  ' + ARCHIVE + '\n')
    metadata = {'schemaVersion': 1, 'revision': revision, 'version': version, 'kind': 'framework-dependent-server',
                'targetFramework': 'net10.0', 'entrypoint': 'Bomber.Server.dll', 'configuration': 'Release',
                'runtimeRequirement': 'ASP.NET Core Runtime 10', 'fileCount': len(files),
                'files': inventory}
    (artifacts / 'server-build-metadata.json').write_text(json.dumps(metadata, indent=2) + '\n')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage-only', action='store_true')
    args = parser.parse_args(argv)
    artifacts = ROOT / 'artifacts'
    destination = artifacts / 'server-staged'
    inventory_path = artifacts / 'server-stage-manifest.json'
    if args.stage_only:
        inventory = stage(artifacts / 'server-release', destination)
        inventory_path.write_text(json.dumps(inventory, indent=2) + '\n')
        print('Staged portable server runtime; source, symbols and host configuration excluded.')
        return
    revision = os.environ.get('GITHUB_SHA') or subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    if not re.fullmatch(r'[0-9a-f]{40}', revision):
        raise RuntimeError('Invalid server source revision')
    match = re.search(r'^application/short_version="([0-9]+\.[0-9]+\.[0-9]+)"$', (ROOT / 'export_presets.cfg').read_text(), re.MULTILINE)
    if not match:
        raise RuntimeError('Missing shared product version')
    inventory = json.loads(inventory_path.read_text())
    package(destination, artifacts, revision, match.group(1), inventory)
    print('Archived tested portable server: ' + str(artifacts / ARCHIVE))


if __name__ == '__main__':
    main()

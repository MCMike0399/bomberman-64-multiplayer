#!/usr/bin/env python3
"""Promote only a successful main Verification artifact into a draft release."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import subprocess
import tempfile
import zipfile
import sys
from release_identity import check_exports
from verify_deployment import targets_from_json, verify, verify_connections, placements_from_json

ARTIFACT = 'Blast-Circuit-macos-arm64-ad-hoc'
ARCHIVE = 'Blast-Circuit-macos-arm64.zip'
SERVER_ARTIFACT = 'Blast-Circuit-server-dotnet10'
SERVER_ARCHIVE = SERVER_ARTIFACT + '.zip'


def gh(*arguments):
    result = subprocess.run(['gh', *arguments], capture_output=True, text=True, timeout=120)
    if result.returncode:
        raise RuntimeError('GitHub CLI operation failed: ' + ' '.join(arguments[:2]))
    return result.stdout


def inspect_run(repository, run_id):
    run = json.loads(gh('api', f'repos/{repository}/actions/runs/{run_id}'))
    if (run.get('head_repository', {}).get('full_name') != repository or run.get('head_branch') != 'main'
            or run.get('status') != 'completed' or run.get('conclusion') != 'success'
            or run.get('event') not in ('push', 'workflow_dispatch')
            or run.get('path') != '.github/workflows/ci.yml'
            or not re.fullmatch(r'[0-9a-f]{40}', run.get('head_sha', ''))):
        raise RuntimeError('Promotion requires a successful completed Verification run from this repository main branch')
    # Overall success can include skipped jobs: require the actual package gate.
    jobs = json.loads(gh('api', f'repos/{repository}/actions/runs/{run_id}/jobs?per_page=100'))['jobs']
    required = {'Public contracts and audio', 'Trusted managed and network', 'Trusted Godot headless', 'Trusted Apple Silicon package'}
    passed = {item['name'] for item in jobs if item.get('conclusion') == 'success'}
    if not required.issubset(passed):
        raise RuntimeError('Required verification/package jobs did not all pass')
    artifacts = json.loads(gh('api', f'repos/{repository}/actions/runs/{run_id}/artifacts?per_page=100'))['artifacts']
    for name in (ARTIFACT, SERVER_ARTIFACT):
        candidates = [item for item in artifacts if item['name'] == name and not item.get('expired')]
        if len(candidates) != 1:
            raise RuntimeError('Expected exactly one unexpired tested artifact: ' + name)
    return run['head_sha']


def require_current_main(repository, revision):
    head = json.loads(gh('api', f'repos/{repository}/git/ref/heads/main'))
    if head.get('object', {}).get('sha') != revision:
        raise RuntimeError('Refusing stale deployment/publication: verify the current main commit first')


def inspect_existing_release(repository, version, revision, publish=False):
    refs = json.loads(gh('api', f'repos/{repository}/git/matching-refs/tags/v{version}'))
    existing = next((item for item in refs if item.get('ref') == 'refs/tags/v' + version), None)
    if existing and (not publish or existing.get('object', {}).get('sha') != revision):
        raise RuntimeError('Release tag already exists; refusing to reuse or move it')
    # GitHub drafts may not have created their tag yet. Inspect drafts independently
    # of refs, and paginate so an older draft cannot become a duplicate release.
    pages = json.loads(gh('api', f'repos/{repository}/releases?per_page=100', '--paginate', '--slurp'))
    published = [item for page in pages for item in page if not item.get('draft') and not item.get('prerelease')]
    for item in published:
        tag = item.get('tag_name', '')
        if not re.fullmatch(r'v[0-9]+\.[0-9]+\.[0-9]+', tag):
            raise RuntimeError('Unrecognized published version; inspect the release history before delivery')
        if tuple(map(int, tag[1:].split('.'))) >= tuple(map(int, version.split('.'))):
            raise RuntimeError('Candidate must upgrade every published stable version; clients refuse downgrades')
    if published:
        latest = max(published, key=lambda item: tuple(map(int, item['tag_name'][1:].split('.'))))
        metadata = json.loads(gh('release', 'download', latest['tag_name'], '--repo', repository,
                                 '--pattern', 'build-metadata.json', '--output', '-'))
        prior_build = metadata.get('release', {}).get('build', metadata.get('build'))
        if prior_build is None:
            # Older releases predate Release.props and omitted the build in metadata.
            # Inspect their published ZIP in place, without creating a launchable app.
            with tempfile.TemporaryDirectory(prefix='blast-prior-release-') as temporary:
                gh('release', 'download', latest['tag_name'], '--repo', repository,
                   '--pattern', ARCHIVE, '--dir', temporary)
                with zipfile.ZipFile(Path(temporary) / ARCHIVE) as bundle:
                    prior_build = plistlib.loads(bundle.read('Blast Circuit.app/Contents/Info.plist')).get('CFBundleVersion')
        if not str(prior_build).isdigit() or int(check_exports()['build']) <= int(prior_build):
            raise RuntimeError('Candidate build must increase beyond the published build')
    matches = [item for page in pages for item in page if item.get('tag_name') == 'v' + version]
    if matches or existing:
        if (publish and len(matches) == 1 and matches[0].get('draft') is True
                and matches[0].get('target_commitish') == revision):
            return True
        raise RuntimeError('Release already exists; only an exact-source draft can be published')
    return False


def verify_uploaded(repository, version, assets):
    """Download the staged draft once and compare the bytes players will receive."""
    with tempfile.TemporaryDirectory(prefix='blast-release-upload-check-') as temporary:
        arguments = ['release', 'download', 'v' + version, '--repo', repository, '--dir', temporary]
        for asset in assets:
            arguments.extend(['--pattern', Path(asset).name])
        gh(*arguments)
        for asset in assets:
            source = Path(asset)
            downloaded = Path(temporary) / source.name
            if not downloaded.is_file() or hashlib.sha256(downloaded.read_bytes()).digest() != hashlib.sha256(source.read_bytes()).digest():
                raise RuntimeError('Uploaded release bytes differ: ' + source.name)


def inspect_package(directory, revision, version):
    archive = directory / ARCHIVE
    receipt = (directory / (ARCHIVE + '.sha256')).read_text().strip()
    match = re.fullmatch(r'([0-9a-fA-F]{64})\s+\*?' + re.escape(ARCHIVE), receipt)
    if not match:
        raise RuntimeError('Malformed package checksum receipt')
    with archive.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if digest != match.group(1).lower():
        raise RuntimeError('Package SHA-256 mismatch')
    metadata = json.loads((directory / 'build-metadata.json').read_text())
    if metadata.get('revision') != revision or metadata.get('version') != version:
        raise RuntimeError('Artifact source revision or version does not match the requested release')
    if metadata.get('release') != check_exports() or metadata['release']['version'] != version:
        raise RuntimeError('Client release compatibility identity does not match the verified source')
    if metadata.get('features', {}).get('switch2UsbBackend') is not True:
        raise RuntimeError('Artifact feature manifest does not include the required Switch 2 USB backend')
    with zipfile.ZipFile(archive) as bundle:
        # Read the plist in-place, never extract paths supplied by an artifact.
        plist = plistlib.loads(bundle.read('Blast Circuit.app/Contents/Info.plist'))
        for name in ('libSDL3.dylib', 'libusb-1.0.dylib', 'SDL-LICENSE.txt', 'libusb-LICENSE.txt'):
            member = 'Blast Circuit.app/Contents/Frameworks/Controllers/' + name
            if member not in bundle.namelist() or bundle.getinfo(member).file_size == 0:
                raise RuntimeError('Archived controller backend is incomplete: ' + name)
    if (plist.get('CFBundleShortVersionString') != version or plist.get('CFBundleIdentifier') != 'io.github.mcmike0399.blastcircuit'
            or plist.get('CFBundleVersion') != metadata['release']['build']):
        raise RuntimeError('Bundle identity/version does not match the requested release')
    return archive


def inspect_server(directory, revision, version):
    archive = directory / SERVER_ARCHIVE
    receipt = (directory / (SERVER_ARCHIVE + '.sha256')).read_text().strip()
    match = re.fullmatch(r'([0-9a-fA-F]{64})\s+\*?' + re.escape(SERVER_ARCHIVE), receipt)
    with archive.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if not match or digest != match.group(1).lower():
        raise RuntimeError('Server package SHA-256 mismatch')
    metadata = json.loads((directory / 'server-build-metadata.json').read_text())
    if (metadata.get('revision') != revision or metadata.get('version') != version
            or metadata.get('kind') != 'framework-dependent-server' or metadata.get('targetFramework') != 'net10.0'):
        raise RuntimeError('Server artifact revision/version/runtime does not match the release')
    if metadata.get('release') != check_exports() or metadata['release']['version'] != version:
        raise RuntimeError('Server release compatibility identity does not match the verified source')
    prefix = 'Blast-Circuit-server-dotnet10/'
    with zipfile.ZipFile(archive) as bundle:
        if len(bundle.namelist()) != len(set(bundle.namelist())) or any(
                ((item.external_attr >> 16) & 0o170000) == 0o120000 for item in bundle.infolist()):
            raise RuntimeError('Server archive contains duplicate entries or symbolic links')
        names = {name.removeprefix(prefix) for name in bundle.namelist()}
        if any(not name.startswith(prefix) for name in bundle.namelist()) or names != set(metadata.get('files', {})):
            raise RuntimeError('Server archive differs from its file manifest')
        required = {'Bomber.Server.dll', 'Bomber.Server.deps.json', 'Bomber.Server.runtimeconfig.json'}
        if not required.issubset(names):
            raise RuntimeError('Server archive is missing runtime entrypoint files')
        for name, digest in metadata['files'].items():
            if (Path(name).is_absolute() or '\\' in name or any(part.startswith('.') or part == '..' for part in Path(name).parts)
                    or Path(name).suffix in ('.cs', '.csproj', '.pdb', '.key', '.pem')):
                raise RuntimeError('Server archive contains forbidden source/configuration material')
            allowed = (Path(name).suffix.lower() in ('.dll', '.dylib') or re.search(r'\.so(?:\.\d+)*$', name)
                       or name in required or name in ('README.txt', 'appsettings.json', 'Notices/Blast-Circuit-MIT.txt', 'Notices/Simulation-MIT.txt')
                       or len(Path(name).parts) == 3 and Path(name).parts[0] == 'plugins' and Path(name).parts[1] in ('Blast.Cluster', 'Realtime.Redis')
                       and (Path(name).name == 'plugin.json' or Path(name).name.endswith('.deps.json')))
            if not allowed:
                raise RuntimeError('Server archive file is outside the runtime delivery allowlist')
            if hashlib.sha256(bundle.read(prefix + name)).hexdigest() != digest:
                raise RuntimeError('Server file checksum mismatch: ' + name)
    return archive


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--repository', default=os.environ.get('GITHUB_REPOSITORY', ''))
    parser.add_argument('--expected-sha')
    parser.add_argument('--check-only', action='store_true')
    parser.add_argument('--signing-key', type=Path)
    parser.add_argument('--publish', action='store_true', help='Publish the verified signed release instead of creating a draft')
    args = parser.parse_args(argv)
    if not re.fullmatch(r'[1-9][0-9]*', args.run_id) or not re.fullmatch(r'v?[0-9]+\.[0-9]+\.[0-9]+', args.version):
        parser.error('Use a positive numeric run ID and a version such as 0.15.0')
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', args.repository):
        parser.error('Repository must be owner/name')
    version = args.version.removeprefix('v')
    revision = inspect_run(args.repository, args.run_id)
    if args.expected_sha and revision != args.expected_sha:
        raise RuntimeError('Run source revision differs from the validated revision')
    # No publication escape hatch: direct CLI calls must satisfy the same live fleet gate as CI.
    targets = targets_from_json(os.environ.get('BLAST_RELEASE_SERVER_URLS', '{}')) if args.publish else None
    placements = placements_from_json(os.environ.get('BLAST_RELEASE_PLACEMENTS', '{}'), targets) if args.publish else None
    if args.check_only:
        if os.environ.get('GITHUB_OUTPUT'):
            with open(os.environ['GITHUB_OUTPUT'], 'a') as stream:
                stream.write('revision=' + revision + '\n')
        print('Validated successful main Verification run at ' + revision)
        return
    existing_draft = inspect_existing_release(args.repository, version, revision, args.publish)
    with tempfile.TemporaryDirectory(prefix='blast-release-') as temporary:
        directory = Path(temporary)
        gh('run', 'download', args.run_id, '--repo', args.repository, '--name', ARTIFACT, '--dir', str(directory))
        archive = inspect_package(directory, revision, version)
        gh('run', 'download', args.run_id, '--repo', args.repository, '--name', SERVER_ARTIFACT, '--dir', str(directory))
        server = inspect_server(directory, revision, version)
        signed = []
        if args.signing_key:
            signer = Path(__file__).resolve().parents[1] / 'updates/sign_release.py'
            subprocess.run([sys.executable, str(signer), '--archive', str(archive), '--key', str(args.signing_key), '--output', str(directory), '--server', str(server)], check=True)
            signed = [str(directory / name) for name in ('Blast-Circuit-update.json', 'Blast-Circuit-update.json.sig',
                                                      'Blast-Circuit-release.json', 'Blast-Circuit-release.json.sig')]
        elif args.publish or tuple(map(int, version.split('.'))) >= (0, 21, 0):
            raise RuntimeError('This release requires --signing-key for authenticated in-app updates')
        if args.publish:
            verify(targets, check_exports(), revision)
        notes = directory / 'release-notes.txt'
        notes.write_text(f'Apple Silicon client and portable .NET 10 server from {revision}.\n\n'
                         f'Verification: https://github.com/{args.repository}/actions/runs/{args.run_id}\n\n'
                         'Managed, network, headless and export checks passed. Metal graphical validation is a separate optional lane. '
                         'Ad-hoc signature; Apple notarization and physical controller validation are not claimed.\n')
        assets = [str(archive), str(directory / (ARCHIVE + '.sha256')), str(server), str(directory / (SERVER_ARCHIVE + '.sha256')),
                  str(directory / 'server-build-metadata.json'), str(directory / 'build-metadata.json'), *signed]
        if existing_draft:
            gh('release', 'upload', 'v' + version, *assets, '--repo', args.repository, '--clobber')
        else:
            gh('release', 'create', 'v' + version, *assets, '--repo', args.repository,
               '--draft', '--target', revision,
               '--title', 'Blast Circuit v' + version, '--notes-file', str(notes))
        verify_uploaded(args.repository, version, assets)
        if args.publish:
            # Main may advance after deployment. The exact live fleet is the publication
            # authority; rechecking it allows safe publication retry without another rollout.
            verify(targets, check_exports(), revision)
            verify_connections(targets, placements)
            gh('release', 'edit', 'v' + version, '--repo', args.repository, '--draft=false', '--latest', '--notes-file', str(notes))
        print(('Published signed release v' if args.publish else 'Created draft release v') + version + ' at ' + revision)


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Verify a published release exactly as players and the updater receive it.

Assets are downloaded anonymously from their public URLs. Both P-256 signatures
are checked against the public key shipped in the client, every signed asset
hash/size is compared, the archives' identities and inventories are inspected
in place (nothing launchable is extracted), the tag must resolve to the signed
source revision, and the latest stable channel must point to the release. With
--run-id, the published bytes must also equal the trusted Verification artifacts.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time
import urllib.request
import desktop_packages
import download_formats

from promote_release import ARCHIVE, ARTIFACT, SERVER_ARCHIVE, SERVER_ARTIFACT, gh, inspect_gates, inspect_package, inspect_server

ROOT = Path(__file__).resolve().parents[2]
PUBLIC_KEY = ROOT / 'src/Bomber.Client/Updates/UpdatePublicKey.txt'
SIGNED = ('Blast-Circuit-update.json', 'Blast-Circuit-release.json')
ASSETS = (ARCHIVE, ARCHIVE + '.sha256', SERVER_ARCHIVE, SERVER_ARCHIVE + '.sha256', 'build-metadata.json',
          'server-build-metadata.json', *SIGNED, *(name + '.sig' for name in SIGNED))
API = 'https://api.github.com/'


def api(path, token=None):
    request = urllib.request.Request(API + path, headers={'Accept': 'application/vnd.github+json', 'User-Agent': 'blast-circuit-release-check'})
    if token:
        # Only metadata requests are authenticated (shared runner IP quotas); asset downloads never are.
        request.add_header('Authorization', 'Bearer ' + token)
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)


def download(url, destination):
    request = urllib.request.Request(url, headers={'User-Agent': 'blast-circuit-release-check'})
    with urllib.request.urlopen(request, timeout=300) as response, destination.open('wb') as output:
        while chunk := response.read(1 << 20):
            output.write(chunk)


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def verify_signature(document, key=None):
    """Check a detached base64 DER ECDSA signature with the client's trusted public key."""
    key = key or PUBLIC_KEY
    signature = Path(str(document) + '.sig')
    with tempfile.TemporaryDirectory(prefix='blast-signature-check-') as temporary:
        public = Path(temporary) / 'public.der'
        public.write_bytes(base64.b64decode(key.read_text().strip(), validate=True))
        raw = Path(temporary) / 'signature.der'
        raw.write_bytes(base64.b64decode(signature.read_text().strip(), validate=True))
        result = subprocess.run(['openssl', 'dgst', '-sha256', '-verify', str(public), '-keyform', 'DER',
                                 '-signature', str(raw), str(document)], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError('Signature verification failed: ' + document.name)


def verify_update_manifest(directory, version):
    """The updater's own trust path: signed manifest whose hash/size bind the client ZIP."""
    manifest_path = directory / 'Blast-Circuit-update.json'
    verify_signature(manifest_path)
    manifest = json.loads(manifest_path.read_text())
    archive = directory / ARCHIVE
    if (manifest.get('schemaVersion') != 1 or manifest.get('version') != version
            or manifest.get('sha256') != digest(archive) or manifest.get('size') != archive.stat().st_size):
        raise RuntimeError('Signed updater manifest does not match the published client archive')
    return manifest


def verify_assets(directory, version):
    """Verify signatures, signed inventory and archive contents; return the signed delivery."""
    verify_update_manifest(directory, version)
    delivery_path = directory / 'Blast-Circuit-release.json'
    verify_signature(delivery_path)
    delivery = json.loads(delivery_path.read_text())
    release, revision = delivery.get('release', {}), delivery.get('revision', '')
    if delivery.get('schemaVersion') != 1 or release.get('version') != version or not re.fullmatch(r'[0-9a-f]{40}', revision):
        raise RuntimeError('Signed delivery manifest does not describe this version')
    expected = {ARCHIVE, SERVER_ARCHIVE, 'build-metadata.json', 'server-build-metadata.json', 'Blast-Circuit-update.json'}
    if desktop_packages.present(directory):
        expected |= {name for target in desktop_packages.present_targets(directory) for name in desktop_packages.asset_names(target)}
        expected |= {'Blast-Circuit-' + target + '-update.json' for target in desktop_packages.present_targets(directory)}
    if 'macos-arm64' in download_formats.present(directory):
        expected |= set(download_formats.names('macos-arm64'))
    if set(delivery.get('assets', {})) != expected:
        raise RuntimeError('Signed delivery manifest has an unexpected asset inventory')
    for name, entry in delivery['assets'].items():
        path = directory / name
        if entry.get('sha256') != digest(path) or entry.get('size') != path.stat().st_size:
            raise RuntimeError('Published asset differs from its signed hash/size: ' + name)
    inspect_package(directory, revision, version, release=release)
    inspect_server(directory, revision, version, release=release)
    if desktop_packages.present(directory):
        desktop_packages.inspect(directory, revision, release, targets=desktop_packages.present_targets(directory))
        for target in desktop_packages.present_targets(directory):
            download_formats.inspect(directory, target, revision, release)
            platform_manifest = directory / ('Blast-Circuit-' + target + '-update.json')
            verify_signature(platform_manifest)
            update = json.loads(platform_manifest.read_text())
            payload = directory / (desktop_packages.names(target)['archive'] if target == 'windows-x64' else download_formats.FILES[target])
            if update != {'schemaVersion': 1, 'version': version, 'sha256': digest(payload), 'size': payload.stat().st_size}:
                raise RuntimeError('Platform updater manifest differs from published payload: ' + target)
    if 'macos-arm64' in download_formats.present(directory):
        download_formats.inspect(directory, 'macos-arm64', revision, release)
    return delivery


def tag_commit(repository, version, token):
    reference = api(f'repos/{repository}/git/ref/tags/v{version}', token)['object']
    if reference['type'] == 'tag':
        reference = api(f'repos/{repository}/git/tags/{reference["sha"]}', token)['object']
    return reference['sha']


def compare_run_artifacts(repository, run_id, revision, directory):
    run = json.loads(gh('api', f'repos/{repository}/actions/runs/{run_id}'))
    if run.get('head_sha') != revision or run.get('conclusion') != 'success':
        raise RuntimeError('Verification run does not match the published source revision')
    with tempfile.TemporaryDirectory(prefix='blast-published-ci-') as temporary:
        for artifact in (ARTIFACT, SERVER_ARTIFACT, *desktop_packages.artifacts(desktop_packages.present_targets(directory))):
            gh('run', 'download', str(run_id), '--repo', repository, '--name', artifact, '--dir', temporary)
        names = (ARCHIVE, ARCHIVE + '.sha256', 'build-metadata.json', SERVER_ARCHIVE, SERVER_ARCHIVE + '.sha256', 'server-build-metadata.json')
        if desktop_packages.present(directory):
            names += tuple(name for target in desktop_packages.present_targets(directory) for name in desktop_packages.asset_names(target))
        if 'macos-arm64' in download_formats.present(directory):
            names += download_formats.names('macos-arm64')
        for name in names:
            if digest(Path(temporary) / name) != digest(directory / name):
                raise RuntimeError('Published bytes differ from the trusted Verification artifact: ' + name)
    return list(names)


def verify(repository, version, token=None, run_id=None, superseded=False, latest_wait=60):
    release = api(f'repos/{repository}/releases/tags/v{version}', token)
    if release.get('draft') or release.get('prerelease'):
        raise RuntimeError('Release is not a published stable release')
    published = {item['name']: item for item in release.get('assets', [])}
    # A historical release may predate a target. Accept only complete target groups;
    # their inclusion is subsequently authenticated by the signed delivery inventory.
    expected_assets = set(ASSETS)
    for target in desktop_packages.TARGETS:
        group = set(desktop_packages.asset_names(target)) | {'Blast-Circuit-' + target + '-update.json', 'Blast-Circuit-' + target + '-update.json.sig'}
        if group & set(published):
            expected_assets |= group
    if set(download_formats.names('macos-arm64')) & set(published):
        expected_assets |= set(download_formats.names('macos-arm64'))
    if set(published) != expected_assets:
        raise RuntimeError('Published asset set differs: ' + ', '.join(sorted(set(published) ^ set(ASSETS))))
    deadline = time.monotonic() + latest_wait
    while True:
        latest = api(f'repos/{repository}/releases/latest', token)['tag_name']
        if latest == 'v' + version or superseded or time.monotonic() > deadline:
            break
        time.sleep(5)
    if superseded:
        if tuple(map(int, latest[1:].split('.'))) <= tuple(map(int, version.split('.'))):
            raise RuntimeError('A superseded release check requires a newer latest stable release')
    elif latest != 'v' + version:
        raise RuntimeError(f'Latest stable channel points to {latest}, not v{version}')
    with tempfile.TemporaryDirectory(prefix='blast-published-') as temporary:
        directory = Path(temporary)
        for name, asset in published.items():
            download(asset['browser_download_url'], directory / name)
        delivery = verify_assets(directory, version)
        revision = delivery['revision']
        if tag_commit(repository, version, token) != revision:
            raise RuntimeError('Release tag does not resolve to the signed source revision')
        receipt = {'schemaVersion': 1, 'repository': repository, 'version': version, 'tag': 'v' + version,
                   'url': release['html_url'], 'publishedAt': release.get('published_at'), 'revision': revision,
                   'release': delivery['release'], 'latestStable': latest, 'anonymousDownloads': True,
                   'signatures': {name: 'verified' for name in SIGNED},
                   'assets': {name: {'sha256': digest(directory / name), 'size': (directory / name).stat().st_size}
                              for name in sorted(published)}}
        if run_id:
            receipt['verificationRun'] = {'id': int(run_id), 'identicalArtifacts': compare_run_artifacts(repository, run_id, revision, directory)}
            # Releases made before the launch gates existed have no gate receipts to report.
            try:
                receipt['releaseGates'] = inspect_gates(repository, run_id, version, delivery['release']['build'])
            except RuntimeError as error:
                receipt['releaseGates'] = {'unavailable': str(error)}
    return receipt


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--version', required=True)
    parser.add_argument('--repository', default=os.environ.get('GITHUB_REPOSITORY', 'MCMike0399/bomberman-64-multiplayer'))
    parser.add_argument('--run-id', help='Also require byte equality with this trusted Verification run (needs gh auth)')
    parser.add_argument('--superseded', action='store_true', help='Check an older release after a newer one became latest')
    parser.add_argument('--output', type=Path, help='Write the JSON receipt here')
    args = parser.parse_args(argv)
    version = args.version.removeprefix('v')
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+', version) or (args.run_id and not re.fullmatch(r'[1-9][0-9]*', args.run_id)):
        parser.error('Use a version such as 0.36.0 and a positive numeric run ID')
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', args.repository):
        parser.error('Repository must be owner/name')
    token = os.environ.get('GH_TOKEN') or os.environ.get('GITHUB_TOKEN')
    receipt = verify(args.repository, version, token, args.run_id, args.superseded)
    text = json.dumps(receipt, indent=2) + '\n'
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text)
    print(text, end='')
    print(f'PASS published v{version}: anonymous assets, signatures, signed inventory, archives, tag and latest channel', flush=True)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())

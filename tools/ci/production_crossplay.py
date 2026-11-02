#!/usr/bin/env python3
"""One-off v0.45 production witness. No server deployment and no product rebuild."""
import argparse
import json
import os
from pathlib import Path
import platform
import signal
import subprocess
import sys
import time

import crossplay
import crossplay_runtime as runtime
import desktop_packages as desktop
import download_formats as downloads
import verify_published as public
from desktop_rendering import execution_driver

SOURCE = 'af93089f385290de2f30954ef2822bd4051324da'
ORIGIN = 'https://blast.burbuja-lab.dev'
VERSION = '0.45.0'
REPOSITORY = 'MCMike0399/bomberman-64-multiplayer'
RELEASE = {'version': VERSION, 'build': '56', 'protocolRevision': 6, 'admissionToken': '6/0.45.0/56'}


def check_origin(origin):
    if (origin.get('origin') != ORIGIN or origin.get('revision') != SOURCE or
            origin.get('release') != RELEASE or origin.get('publishedTag') != 'v' + VERSION or
            not origin.get('serverArchiveSha256')):
        raise RuntimeError('Production witness requires the exact published release and fixed public origin')
    actual = crossplay.read_json(ORIGIN + '/game')['release']
    if actual != dict(RELEASE, revision=SOURCE):
        raise RuntimeError('Public server does not match the published client')


def verify_download(directory, name, inventory):
    path = directory / name
    if name == 'Blast-Circuit-macos-arm64.zip.sha256':
        archive = 'Blast-Circuit-macos-arm64.zip'
        expected = inventory[archive]['sha256'] + '  ' + archive + '\n'
        if path.read_text() != expected:
            raise RuntimeError('Mac ZIP checksum sidecar differs from the signed ZIP digest')
        return {'sha256': desktop.digest(path), 'size': path.stat().st_size,
                'binding': 'checksum-text-matches-signed-zip-digest',
                'sourceArchiveSha256': inventory[archive]['sha256']}
    entry = inventory[name]
    if desktop.digest(path) != entry['sha256'] or path.stat().st_size != entry['size']:
        raise RuntimeError('Public payload differs from the signed inventory: ' + name)
    return entry


def prepare(directory, target):
    directory.mkdir(parents=True, exist_ok=True)
    release = public.api('repos/' + REPOSITORY + '/releases/tags/v' + VERSION, os.environ.get('GH_TOKEN'))
    if release['draft'] or not release['published_at'] or release['target_commitish'] != SOURCE:
        raise RuntimeError('Expected exact-source published release')
    assets = {entry['name']: entry for entry in release['assets']}
    for name in ('Blast-Circuit-release.json', 'Blast-Circuit-release.json.sig'):
        public.download(assets[name]['browser_download_url'], directory / name)
    public.verify_signature(directory / 'Blast-Circuit-release.json')
    delivery = json.loads((directory / 'Blast-Circuit-release.json').read_text())
    if delivery['revision'] != SOURCE or delivery['release'] != RELEASE:
        raise RuntimeError('Signed public delivery identity mismatch')
    archive = 'Blast-Circuit-' + target + '.zip'
    metadata = 'build-metadata.json' if target == 'macos-arm64' else desktop.names(target)['metadata']
    required = (archive, archive + '.sha256', metadata, *downloads.names(target))
    verified = {}
    for name in required:
        public.download(assets[name]['browser_download_url'], directory / name)
        verified[name] = verify_download(directory, name, delivery['assets'])
    if target == 'macos-arm64':
        public.inspect_package(directory, SOURCE, VERSION, release=RELEASE)
    else:
        desktop.inspect_archive(directory, target, SOURCE, RELEASE)
    downloads.inspect(directory, target, SOURCE, RELEASE)
    origin = {'schemaVersion': 1, 'origin': ORIGIN, 'revision': SOURCE, 'release': RELEASE,
              'publishedTag': 'v' + VERSION, 'serverArchiveSha256': delivery['assets']['Blast-Circuit-server-dotnet10.zip']['sha256']}
    check_origin(origin)
    (directory / 'origin.json').write_text(json.dumps(origin, indent=2) + '\n')
    proof = dict(origin, actualOS=platform.system(), target=target, anonymousDownloads=True,
                 signedDeliverySha256=desktop.digest(directory / 'Blast-Circuit-release.json'),
                 verifiedPublicAssets=verified)
    (directory / 'production-provenance.json').write_text(json.dumps(proof, indent=2) + '\n')


def serve(directory, target, output):
    if os.name == 'posix':
        def cancelled(*_): raise KeyboardInterrupt('Production client cancelled')
        signal.signal(signal.SIGTERM, cancelled)
    check_origin(json.loads((directory / 'origin.json').read_text()))
    process = None
    result = {'exitCode': None, 'error': None}
    try:
        executable = runtime.install(directory, target, output)
        command = [str(executable), '--rendering-driver', execution_driver(target), '--windowed', '--max-fps', '30',
                   '--fixed-fps', '30', '--disable-vsync', '--', '--display-windowed',
                   '--playtest=cross-platform-quick-play', '--playtest-output=' + str(output),
                   '--playtest-server=' + ORIGIN, '--crossplay-role=' + target,
                   '--crossplay-release=' + str(output / 'release')]
        environment = crossplay.clean_environment()
        if target == 'linux-x64': environment.pop('APPIMAGE_EXTRACT_AND_RUN', None)
        with (output / 'runtime.log').open('w') as log:
            process = subprocess.Popen(command, cwd=output, env=environment, stdout=log, stderr=subprocess.STDOUT)
            result['exitCode'] = process.wait(timeout=crossplay.CLIENT_PROCESS_SECONDS)
    except BaseException as error:
        result['error'] = str(error)
        raise
    finally:
        if process is not None: crossplay.stop_child(process)
        (output / 'exit.json').write_text(json.dumps(result) + '\n')


def committed(output):
    deadline = time.monotonic() + 60
    while True:
        paths = [output / name for name in ('crossplay.json', 'crossplay-actions.json', 'crossplay-committed.json')]
        if all(path.exists() for path in paths) and len({path.read_bytes() for path in paths}) == 1:
            return
        if (output / 'exit.json').exists() or time.monotonic() > deadline:
            raise RuntimeError('Production action witness did not commit before final barrier')
        time.sleep(.2)


def aggregate(directory):
    subprocess.run([sys.executable, str(runtime.ROOT / 'tools/ci/crossplay.py'), 'aggregate', '--directory', str(directory)], check=True)
    reports = []
    for target in runtime.SYSTEMS:
        proof = json.loads((directory / target / 'production-provenance.json').read_text())
        check_origin(proof)
        if proof['target'] != target or proof['actualOS'] != runtime.SYSTEMS[target][0] or not proof['anonymousDownloads']:
            raise RuntimeError('Production witness OS or download provenance mismatch')
        receipt = json.loads((directory / target / 'crossplay-receipt.json').read_text())
        if receipt['downloadSha256'] != proof['verifiedPublicAssets'][downloads.FILES[target]]['sha256']:
            raise RuntimeError('Executed download differs from the public signed installer')
        reports.append(proof)
    combined = json.loads((directory / crossplay.RECEIPT).read_text())
    combined.update(verificationKind='production-public-installed-clients', productionOrigin=ORIGIN, provenance=reports)
    (directory / 'production-crossplay-receipt.json').write_text(json.dumps(combined, indent=2) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=('prepare', 'start', 'serve', 'committed', 'release', 'finish', 'stop', 'aggregate'))
    parser.add_argument('--target', choices=runtime.SYSTEMS)
    parser.add_argument('--directory', type=Path, default=runtime.ROOT / 'artifacts')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    directory = args.directory.resolve()
    if args.operation == 'aggregate': return aggregate(directory)
    if not args.target or platform.system() != runtime.SYSTEMS[args.target][0]:
        raise RuntimeError('Client must run on its actual native OS')
    if os.environ.get('GITHUB_ACTIONS') != 'true':
        raise RuntimeError('Production installer witness requires a disposable hosted CI desktop')
    if args.operation == 'prepare': return prepare(directory, args.target)
    output = args.output.resolve()
    if args.operation == 'serve': return serve(directory, args.target, output)
    if args.operation == 'committed': return committed(output)
    if args.operation == 'start':
        check_origin(json.loads((directory / 'origin.json').read_text()))
        command = [sys.executable, str(Path(__file__).resolve()), 'serve', '--target', args.target,
                   '--directory', str(directory), '--output', str(output)]
        if args.target == 'linux-x64':
            command = [sys.executable, str(runtime.ROOT / 'tools/packaging/linux-desktop.py'), '--', *command]
        process = crossplay.launch(command, output)
        (output / "production-provenance.json").write_bytes((directory / "production-provenance.json").read_bytes())
        deadline = time.monotonic() + crossplay.CLIENT_READINESS_SECONDS
        while not (output / 'crossplay-ready.json').exists():
            if process.poll() is not None or (output / 'exit.json').exists() or time.monotonic() > deadline:
                raise RuntimeError('Production rendered client failed before readiness')
            time.sleep(.2)
        return
    if args.operation != 'stop': check_origin(json.loads((directory / 'origin.json').read_text()))
    sys.argv = [str(Path(runtime.__file__)), args.operation, '--target', args.target, '--directory', str(directory),
                '--output', str(output), '--origin', str(directory / 'origin.json')]
    runtime.main()


if __name__ == '__main__': main()

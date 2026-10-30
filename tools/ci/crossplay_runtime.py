#!/usr/bin/env python3
"""Install the actual desktop download, keep its rendered game alive across the CI barrier, and verify its evidence."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import subprocess
import sys
import time
import zipfile

import crossplay
from desktop_rendering import DRIVERS, execution_driver, report_matches
import desktop_packages as desktop
import download_formats as downloads

ROOT = Path(__file__).resolve().parents[2]
sys.path.append(str(ROOT / 'tools/playtest'))
from contracts import validate_summary

SYSTEMS = {'windows-x64': ('Windows', 'Windows', DRIVERS['windows-x64']), 'linux-x64': ('Linux', 'Linux', 'vulkan'),
           'macos-arm64': ('Darwin', 'macOS', 'metal')}


def metadata(directory, target):
    return json.loads((directory / ('build-metadata.json' if target == 'macos-arm64' else desktop.names(target)['metadata'])).read_text())


def install(directory, target, output):
    data = metadata(directory, target)
    downloads.inspect(directory, target, data['revision'], data['release'])
    installed = output / 'installed'
    if target == 'windows-x64':
        subprocess.run([str(directory / downloads.FILES[target]), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
                        '/CURRENTUSER', '/DIR=' + str(installed)], check=True, timeout=180)
        for name, digest in data['files'].items():
            if desktop.digest(installed / name) != digest: raise RuntimeError('Installed Windows crossplay file differs: ' + name)
        return installed / 'BlastCircuit.exe'
    if target == 'linux-x64':
        executable = directory / downloads.FILES[target]; executable.chmod(0o755)
        return executable
    mount = output / 'dmg-volume'
    subprocess.run(['hdiutil', 'attach', '-readonly', '-nobrowse', '-noautoopen', '-mountpoint', str(mount),
                    str(directory / downloads.FILES[target])], check=True, timeout=120)
    try:
        installed.mkdir()
        subprocess.run(['ditto', str(mount / 'Blast Circuit.app'), str(installed / 'Blast Circuit.app')], check=True, timeout=120)
    finally:
        subprocess.run(['hdiutil', 'detach', str(mount)], check=True, timeout=120)
    with zipfile.ZipFile(directory / 'Blast-Circuit-macos-arm64.zip') as archive:
        for entry in archive.infolist():
            if entry.is_dir() or not entry.filename.startswith('Blast Circuit.app/'): continue
            path = installed / entry.filename
            actual = os.readlink(path).encode() if path.is_symlink() else path.read_bytes()
            if hashlib.sha256(actual).digest() != hashlib.sha256(archive.read(entry)).digest():
                raise RuntimeError('DMG-installed Mac payload differs: ' + entry.filename)
    return installed / 'Blast Circuit.app/Contents/MacOS/Blast Circuit'


def serve(directory, target, output, origin_path):
    if os.name == 'posix':
        def cancelled(*_): raise KeyboardInterrupt('Crossplay client cancelled')
        signal.signal(signal.SIGTERM, cancelled)
    origin = json.loads(origin_path.read_text()); data = metadata(directory, target)
    if (origin.get('revision') != data['revision'] or origin.get('release') != data['release'] or
            not re.fullmatch(r'https://[a-z0-9-]+\.trycloudflare\.com', origin.get('origin', ''))):
        raise RuntimeError('Crossplay may only use this exact candidate on its disposable staging origin')
    process = None
    result = {'exitCode': None, 'error': None}
    try:
        executable = install(directory, target, output)
        command = [str(executable), '--rendering-driver', execution_driver(target), '--windowed', '--max-fps', '30', '--fixed-fps', '30', '--disable-vsync', '--',
                   '--display-windowed', '--playtest=cross-platform-quick-play', '--playtest-output=' + str(output),
                   '--playtest-server=' + origin['origin'], '--crossplay-role=' + target, '--crossplay-release=' + str(output / 'release')]
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


def phase(output, name, started):
    # Bounded per-phase wall timings for budget decisions; never a pass criterion.
    path = output / 'phases.json'
    phases = json.loads(path.read_text()) if path.exists() else {}
    phases[name] = round(time.monotonic() - started, 1)
    path.write_text(json.dumps(phases, indent=2) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=('start', 'serve', 'release', 'finish', 'stop'))
    parser.add_argument('--target', choices=SYSTEMS, required=True)
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--origin', type=Path, default=ROOT / 'artifacts/origin/origin.json')
    args = parser.parse_args(); directory = args.directory.resolve(); output = args.output.resolve()
    if platform.system() != SYSTEMS[args.target][0]: raise RuntimeError('Crossplay client must run on its actual target OS')
    if args.operation == 'start':
        if os.environ.get('GITHUB_ACTIONS') != 'true': raise RuntimeError('Installer crossplay requires a disposable CI desktop')
        command = [sys.executable, str(Path(__file__).resolve()), 'serve', '--target', args.target, '--directory', str(directory),
                   '--output', str(output), '--origin', str(args.origin.resolve())]
        if args.target == 'linux-x64': command = [sys.executable, str(ROOT / 'tools/packaging/linux-desktop.py'), '--', *command]
        process = crossplay.launch(command, output)
        started = time.monotonic(); deadline = started + crossplay.CLIENT_READINESS_SECONDS
        try:
            while not (output / 'crossplay-ready.json').exists():
                if process.poll() is not None or (output / 'exit.json').exists() or time.monotonic() > deadline:
                    raise RuntimeError('Rendered crossplay client failed before readiness; inspect retained logs')
                time.sleep(.2)
        finally: phase(output, 'readiness', started)
    elif args.operation == 'serve': serve(directory, args.target, output, args.origin.resolve())
    elif args.operation == 'release':
        (output / 'release').touch()
        started = time.monotonic(); deadline = started + crossplay.CLIENT_ACTIONS_SECONDS
        try:
            while not (output / 'crossplay-actions.json').exists():
                if (output / 'exit.json').exists() or time.monotonic() > deadline:
                    raise RuntimeError('Mixed-platform game did not complete its action witness')
                time.sleep(.2)
        finally: phase(output, 'actions', started)
    elif args.operation == 'stop': crossplay.stop_launcher(output)
    else:
        receipt = {'schemaVersion': 1, 'passed': False, 'target': args.target}
        try:
            if not (output / 'crossplay-actions.json').exists(): raise RuntimeError('Final barrier requires an action witness from this client')
            (output / 'finish').touch()
            deadline = time.monotonic() + crossplay.CLIENT_FINISH_SECONDS
            while not (output / 'exit.json').exists():
                if time.monotonic() > deadline: raise RuntimeError('Mixed-platform game exceeded its bounded deadline')
                time.sleep(.2)
            status = json.loads((output / 'exit.json').read_text())
            report = json.loads((output / 'summary.json').read_text())
            errors = validate_summary(report, output, 'cross-platform-quick-play', False)
            text = (output / 'runtime.log').read_text(errors='replace')
            runtime_errors = [line for line in text.splitlines() if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|FATAL:)', line)]
            data = metadata(directory, args.target)
            if (status != {'exitCode': 0, 'error': None} or errors or runtime_errors or
                    report.get('release') != dict(data['release'], revision=data['revision']) or
                    report.get('platform') != SYSTEMS[args.target][1] or not report_matches(args.target,report) or
                    report.get('renderingMethod') != 'forward_plus' or not report.get('adapter')):
                raise RuntimeError('Mixed-platform gameplay/renderer evidence failed: ' + ', '.join(errors))
            if (output / 'crossplay-committed.json').exists():
                committed = (output / 'crossplay-committed.json').read_bytes()
                if (output / 'crossplay.json').read_bytes() != committed or (output / 'crossplay-actions.json').read_bytes() != committed:
                    raise RuntimeError('Final crossplay receipt differs from its immutable completed-action witness')
            state = json.loads((output / 'crossplay.json').read_text())
            if state.get('passed') is not True or state.get('role') != args.target: raise RuntimeError('Missing target crossplay witness')
            origin = json.loads(args.origin.read_text())
            if origin.get('revision') != data['revision'] or origin.get('release') != data['release']:
                raise RuntimeError('Crossplay server identity differs from the installed client')
            receipt = {'schemaVersion': 1, 'passed': True, 'target': args.target, 'revision': data['revision'], 'release': data['release'],
                'downloadSha256': desktop.digest(directory / downloads.FILES[args.target]), 'assertions': report['assertions'],
                'serverArchiveSha256': origin['serverArchiveSha256'],
                'runtimeErrors': runtime_errors, 'crossplay': state,
                'captures': {Path(path).name: desktop.digest(Path(path)) for path in report['screenshots']}}
            print('PASS actual installed ' + args.target + ' shared Quick Play')
        finally:
            output.mkdir(parents=True, exist_ok=True)
            (output / 'crossplay-receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
            crossplay.stop_launcher(output)
            uninstaller = output / 'installed/unins000.exe'
            if uninstaller.is_file(): subprocess.run([str(uninstaller), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'], check=True, timeout=120)


if __name__ == '__main__': main()

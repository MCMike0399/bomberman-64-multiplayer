#!/usr/bin/env python3
"""Exercise every saved preset/display startup on the target renderer without changing personal preferences."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import signal
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import desktop_packages as desktop
from desktop_rendering import execution_driver, report_matches


def run_startup(command, cwd, environment, log, timeout=240, on_timeout=None):
    process = subprocess.Popen(command, cwd=cwd, env=environment, stdout=log,
                               stderr=subprocess.STDOUT, start_new_session=os.name == 'posix')
    try:
        try:
            return subprocess.CompletedProcess(command, process.wait(timeout=timeout))
        except subprocess.TimeoutExpired:
            if on_timeout is not None:
                on_timeout(process)
            raise
    finally:
        if process.poll() is None:
            # AppImage extraction has a launcher and a game child. Stop this
            # owned tree before the caller destroys its X11/audio resources.
            if os.name == 'posix':
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
            else:
                subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], capture_output=True, timeout=15)
            process.wait(timeout=10)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=desktop.TARGETS, required=True)
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    args = parser.parse_args()
    settings, item = desktop.TARGETS[args.target], desktop.names(args.target)
    if platform.system() != settings['os']:
        raise RuntimeError('Saved-startup verification requires the target OS')
    directory = args.directory.resolve()
    metadata = json.loads((directory / item['metadata']).read_text())
    desktop.inspect_archive(directory, args.target, metadata['revision'], metadata['release'])
    spec = importlib.util.spec_from_file_location('verify_desktop', ROOT / 'tools/packaging/verify-desktop.py')
    verifier = importlib.util.module_from_spec(spec); spec.loader.exec_module(verifier)
    cases = []
    output = directory / (item['root'] + '-startup-details')
    output.mkdir()
    with tempfile.TemporaryDirectory(prefix='blast-saved-startup-') as temporary:
        temporary = Path(temporary)
        if args.target == 'windows-x64':
            verifier.extract(directory / item['archive'], temporary)
            executable = temporary / item['root'] / settings['executable']
        else:
            executable = directory / (item['root'] + '.AppImage')
        for preset, display, kind in [(p, d, 'menu') for p in ('low', 'medium', 'high') for d in ('fullscreen', 'windowed')] + [(p, 'fullscreen', 'gameplay') for p in ('low', 'medium', 'high')]:
            name = '-'.join((preset, display, kind))
            folder = output / name; folder.mkdir()
            preferences = folder / 'preferences.cfg'
            preferences.write_text('[player]\nautomaticUpdates=false\n[video]\n' +
                'graphicsPreset="' + preset + '"\nfullscreen=' + ('true' if display == 'fullscreen' else 'false') + '\nresolution=Vector2i(0, 0)\n')
            command = [str(executable), '--rendering-driver', execution_driver(args.target), '--',
                       '--smoke-menu' if kind == 'menu' else '--smoke', '--smoke-seconds=4',
                       '--smoke-preferences=' + str(preferences), '--smoke-output=' + str(folder)]
            environment = os.environ.copy()
            if args.target == 'linux-x64': environment['APPIMAGE_EXTRACT_AND_RUN'] = '1'
            started = time.monotonic()
            with (folder / 'runtime.log').open('w') as log:
                result = run_startup(command, temporary, environment, log)
            text = (folder / 'runtime.log').read_text(errors='replace')
            smoke = re.findall(r'^GODOT_SMOKE_OK (\S+) snapshots=(\d+) graphics=(\S+) window=(\S+) size=(\d+)x(\d+) adapter=(.+)$', text, re.MULTILINE)
            if (result.returncode != 0 or len(smoke) != 1 or re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|FATAL:)', text) or
                    smoke[0][0] != kind or smoke[0][2:4] != (preset, display) or min(map(int, smoke[0][4:6])) < 320 or
                    kind == 'gameplay' and int(smoke[0][1]) <= 0):
                raise RuntimeError('Saved startup failed: ' + name)
            capture = folder / (kind + '.png')
            if not capture.is_file() or capture.stat().st_size < 1024 or capture.read_bytes()[:8] != b'\x89PNG\r\n\x1a\n':
                raise RuntimeError('Saved startup did not capture its rendered viewport: ' + name)
            renderer=json.loads((folder/'renderer-selection.json').read_text())
            if renderer.get('sourceRevision')!=metadata['revision'] or not report_matches(args.target,renderer,'driver') or renderer.get('method')!='forward_plus':
                raise RuntimeError('Startup selected an unrecorded or unsupported renderer: '+name)
            cases.append({'id': name, 'passed': True, 'preset': preset, 'display': display, 'kind': kind,
                          'driver':renderer['driver'],'requestedDriver':renderer['requestedDriver'],'fallbackReason':renderer.get('fallbackReason'),'method':renderer['method'],
                          'adapter': smoke[0][6], 'snapshots': int(smoke[0][1]), 'seconds': time.monotonic() - started})
            print('PASS saved ' + name, flush=True)
    (directory / (item['root'] + '-startup.json')).write_text(json.dumps({'schemaVersion': 1, 'passed': True,
        'target': args.target, 'revision': metadata['revision'], 'release': metadata['release'],
        'archiveSha256': desktop.digest(directory / item['archive']), 'cases': cases}, indent=2) + '\n')


if __name__ == '__main__':
    main()

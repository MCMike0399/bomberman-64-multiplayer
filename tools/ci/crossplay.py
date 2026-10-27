#!/usr/bin/env python3
"""Coordinate disposable three-OS game clients through Actions artifacts and an isolated staged server."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import signal
import subprocess
import sys
import time
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
TARGETS = ('windows-x64', 'linux-x64', 'macos-arm64')
ARTIFACT = 'release-gate-crossplay'
RECEIPT = 'Blast-Circuit-crossplay.json'
CLOUDFLARED = 'https://github.com/cloudflare/cloudflared/releases/download/2026.10.5/cloudflared-linux-amd64'
CLOUDFLARED_SHA = 'e1bc0cf084fd9e590554fe769a86b1db46701c3f847a43d902f7b974a4e16298'


def clean_environment():
    environment = os.environ.copy()
    for name in ('GH_TOKEN', 'GITHUB_TOKEN', 'ACTIONS_RUNTIME_TOKEN', 'ACTIONS_ID_TOKEN_REQUEST_TOKEN'):
        environment.pop(name, None)
    return environment


def github(*args):
    return subprocess.check_output(['gh', *map(str, args)], text=True, timeout=60)


def wait_artifacts(names, timeout):
    if not names: raise ValueError('An explicit artifact barrier is required')
    repository, run_id = os.environ['GITHUB_REPOSITORY'], os.environ['GITHUB_RUN_ID']
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        assets = json.loads(github('api', f'repos/{repository}/actions/runs/{run_id}/artifacts?per_page=100'))['artifacts']
        if set(names).issubset({item['name'] for item in assets if not item['expired']}): return
        time.sleep(5)
    raise RuntimeError('Timed out waiting for three-platform artifacts: ' + ', '.join(names))


def read_json(url):
    with urllib.request.urlopen(url, timeout=5) as response: return json.load(response)


def inspect_summary(receipt, revision, release, directory=None):
    if receipt.get('schemaVersion') != 1 or receipt.get('passed') is not True or receipt.get('revision') != revision or receipt.get('release') != release:
        raise RuntimeError('Three-platform Quick Play receipt does not match the released candidate')
    reports = receipt.get('reports', [])
    if [report.get('target') for report in reports] != list(TARGETS): raise RuntimeError('Three-platform receipt is missing a target OS')
    expected = set(receipt.get('roster', []))
    if len(expected) != 3 or not receipt.get('lobbyId') or type(receipt.get('deadline')) is not int:
        raise RuntimeError('Three-platform receipt is missing its shared public match')
    for report in reports:
        target = report['target']; state = report.get('crossplay', {})
        if (report.get('passed') is not True or report.get('revision') != revision or report.get('release') != release or
                report.get('runtimeErrors') != [] or type(report.get('assertions')) is not int or report['assertions'] <= 0 or not report.get('captures') or
                state.get('lobbyId') != receipt['lobbyId'] or state.get('deadline') != receipt['deadline'] or set(state.get('roster', [])) != expected or
                set(state.get('moved', [])) != expected or set(state.get('bombOwners', [])) != expected or state.get('resetObserved') is not True or
                state.get('hosting') != 'cluster' or state.get('firstGameplayServerTime', 0) < receipt['deadline'] or
                state.get('snapshots', 0) <= 4 or state.get('lastServerTime', 0) - state.get('firstServerTime', 0) < 5000 or
                state.get('driver') != {'windows-x64': 'd3d12', 'linux-x64': 'vulkan', 'macos-arm64': 'metal'}[target] or
                state.get('method') != 'forward_plus' or not state.get('adapter')):
            raise RuntimeError('Incomplete three-platform gameplay or renderer witness: ' + target)
        if directory is not None:
            import desktop_packages as desktop
            import download_formats
            if report.get('downloadSha256') != desktop.digest(directory / download_formats.FILES[target]):
                raise RuntimeError('Crossplay did not run the exact player download: ' + target)
    if not reports[1]['crossplay'].get('lateExcluded'): raise RuntimeError('Crossplay did not reject a late stranger')
    return receipt


def stop_child(process):
    if process.poll() is None:
        process.terminate()
        try: process.wait(timeout=5)
        except subprocess.TimeoutExpired: process.kill(); process.wait()


def launch(command, output):
    output.mkdir(parents=True, exist_ok=True)
    with (output / 'launcher.log').open('w') as log:
        process = subprocess.Popen(command, cwd=ROOT, env=clean_environment(), stdout=log,
                                   stderr=subprocess.STDOUT, start_new_session=os.name == 'posix')
    (output / 'launcher.json').write_text(json.dumps({'pid': process.pid, 'marker': str(output)}))
    return process


def stop_launcher(output):
    path = output / 'launcher.json'
    if not path.exists(): return
    state = json.loads(path.read_text()); pid = int(state['pid'])
    if os.name == 'posix':
        result = subprocess.run(['ps', '-p', str(pid), '-o', 'stat=,command='], capture_output=True, text=True)
        if result.returncode != 0: return
        if result.stdout.lstrip().startswith('Z'): return
        if state['marker'] not in result.stdout or os.getpgid(pid) != pid:
            raise RuntimeError('Refusing to stop a process outside this owned crossplay session')
        os.killpg(pid, signal.SIGTERM)
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            try: os.killpg(pid, 0)
            except ProcessLookupError: return
            time.sleep(.1)
        try: os.killpg(pid, signal.SIGKILL)
        except ProcessLookupError: pass
    else:
        command = subprocess.check_output(['powershell', '-NoProfile', '-Command',
            f'(Get-CimInstance Win32_Process -Filter "ProcessId = {pid}").CommandLine'], text=True)
        if not command.strip(): return
        if state['marker'] not in command: raise RuntimeError('Refusing to stop an unrelated Windows process')
        subprocess.run(['taskkill', '/PID', str(pid), '/T', '/F'], check=True, capture_output=True)


def server_serve(directory, output):
    def cancelled(*_): raise KeyboardInterrupt('Crossplay staging cancelled')
    signal.signal(signal.SIGTERM, cancelled)
    if platform.system() != 'Linux': raise RuntimeError('Staging coordinator runs only on the disposable Linux runner')
    from promote_release import inspect_server
    from desktop_packages import digest
    metadata = json.loads((directory / 'server-build-metadata.json').read_text())
    inspect_server(directory, metadata['revision'], metadata['version'], release=metadata['release'])
    staged = output / 'server'
    with zipfile.ZipFile(directory / 'Blast-Circuit-server-dotnet10.zip') as archive: archive.extractall(staged)
    binary = output / 'cloudflared'
    urllib.request.urlretrieve(CLOUDFLARED, binary)
    if digest(binary) != CLOUDFLARED_SHA: raise RuntimeError('Staging tunnel binary checksum mismatch')
    binary.chmod(0o755)
    server = tunnel = None
    started = time.monotonic()
    try:
        with (output / 'server.log').open('w') as log:
            server = subprocess.Popen(['dotnet', str(staged / 'Blast-Circuit-server-dotnet10/Bomber.Server.dll'), '--urls', 'http://127.0.0.1:0'],
                cwd=output, env=clean_environment(), stdout=log, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 30
        local = None
        while time.monotonic() < deadline:
            if server.poll() is not None: raise RuntimeError('Owned staging server exited')
            match = re.search(r'Now listening on:\s+(http://127\.0\.0\.1:\d+)', (output / 'server.log').read_text(errors='replace'))
            if match:
                local = match[1]; break
            time.sleep(.1)
        if not local: raise RuntimeError('Owned staging server did not become ready')
        with (output / 'tunnel.log').open('w') as log:
            tunnel = subprocess.Popen([str(binary), 'tunnel', '--no-autoupdate', '--protocol', 'http2', '--url', local],
                env=clean_environment(), stdout=log, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 90
        origin = None
        while time.monotonic() < deadline:
            if tunnel.poll() is not None: raise RuntimeError('Disposable staging tunnel exited')
            match = re.search(r'https://[a-z0-9-]+\.trycloudflare\.com', (output / 'tunnel.log').read_text(errors='replace'))
            if match:
                try:
                    identity = read_json(match[0] + '/game')['release']
                    if identity != dict(metadata['release'], revision=metadata['revision']): raise RuntimeError('Staging origin identity differs from candidate')
                    origin = match[0]; break
                except (OSError, ValueError): pass
            time.sleep(.5)
        if not origin: raise RuntimeError('Disposable HTTPS staging origin did not become ready')
        (output / 'origin.json').write_text(json.dumps({'schemaVersion': 1, 'origin': origin, 'revision': metadata['revision'],
            'release': metadata['release'], 'serverArchiveSha256': digest(directory / 'Blast-Circuit-server-dotnet10.zip')}, indent=2) + '\n')
        while not (output / 'stop').exists() and time.monotonic() - started < 1200:
            if server.poll() is not None or tunnel.poll() is not None: raise RuntimeError('Owned staging service stopped during crossplay')
            time.sleep(.2)
    finally:
        for process in (tunnel, server):
            if process is not None: stop_child(process)
        (output / 'server-summary.json').write_text(json.dumps({'schemaVersion': 1, 'stopped': True,
            'revision': metadata['revision'], 'release': metadata['release'], 'seconds': time.monotonic() - started}) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=('validate-run', 'wait', 'server-start', 'server-serve', 'server-stop', 'aggregate'))
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/crossplay-server')
    parser.add_argument('--names', nargs='+', default=[])
    parser.add_argument('--timeout', type=int, default=900)
    parser.add_argument('--run-id')
    args = parser.parse_args(); directory = args.directory.resolve(); output = args.output.resolve()
    if args.operation == 'validate-run':
        if not args.run_id or not args.run_id.isdigit(): raise RuntimeError('A successful exact-source native candidate run is required')
        run = json.loads(github('run', 'view', args.run_id, '--json', 'headSha,conclusion'))
        if run != {'conclusion': 'success', 'headSha': os.environ['CANDIDATE_SHA']}:
            raise RuntimeError('Three-platform test input does not match a successful native candidate run')
    elif args.operation == 'wait': wait_artifacts(args.names, args.timeout)
    elif args.operation == 'server-start':
        process = launch([sys.executable, str(Path(__file__).resolve()), 'server-serve', '--directory', str(directory), '--output', str(output)], output)
        deadline = time.monotonic() + 150
        while not (output / 'origin.json').exists():
            if process.poll() is not None or time.monotonic() > deadline: raise RuntimeError('Staging server failed; inspect launcher/server/tunnel logs')
            time.sleep(.2)
    elif args.operation == 'server-serve': server_serve(directory, output)
    elif args.operation == 'server-stop':
        if output.exists():
            (output / 'stop').touch()
            deadline = time.monotonic() + 15
            while not (output / 'server-summary.json').exists() and time.monotonic() < deadline: time.sleep(.1)
            if not (output / 'server-summary.json').exists(): stop_launcher(output)
    else:
        reports = [json.loads((directory / target / 'crossplay-receipt.json').read_text()) for target in TARGETS]
        states = [report['crossplay'] for report in reports]
        if any(report.get('passed') is not True for report in reports): raise RuntimeError('A target client failed crossplay')
        if ([report.get('target') for report in reports] != list(TARGETS) or
                any(report.get('release') != reports[0]['release'] or report.get('revision') != reports[0]['revision'] for report in reports)):
            raise RuntimeError('Crossplay clients have mismatched platform or release identities')
        for field in ('lobbyId', 'deadline', 'roster'):
            if any(state[field] != states[0][field] for state in states[1:]): raise RuntimeError('Three platform clients disagree on ' + field)
        expected = set(states[0]['roster'])
        if len(expected) != 3 or any(set(state['moved']) != expected or set(state['bombOwners']) != expected or not state['resetObserved'] for state in states):
            raise RuntimeError('Missing cross-platform accepted movement/bomb/countdown witnesses')
        if not next(state for state in states if state['role'] == 'linux-x64')['lateExcluded']:
            raise RuntimeError('Mixed-platform public gameplay admitted a late stranger')
        receipt = {'schemaVersion': 1, 'passed': True, 'targets': TARGETS,
            'release': reports[0]['release'], 'revision': reports[0]['revision'], 'lobbyId': states[0]['lobbyId'],
            'deadline': states[0]['deadline'], 'roster': states[0]['roster'], 'reports': reports}
        inspect_summary(receipt, receipt['revision'], receipt['release'])
        (directory / RECEIPT).write_text(json.dumps(receipt, indent=2) + '\n')
        print('PASS real Windows/Linux/Mac Quick Play: roster reset, real minute, shared match and accepted movement/bombs')


if __name__ == '__main__': main()

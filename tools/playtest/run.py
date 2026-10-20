#!/usr/bin/env python3
"""Launch real Godot gameplay and retain machine-readable failure evidence."""
import argparse
import datetime
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[2]


def healthy(origin):
    try:
        with urllib.request.urlopen(origin.rstrip('/') + '/realtime/v1/games', timeout=1) as response:
            return any(g['id'] == 'blast' for g in json.load(response)['value'])
    except Exception:
        return False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--scenario', choices=['all', 'menu', 'local-actions', 'ramp', 'network', 'combat', 'results', 'script'], default='all')
    parser.add_argument('--script', type=Path, help='JSON scenario, see temple-walk.json')
    parser.add_argument('--headless', action='store_true', help='Explicitly disable screenshot capture')
    parser.add_argument('--godot', default=os.environ.get('GODOT', '/Applications/Godot_mono.app/Contents/MacOS/Godot'))
    parser.add_argument('--server', default='http://127.0.0.1:18496')
    parser.add_argument('--start-server', action='store_true', help='Start local server if unreachable; stop only the process this runner owns')
    parser.add_argument('--build', action='store_true', help='Build Godot and server projects first')
    parser.add_argument('--timeout', type=float, default=120)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts' / 'playtest')
    args = parser.parse_args()
    if args.scenario == 'script' and not args.script:
        parser.error('--scenario script requires --script')
    run_id = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ') + '-' + args.scenario
    folder = args.output.resolve() / run_id
    folder.mkdir(parents=True)
    owned_server = None
    runtime = None
    server_log = None
    started = time.monotonic()
    result = {'passed': False, 'scenario': args.scenario, 'headless': args.headless, 'folder': str(folder),
              'platform': platform.platform(), 'errors': [], 'serverOrigin': args.server}
    try:
        if args.build:
            with (folder / 'build.log').open('w') as build_log:
                for project in ['Bomberman64Multiplayer.csproj', 'src/Bomber.Server/Bomber.Server.csproj']:
                    subprocess.run(['dotnet', 'build', project, '--nologo', '-m:1'], cwd=ROOT, stdout=build_log, stderr=subprocess.STDOUT, check=True, timeout=90)
        needs_network = args.scenario in ['all', 'network']
        if needs_network and not healthy(args.server):
            if not args.start_server:
                raise RuntimeError('Authoritative server is unavailable; start scripts/server.sh or use --start-server')
            server_log = (folder / 'server.log').open('w')
            owned_server = subprocess.Popen(['dotnet', 'src/Bomber.Server/bin/Debug/net10.0/Bomber.Server.dll', '--urls', args.server], cwd=ROOT, stdout=server_log, stderr=subprocess.STDOUT)
            deadline = time.monotonic() + 15
            while not healthy(args.server):
                if owned_server.poll() is not None or time.monotonic() > deadline:
                    raise RuntimeError('Server startup failed; inspect server.log')
                time.sleep(.1)
        command = [args.godot, '--path', str(ROOT), '--max-fps', '30', '--fixed-fps', '30', '--disable-vsync']
        if args.headless:
            command.append('--headless')
        command += ['--', '--playtest=' + args.scenario, '--playtest-output=' + str(folder), '--playtest-server=' + args.server]
        if args.script:
            command.append('--playtest-file=' + str(args.script.resolve()))
        result['command'] = command
        with (folder / 'runtime.log').open('w') as log:
            runtime = subprocess.Popen(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
            runtime.wait(timeout=args.timeout)
        result['exitCode'] = runtime.returncode
        summary_path = folder / 'summary.json'
        if not summary_path.exists():
            raise RuntimeError('Godot did not write summary.json; inspect runtime.log for compile/launch errors')
        runtime_text = (folder / 'runtime.log').read_text(errors='replace')
        runtime_errors = [line for line in runtime_text.splitlines() if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|Unhandled Exception|FATAL:)', line)]
        result['runtimeErrorCount'] = len(runtime_errors)
        runtime_errors = list(dict.fromkeys(runtime_errors))
        result['runtimeErrors'] = runtime_errors
        summary = json.loads(summary_path.read_text())
        result['assertions'] = summary['assertions']
        result['screenshots'] = len(summary['screenshots'])
        result['passed'] = runtime.returncode == 0 and summary['passed'] and summary['assertions'] > 0 and not runtime_errors
        if runtime_errors:
            result['errors'].append('Runtime log contains engine/script errors; inspect runtimeErrors and runtime.log')
        if not args.headless and not summary['screenshots']:
            result['passed'] = False
            result['errors'].append('Graphical scenario produced no screenshots')
        if not result['passed']:
            result['errors'].append(summary.get('failure') or 'One or more required runtime, screenshot or assertion checks failed')
    except Exception as error:
        result['errors'].append(str(error))
    finally:
        if runtime and runtime.poll() is None:
            runtime.terminate()
            try:
                runtime.wait(timeout=5)
            except subprocess.TimeoutExpired:
                runtime.kill()
                runtime.wait()
        if owned_server:
            owned_server.terminate()
            try:
                owned_server.wait(timeout=5)
            except subprocess.TimeoutExpired:
                owned_server.kill()
                owned_server.wait()
        if server_log:
            server_log.close()
        result['seconds'] = round(time.monotonic() - started, 3)
        (folder / 'runner-summary.json').write_text(json.dumps(result, indent=2) + '\n')
        print(json.dumps(result, indent=2))
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())

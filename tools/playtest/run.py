#!/usr/bin/env python3
"""Launch real Godot gameplay and retain machine-readable failure evidence."""
import argparse
import shutil
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
import hashlib
import signal
import tempfile
from contracts import catalog, expanded, validate_summary, junit

ROOT = Path(__file__).resolve().parents[2]


def stop(process):
    """Reap only the process group launched by this harness."""
    if process.poll() is not None and os.name != 'posix':
        return
    for force in (False, True):
        try:
            if os.name == 'posix':
                os.killpg(process.pid, signal.SIGKILL if force else signal.SIGTERM)
            else:
                process.kill() if force else process.terminate()
            process.wait(timeout=5)
            if os.name == 'posix':
                # The direct child may exit before its descendants. Reap its
                # remaining dedicated group, too (never a shared shell group).
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            return
        except ProcessLookupError:
            process.wait()
            return
        except subprocess.TimeoutExpired:
            continue
    process.wait()



def healthy(origin):
    try:
        with urllib.request.urlopen(origin.rstrip('/') + '/realtime/v1/games', timeout=1) as response:
            return any(g['id'] == 'blast' for g in json.load(response)['value'])
    except Exception:
        return False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    scenarios = catalog()
    parser.add_argument('--scenario', choices=list(scenarios), default='all')
    parser.add_argument('--list', action='store_true', help='Print scenario metadata without launching Godot')
    parser.add_argument('--ffmpeg', default=os.environ.get('FFMPEG', 'ffmpeg'), help='FFmpeg executable for --record export and decode validation')
    parser.add_argument('--script', type=Path, help='JSON scenario, see temple-walk.json')
    parser.add_argument('--record', action='store_true', help='Record actual gameplay at 30 FPS and export a validated MP4; requires ffmpeg')
    parser.add_argument('--headless', action='store_true', help='Explicitly disable screenshot capture')
    display = parser.add_mutually_exclusive_group()
    display.add_argument('--windowed', action='store_true', help='Use windowed testing (the default)')
    display.add_argument('--fullscreen', action='store_true', help='Explicitly test fullscreen; normal QA stays windowed')
    parser.add_argument('--godot', default=os.environ.get('GODOT', '/Applications/Godot_mono.app/Contents/MacOS/Godot'))
    parser.add_argument('--app', type=Path, help='Test an exported macOS .app independently of the source project')
    parser.add_argument('--server', default='http://127.0.0.1:18496')
    parser.add_argument('--start-server', action='store_true', help='Start local server if unreachable; stop only the process this runner owns')
    parser.add_argument('--build', action='store_true', help='Build Godot and server projects first')
    parser.add_argument('--timeout', type=float, help='Override the catalog timeout (full suites default to 600 seconds)')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts' / 'playtest')
    args = parser.parse_args()
    if args.list:
        print(json.dumps({'schemaVersion': 1, 'scenarios': list(scenarios.values())}, indent=2))
        return 0
    if args.script and args.scenario != 'script':
        parser.error('--script must use --scenario script; it cannot stand in for a full suite')
    if args.headless and not scenarios[args.scenario]['headless']:
        parser.error('Scenario requires graphics; use core-headless for headless gameplay coverage')
    args.timeout = args.timeout if args.timeout is not None else scenarios[args.scenario]['timeout']
    if args.timeout <= 0:
        parser.error('--timeout must be positive')
    if args.scenario == 'script' and not args.script:
        parser.error('--scenario script requires --script')
    if args.scenario == 'fullscreen' and (args.windowed or args.headless):
        parser.error('--scenario fullscreen requires a graphical fullscreen launch')
    if args.record and args.headless:
        parser.error('--record requires graphical rendering')
    if args.record and not shutil.which(args.ffmpeg):
        parser.error('--record requires ffmpeg; set --ffmpeg or FFMPEG')
    windowed = args.headless or not (args.fullscreen or args.scenario == 'fullscreen')
    run_id = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ') + '-' + args.scenario
    folder = args.output.resolve() / run_id
    folder.mkdir(parents=True)
    owned_server = None
    runtime = None
    server_log = None
    graphical_lock = None
    summary = None
    started = time.monotonic()
    result = {'passed': False, 'scenario': args.scenario, 'headless': args.headless, 'folder': str(folder),
              'schemaVersion': 1, 'platform': platform.platform(), 'errors': [], 'serverOrigin': args.server,
              'domains': scenarios[args.scenario]['domains'], 'expandedScenarios': expanded(args.scenario, scenarios),
              'catalogSha256': hashlib.sha256((ROOT / 'assets/testing/scenarios.json').read_bytes()).hexdigest()}
    try:
        revision = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True, capture_output=True)
        result['revision'] = revision.stdout.strip() if revision.returncode == 0 else 'unavailable'
        result['dirtyWorktree'] = subprocess.run(['git', 'diff', '--quiet'], cwd=ROOT).returncode != 0
        if not args.headless and os.name == 'posix':
            import fcntl
            graphical_lock = open(Path(tempfile.gettempdir()) / ('blast-circuit-qa-' + str(os.getuid()) + '.lock'), 'a')
            try:
                fcntl.flock(graphical_lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                raise RuntimeError('Another graphical playtest owns the desktop. Run graphical scenarios sequentially.')
        if args.build:
            with (folder / 'build.log').open('w') as build_log:
                for project in ['Bomberman64Multiplayer.csproj', 'src/Bomber.Server/Bomber.Server.csproj']:
                    subprocess.run(['dotnet', 'build', project, '--nologo', '-m:1'], cwd=ROOT, stdout=build_log, stderr=subprocess.STDOUT, check=True, timeout=90)
        needs_network = 'network' in expanded(args.scenario, scenarios)
        if needs_network and not healthy(args.server):
            if not args.start_server:
                raise RuntimeError('Authoritative server is unavailable; start scripts/server.sh or use --start-server')
            server_log = (folder / 'server.log').open('w')
            owned_server = subprocess.Popen(['dotnet', 'src/Bomber.Server/bin/Debug/net10.0/Bomber.Server.dll', '--urls', args.server], cwd=ROOT, stdout=server_log, stderr=subprocess.STDOUT, start_new_session=True)
            deadline = time.monotonic() + 15
            while not healthy(args.server):
                if owned_server.poll() is not None or time.monotonic() > deadline:
                    raise RuntimeError('Server startup failed; inspect server.log')
                time.sleep(.1)
        if args.app:
            import plistlib
            app = args.app.resolve()
            with (app / 'Contents/Info.plist').open('rb') as plist:
                executable = plistlib.load(plist)['CFBundleExecutable']
            command = [str(app / 'Contents/MacOS' / executable)]
        else:
            command = [args.godot, '--path', str(ROOT)]
        command += ['--max-fps', '30', '--fixed-fps', '30', '--disable-vsync']
        if args.record:
            command += ['--write-movie', str(folder / 'gameplay.avi')]
        if windowed:
            command.append('--windowed')
        if args.headless:
            command.append('--headless')
        command += ['--', '--playtest=' + args.scenario, '--playtest-output=' + str(folder), '--playtest-server=' + args.server]
        if windowed:
            command.append('--display-windowed')
        if args.script:
            command.append('--playtest-file=' + str(args.script.resolve()))
        result['command'] = command
        with (folder / 'runtime.log').open('w') as log:
            runtime = subprocess.Popen(command, cwd=folder if args.app else ROOT, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
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
        receipt_errors = validate_summary(summary, folder, args.scenario, args.headless)
        result['errors'].extend(receipt_errors)
        result['assertions'] = summary['assertions']
        result['screenshots'] = len(summary['screenshots'])
        result['passed'] = runtime.returncode == 0 and summary['passed'] and summary['assertions'] > 0 and not runtime_errors and not receipt_errors
        if runtime_errors:
            result['errors'].append('Runtime log contains engine/script errors; inspect runtimeErrors and runtime.log')
        if not args.headless and not summary['screenshots']:
            result['passed'] = False
            result['errors'].append('Graphical scenario produced no screenshots')
        if args.record:
            recording = folder / 'gameplay.avi'
            movie = folder / 'gameplay.mp4'
            with (folder / 'video-export.log').open('w') as export_log:
                subprocess.run([args.ffmpeg, '-y', '-i', str(recording), '-c:v', 'libx264', '-preset', 'fast', '-crf', '18', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-movflags', '+faststart', str(movie)], stdout=export_log, stderr=subprocess.STDOUT, check=True, timeout=120)
            decoded = subprocess.run([args.ffmpeg, '-v', 'error', '-i', str(movie), '-map', '0:v:0', '-f', 'null', '-', '-progress', 'pipe:1'], capture_output=True, text=True, check=True, timeout=120)
            progress = dict(line.split('=', 1) for line in decoded.stdout.splitlines() if '=' in line)
            if int(progress.get('frame', '0')) <= 0 or int(progress.get('out_time_us', '0')) <= 0 or decoded.stderr.strip():
                raise RuntimeError('Recording failed full decode validation: ' + decoded.stderr)
            result['recording'] = {'path': str(movie), 'source': str(recording), 'playbackSpeed': 1, 'fps': 30, 'decodedFrames': int(progress['frame']), 'seconds': int(progress['out_time_us']) / 1e6}
            (folder / 'recording.json').write_text(json.dumps(result['recording'], indent=2) + '\n')
        if not result['passed']:
            result['errors'].append(summary.get('failure') or 'One or more required runtime, screenshot or assertion checks failed')
    except Exception as error:
        result['passed'] = False
        result['errors'].append(str(error))
    finally:
        for process in (runtime, owned_server):
            if process:
                try:
                    stop(process)
                except Exception as error:
                    result['passed'] = False
                    result['errors'].append('Owned process cleanup failed: ' + str(error))
        if server_log:
            server_log.close()
        if graphical_lock:
            graphical_lock.close()
        result['seconds'] = round(time.monotonic() - started, 3)
        (folder / 'runner-summary.json').write_text(json.dumps(result, indent=2) + '\n')
        junit(folder / 'junit.xml', result, summary)
        progress = folder / 'progress.json'
        try:
            state = json.loads(progress.read_text()) if progress.exists() else {}
        except (OSError, ValueError):
            state = {'unavailable': 'progress receipt incomplete'}
        (folder / 'triage.md').write_text('# Playtest receipt\n\n' + ('PASS' if result['passed'] else 'FAIL') + ': ' + args.scenario + '\n\n' + '\n'.join(result['errors']) + '\n\nLast progress: `' + json.dumps(state) + '`\n')
        print(json.dumps(result, indent=2))
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())

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
import contextlib
from contracts import catalog, expanded, validate_summary, validate_benchmark, junit

ROOT = Path(__file__).resolve().parents[2]


def engine_timing_args(scenario):
    if scenario == 'graphics-benchmark':
        return ['--max-fps', '0', '--disable-vsync']
    return ['--max-fps', '30', '--fixed-fps', '30', '--disable-vsync']


def native_benchmark_error(scenario, desktop, headless, record):
    if scenario == 'graphics-benchmark' and (desktop != 'shared' or headless or record):
        return 'graphics-benchmark requires --desktop shared, real graphics and no --record; VM/fixed-step footage cannot measure native GPU throughput'
    return None


def native_power_evidence():
    if platform.system() != 'Darwin':
        return {'available': False, 'reason': 'No platform power-state collector'}
    evidence = {}
    for query in ('batt', 'custom', 'therm'):
        try:
            result = subprocess.run(['pmset', '-g', query], text=True, capture_output=True, timeout=5)
            evidence[query] = {'exitCode': result.returncode, 'output': re.sub(r'\(id=\d+\)', '', result.stdout.strip())}
        except (OSError, subprocess.TimeoutExpired) as error:
            evidence[query] = {'unavailable': str(error)}
    return evidence


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
                except (ProcessLookupError, PermissionError):
                    # macOS may retain a protected zombie group briefly after
                    # its owned leader was reaped; primary errors still fail.
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
    # QA/agent cancellation must unwind the VM and owned processes as well as Ctrl-C.
    def cancelled(signum, frame):
        raise KeyboardInterrupt('Playtest cancelled by signal ' + str(signum))
    signal.signal(signal.SIGTERM, cancelled)
    parser = argparse.ArgumentParser(description=__doc__)
    scenarios = catalog()
    parser.add_argument('--scenario', choices=list(scenarios), default='all')
    parser.add_argument('--list', action='store_true', help='Print scenario metadata without launching Godot')
    parser.add_argument('--ffmpeg', default=os.environ.get('FFMPEG', 'ffmpeg'), help='FFmpeg executable for --record export and decode validation')
    parser.add_argument('--script', type=Path, help='JSON scenario, see temple-walk.json')
    parser.add_argument('--record', action='store_true', help='Record actual gameplay at 30 FPS and export a validated MP4; requires ffmpeg')
    parser.add_argument('--metal-hud', action='store_true', help='Collect Apple GPU timing logs for the native macOS graphics-benchmark')
    parser.add_argument('--cold-effects', action='store_true', help='Disable startup warmup in first-use-rendering to retain a negative regression witness')
    parser.add_argument('--desktop', choices=('isolated', 'shared'), default='isolated' if platform.system() == 'Darwin' else 'shared', help='Graphical macOS QA uses a private VM by default; shared explicitly uses your desktop')
    parser.add_argument('--vm', default=os.environ.get('BLAST_QA_VM', 'blast-circuit-qa'), help='Stopped dedicated Tart VM for isolated graphical QA')
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
    if args.cold_effects and args.scenario != 'first-use-rendering':
        parser.error('--cold-effects is only a negative first-use-rendering diagnostic')
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
    benchmark_error = native_benchmark_error(args.scenario, args.desktop, args.headless, args.record)
    if benchmark_error:
        parser.error(benchmark_error)
    if args.metal_hud and (args.scenario != 'graphics-benchmark' or platform.system() != 'Darwin'):
        parser.error('--metal-hud requires graphics-benchmark on native macOS')
    if (scenarios[args.scenario].get('manual') or args.scenario == 'controller-inventory') and args.desktop == 'isolated' and not args.headless:
        parser.error('Physical controller inventory/observation requires --desktop shared on the device test machine; VM devices do not prove host controller support')
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
        if not args.headless and args.desktop == 'isolated':
            from isolated import run as run_isolated
            run_isolated(args, folder, result, 'network' in scenarios[args.scenario]['domains'], windowed)
        else:
            result['desktop'] = {'mode': 'headless' if args.headless else 'shared'}
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
            needs_network = 'network' in scenarios[args.scenario]['domains']
            if needs_network and not healthy(args.server):
                if not args.start_server:
                    raise RuntimeError('Authoritative server is unavailable; start tools/dev/server.sh or use --start-server')
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
                if args.scenario == 'graphics-benchmark':
                    from isolated import inventory
                    (folder / 'tested-app-files.json').write_text(json.dumps(inventory(app), indent=2) + '\n')
            else:
                command = [args.godot, '--path', str(ROOT)]
            result['runtimeSource'] = 'app-bundle' if args.app else 'source-project'
            command += engine_timing_args(args.scenario)
            result['timingMode'] = 'realtime-uncapped' if args.scenario == 'graphics-benchmark' else 'fixed-step-30'
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
            if args.cold_effects:
                command.append('--playtest-cold-effects')
            result['command'] = command
            if args.scenario == 'graphics-benchmark':
                result['powerBefore'] = native_power_evidence()
            with (folder / 'runtime.log').open('w') as log:
                runtime_env = os.environ.copy()
                if args.metal_hud:
                    runtime_env.update(MTL_HUD_ENABLED='1', MTL_HUD_LOG_ENABLED='1', MTL_HUD_OPACITY='0')
                runtime = subprocess.Popen(command, cwd=folder if args.app else ROOT, stdout=log, stderr=subprocess.STDOUT, start_new_session=True, env=runtime_env)
                from metal_metrics import MetalHudCapture
                with MetalHudCapture(folder, runtime.pid) if args.metal_hud else contextlib.nullcontext():
                    runtime.wait(timeout=args.timeout)
            result['exitCode'] = runtime.returncode
            if args.scenario == 'graphics-benchmark':
                result['powerAfter'] = native_power_evidence()
        summary_path = folder / 'summary.json'
        if not summary_path.exists():
            raise RuntimeError('Godot did not write summary.json; inspect runtime.log for compile/launch errors')
        runtime_text = (folder / 'runtime.log').read_text(errors='replace')
        runtime_errors = [line for line in runtime_text.splitlines() if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|Unhandled Exception|FATAL:)', line)]
        result['runtimeErrorCount'] = len(runtime_errors)
        runtime_errors = list(dict.fromkeys(runtime_errors))
        result['runtimeErrors'] = runtime_errors
        result['runtimeWarnings'] = list(dict.fromkeys(
            line for line in runtime_text.splitlines() if re.search(r'(^|\s)WARNING:', line)))
        result['coverageWarnings'] = [line for line in result['runtimeWarnings']
                                      if 'Optional Switch 2 USB backend:' in line]
        result['renderer'] = next((line for line in runtime_text.splitlines()
                                   if 'Using Device #' in line), None)
        if args.scenario == 'graphics-benchmark':
            benchmark_path = folder / 'graphics-benchmark.json'
            result['benchmark'] = json.loads(benchmark_path.read_text()) if benchmark_path.is_file() else {}
        summary = json.loads(summary_path.read_text())
        receipt_errors = validate_summary(summary, folder, args.scenario, args.headless)
        if args.scenario == 'graphics-benchmark':
            receipt_errors.extend(validate_benchmark(result['benchmark']) if result['benchmark'] else ['Missing native benchmark timing receipt'])
            result['desktop']['nativeHostGpuCoverage'] = not receipt_errors
            if args.metal_hud and result['benchmark']:
                from metal_metrics import summarize
                result['metalHud'] = summarize(folder / 'metal-hud.ndjson', runtime.pid, result['benchmark'], folder / 'runtime.log')
                (folder / 'metal-gpu-summary.json').write_text(json.dumps(result['metalHud'], indent=2) + '\n')
        if not args.headless and args.desktop == 'isolated' and not re.match(r'^Metal .*Forward\+', result['renderer'] or ''):
            receipt_errors.append('Isolated guest did not use the project\'s Metal Forward+ renderer; inspect runtime.log')
        result['errors'].extend(receipt_errors)
        result['assertions'] = summary['assertions']
        result['screenshots'] = len(summary['screenshots'])
        result['passed'] = result['exitCode'] == 0 and summary['passed'] and summary['assertions'] > 0 and not runtime_errors and not receipt_errors
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
    except (Exception, KeyboardInterrupt) as error:
        result['passed'] = False
        result['errors'].append(str(error) or type(error).__name__)
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

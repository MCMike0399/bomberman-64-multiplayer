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
from contracts import catalog, expanded, validate_summary, validate_benchmark, junit, runtime_errors_with_allowlist

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
    parser.add_argument('--gpu-profile', action='store_true', help='Print engine GPU pass timings during the native benchmark (requires working engine timestamps)')
    parser.add_argument('--benchmark-resolution', default='1920x1200', help='Native benchmark output pixels, WIDTHxHEIGHT')
    parser.add_argument('--benchmark-stage', choices=('all', 'sunken-temple', 'rock-garden', 'up-and-down', 'top-rules', 'blizzard-battle', 'greedy-trap'), default='sunken-temple')
    parser.add_argument('--benchmark-seconds', type=int, default=10, help='Measured battle duration per preset (10–120 seconds)')
    parser.add_argument('--benchmark-preset', choices=('all', 'low', 'medium', 'high'), default='all')
    parser.add_argument('--benchmark-tweak', default='none', help='Diagnostic comma-separated renderer tweaks: static A/B captures per stage plus a timed battle per tweak; ordinary measurements stay untouched')
    parser.add_argument('--benchmark-disable', choices=('none', 'ssil', 'ssao', 'dof', 'glow', 'water', 'shadows'), default='none', help='Diagnostic pass omission; never a full-quality performance result')
    parser.add_argument('--cold-effects', action='store_true', help='Disable startup warmup in first-use-rendering to retain a negative regression witness')
    parser.add_argument('--qa-windows-settings-rows', action='store_true', help='Playtest-only: show the Windows renderer rows on macOS/Linux to audit the Windows Settings layout')
    parser.add_argument('--desktop', choices=('isolated', 'shared'), default='isolated' if platform.system() == 'Darwin' else 'shared', help='Graphical macOS QA uses a private VM by default; shared explicitly uses your desktop')
    parser.add_argument('--vm', default=os.environ.get('BLAST_QA_VM', 'blast-circuit-qa'), help='Stopped dedicated Tart VM for isolated graphical QA')
    parser.add_argument('--headless', action='store_true', help='Explicitly disable screenshot capture')
    display = parser.add_mutually_exclusive_group()
    display.add_argument('--windowed', action='store_true', help='Use windowed testing (the default)')
    display.add_argument('--fullscreen', action='store_true', help='Explicitly test fullscreen; normal QA stays windowed')
    parser.add_argument('--godot', default=os.environ.get('GODOT', '/Applications/Godot_mono.app/Contents/MacOS/Godot'))
    parser.add_argument('--app', type=Path, help='Test an exported macOS .app independently of the source project')
    parser.add_argument('--executable', type=Path, help='Test an exported Windows/Linux executable independently of the source project')
    parser.add_argument('--rendering-driver', choices=('d3d12', 'vulkan', 'metal'), help='Require a rendering driver for a graphical exported-client check')
    parser.add_argument('--server', default='http://127.0.0.1:18496')
    parser.add_argument('--start-server', action='store_true', help='Start local server if unreachable; stop only the process this runner owns')
    parser.add_argument('--build', action='store_true', help='Build Godot and server projects first')
    parser.add_argument('--frame-stall-ms', type=int, default=0, help='QA diagnostic: block the engine thread this long each frame (0-5000); never release evidence')
    parser.add_argument('--timeout', type=float, help='Override the catalog timeout (full suites default to 600 seconds)')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts' / 'playtest')
    args = parser.parse_args()
    if not re.fullmatch(r'[1-9][0-9]{2,3}x[1-9][0-9]{2,3}', args.benchmark_resolution) or any(int(n) > 7680 for n in args.benchmark_resolution.split('x')):
        parser.error('--benchmark-resolution must be WIDTHxHEIGHT, between 100 and 7680 pixels per dimension')
    if not 0 <= args.frame_stall_ms <= 5000:
        parser.error('--frame-stall-ms must be between 0 and 5000')
    if not 10 <= args.benchmark_seconds <= 120:
        parser.error('--benchmark-seconds must be between 10 and 120')
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
    if args.scenario == 'graphics-benchmark':
        stages = 6 if args.benchmark_stage == 'all' else 1
        presets = 3 if args.benchmark_preset == 'all' else 1
        tweaks = 0 if args.benchmark_tweak == 'none' else len([t for t in args.benchmark_tweak.split(',') if t])
        args.timeout = max(args.timeout, 45 + presets * (15 + stages * (args.benchmark_seconds + 15 + tweaks * (args.benchmark_seconds + 8) + (25 if tweaks else 0))))
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
    # Packaged Linux games: memory for every run, kernel/core-dump evidence for an abnormal exit.
    linux_capture = platform.system() == 'Linux' and bool(args.executable)
    crash_state = None
    server_log = None
    graphical_lock = None
    gpu_guard = contextlib.ExitStack()
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
            if args.scenario == 'graphics-benchmark' and platform.system() == 'Darwin':
                from isolated import native_gpu_lease
                result['desktop']['gpuIsolation'] = gpu_guard.enter_context(native_gpu_lease(args.vm))
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
            if args.app and args.executable:
                raise RuntimeError('Choose --app or --executable')
            if args.executable:
                if not args.executable.is_file():
                    raise RuntimeError('Exported executable does not exist')
                command = [str(args.executable.resolve())]
            elif args.app:
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
            result['runtimeSource'] = 'exported-executable' if args.executable else 'app-bundle' if args.app else 'source-project'
            if args.rendering_driver:
                command += ['--rendering-driver', args.rendering_driver]
            command += engine_timing_args(args.scenario)
            result['timingMode'] = 'realtime-uncapped' if args.scenario == 'graphics-benchmark' else 'fixed-step-30'
            if args.record:
                command += ['--write-movie', str(folder / 'gameplay.avi')]
            if windowed:
                command.append('--windowed')
            if args.headless:
                command.append('--headless')
            if args.gpu_profile:
                command.append('--gpu-profile')
            command += ['--', '--playtest=' + args.scenario, '--playtest-output=' + str(folder), '--playtest-server=' + args.server]
            if args.frame_stall_ms:
                command.append(f'--playtest-frame-stall-ms={args.frame_stall_ms}')
            if windowed:
                command.append('--display-windowed')
            if args.script:
                command.append('--playtest-file=' + str(args.script.resolve()))
            if args.cold_effects:
                command.append('--playtest-cold-effects')
            if args.qa_windows_settings_rows:
                # Renderer changes go to a scratch launcher state, never the real one.
                command += ['--qa-windows-settings-rows', '--launcher-state-dir=' + str(folder / 'launcher-state')]
            if args.scenario == 'graphics-benchmark':
                command += ['--benchmark-resolution=' + args.benchmark_resolution,
                            '--benchmark-stage=' + args.benchmark_stage,
                            '--benchmark-preset=' + args.benchmark_preset,
                            '--benchmark-disable=' + args.benchmark_disable,
                            '--benchmark-tweak=' + args.benchmark_tweak,
                            '--benchmark-seconds=' + str(args.benchmark_seconds)]
            result['command'] = command
            if args.scenario == 'graphics-benchmark':
                result['powerBefore'] = native_power_evidence()
            with (folder / 'runtime.log').open('w') as log:
                runtime_env = os.environ.copy()
                if args.metal_hud:
                    runtime_env.update(MTL_HUD_ENABLED='1', MTL_HUD_LOG_ENABLED='1', MTL_HUD_OPACITY='0')
                if linux_capture:
                    import linux_crash
                    crash_state = linux_crash.before()
                runtime = subprocess.Popen(command, cwd=folder if args.app or args.executable else ROOT, stdout=log, stderr=subprocess.STDOUT, start_new_session=os.name == 'posix', env=runtime_env,
                                           preexec_fn=linux_crash.enable_cores if linux_capture else None)
                from metal_metrics import MetalHudCapture
                with MetalHudCapture(folder, runtime.pid) if args.metal_hud else contextlib.nullcontext():
                    runtime.wait(timeout=args.timeout)
            result['exitCode'] = runtime.returncode
            if runtime.returncode == 0:
                from display_restart import run as restore_display
                parent_summary = json.loads((folder / 'summary.json').read_text())
                restore_display(folder, command, parent_summary, runtime,
                                cwd=folder if args.app or args.executable else ROOT, environment=runtime_env)
            if args.scenario == 'graphics-benchmark':
                result['powerAfter'] = native_power_evidence()
        summary_path = folder / 'summary.json'
        if not summary_path.exists():
            raise RuntimeError('Godot did not write summary.json; inspect runtime.log for compile/launch errors')
        runtime_text = (folder / 'runtime.log').read_text(errors='replace')
        trace_path = folder / 'trace.jsonl'
        runtime_errors, allowlisted = runtime_errors_with_allowlist(
            runtime_text, platform.system(), args.scenario, trace_path.read_text(errors='replace') if trace_path.exists() else '')
        result['allowlistedRuntimeErrors'] = allowlisted
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
        if not args.headless and 'display-settings' in expanded(args.scenario, scenarios):
            from display_restart import inspect as inspect_display_restart
            restart_path = folder / 'display-restart.json'
            restart = json.loads(restart_path.read_text()) if restart_path.is_file() else {}
            try:
                inspect_display_restart(restart, summary)
                receipt_errors.extend(validate_summary(restart['summary'], folder / 'restart', 'display-settings', False))
                result['displayRestart'] = restart
            except (RuntimeError, KeyError) as error:
                receipt_errors.append(str(error))
        if args.scenario == 'graphics-benchmark':
            receipt_errors.extend(validate_benchmark(result['benchmark'], stage=args.benchmark_stage,
                presets=('Low', 'Medium', 'High') if args.benchmark_preset == 'all' else (args.benchmark_preset.title(),),
                resolution=tuple(map(int, args.benchmark_resolution.split('x'))), omission=args.benchmark_disable) if result['benchmark'] else ['Missing native benchmark timing receipt'])
            result['desktop']['nativeHostGpuCoverage'] = not receipt_errors
            if args.metal_hud and result['benchmark']:
                from metal_metrics import summarize
                result['metalHud'] = summarize(folder / 'metal-hud.ndjson', runtime.pid, result['benchmark'], folder / 'runtime.log')
                (folder / 'metal-gpu-summary.json').write_text(json.dumps(result['metalHud'], indent=2) + '\n')
        if not args.headless and args.desktop == 'isolated' and not re.match(r'^Metal .*Forward\+', result['renderer'] or ''):
            receipt_errors.append('Isolated guest did not use the project\'s Metal Forward+ renderer; inspect runtime.log')
        result['errors'].extend(receipt_errors)
        for field in ('renderingDriver','requestedDriver','fallbackReason','renderingMethod','adapter'):
            if field in summary:result[field]=summary[field]
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
        gpu_guard.close()
        if crash_state is not None:
            try:
                import linux_crash
                result['linuxCrashCapture'] = linux_crash.after(folder, crash_state, result.get('exitCode'),
                                                                (folder / 'summary.json').is_file())
            except Exception as error:  # evidence only; never changes the verdict
                result['linuxCrashCapture'] = {'captureError': str(error)}
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

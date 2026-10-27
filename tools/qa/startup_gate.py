#!/usr/bin/env python3
"""Release startup gate: launch an exported app with each saved display setting on a real renderer.

Every case starts the bundle exactly as Launch Services would after an update,
with no engine overrides, from an isolated user-data directory that already
contains saved graphics/display preferences. This is the path v0.35.0 broke:
a suite that starts on Medium and switches to High after layout cannot see it.
"""
import argparse
import datetime
import json
import os
from pathlib import Path
import platform
import plistlib
import re
import shutil
import signal
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
PRESETS = ('low', 'medium', 'high')
# Saved fullscreen is the first-launch default and what most players restore.
CASES = tuple([(preset, display, 'menu') for preset in PRESETS for display in ('fullscreen', 'windowed')]
              + [(preset, 'fullscreen', 'gameplay') for preset in PRESETS])
USER_DATA = Path('Library/Application Support/Godot/app_userdata/Blast Circuit')
SMOKE = re.compile(r'^GODOT_SMOKE_OK (?P<capture>\S+) snapshots=(?P<snapshots>\d+)(?P<settings>[^\r\n]*)$', re.MULTILINE)
SETTINGS = re.compile(r'^ graphics=(?P<graphics>\S+) window=(?P<window>\S+) size=(?P<width>\d+)x(?P<height>\d+) adapter=(?P<adapter>.*)$')
FATAL = re.compile(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|FATAL:)|CrashHandlerException|Program crashed with signal', re.MULTILINE)
SOFTWARE_ADAPTERS = re.compile(r'llvmpipe|swiftshader|software', re.IGNORECASE)
PNG = b'\x89PNG\r\n\x1a\n'


def preferences(preset, display):
    # Automatic update checks stay off so a gate never downloads a published build.
    return ('[player]\n\nautomaticUpdates=false\n\n[video]\n\n'
            f'graphicsPreset="{preset}"\nfullscreen={"true" if display == "fullscreen" else "false"}\n'
            'resolution=Vector2i(0, 0)\n')


def host_model():
    try:
        return subprocess.run(['sysctl', '-n', 'hw.model'], capture_output=True, text=True, timeout=10).stdout.strip()
    except (OSError, subprocess.TimeoutExpired):
        return 'unavailable'


def disposable_desktop(model, environment=os.environ):
    """Hosted CI and Apple Virtualization guests own their desktop; a physical Mac does not."""
    return environment.get('GITHUB_ACTIONS') == 'true' or model.startswith('VirtualMac')


def bundle(app):
    info = plistlib.loads((app / 'Contents/Info.plist').read_bytes())
    executable = app / 'Contents/MacOS' / info['CFBundleExecutable']
    if not executable.is_file():
        raise RuntimeError('App bundle executable is missing')
    return executable, {'version': info.get('CFBundleShortVersionString'), 'build': info.get('CFBundleVersion'),
                        'identifier': info.get('CFBundleIdentifier')}


def tail(text, lines=40):
    return text.splitlines()[-lines:]


def evaluate(case, log, exit_code, capture, legacy=False):
    """Return the verified report for one launch or raise with the first failed requirement."""
    preset, display, kind = case
    if exit_code != 0:
        raise RuntimeError(f'App exited {exit_code} before completing its startup smoke')
    if FATAL.search(log):
        raise RuntimeError('App logged an engine/script error or crash: ' + FATAL.search(log).group(0).strip())
    matches = list(SMOKE.finditer(log))
    if len(matches) != 1 or matches[0]['capture'] != kind:
        raise RuntimeError(f'Expected exactly one GODOT_SMOKE_OK {kind} line')
    if kind != 'menu' and int(matches[0]['snapshots']) <= 0:
        raise RuntimeError('Gameplay smoke observed no authority snapshots')
    if not capture.is_file() or capture.stat().st_size < 1024 or capture.read_bytes()[:8] != PNG:
        raise RuntimeError('No rendered viewport capture in the isolated user-data directory')
    report = {'capture': kind, 'snapshots': int(matches[0]['snapshots'])}
    settings = SETTINGS.match(matches[0]['settings'])
    if settings is None:
        if not legacy:
            raise RuntimeError('Smoke line lacks the applied graphics/window/adapter report')
        report['legacyReport'] = True
        return report
    report.update(graphics=settings['graphics'], window=settings['window'],
                  size=[int(settings['width']), int(settings['height'])], adapter=settings['adapter'].strip())
    if report['graphics'] != preset:
        raise RuntimeError(f'Saved {preset} preset was not applied (reported {report["graphics"]})')
    if report['window'] != display:
        raise RuntimeError(f'Saved {display} display mode was not applied (reported {report["window"]})')
    if min(report['size']) < 320:
        raise RuntimeError('Rendered window is implausibly small')
    if not report['adapter'] or SOFTWARE_ADAPTERS.search(report['adapter']):
        raise RuntimeError('No hardware/paravirtual GPU adapter reported')
    return report


def stop(process):
    for sig in (signal.SIGTERM, signal.SIGKILL):
        try:
            os.killpg(process.pid, sig)
            process.wait(timeout=5)
            return
        except ProcessLookupError:
            return
        except subprocess.TimeoutExpired:
            continue


def run_case(executable, case, output, scratch, timeout, legacy):
    preset, display, kind = case
    name = f'{preset}-{display}-{kind}'
    folder = output / name
    folder.mkdir()
    # Keep the isolated home outside the uploaded QA tree: only receipts and the capture are staged.
    home = scratch / name
    user = home / USER_DATA
    user.mkdir(parents=True)
    (user / 'preferences.cfg').write_text(preferences(preset, display))
    support = str(home / 'Library/Application Support')
    environment = dict(os.environ, HOME=str(home), XDG_DATA_HOME=support, XDG_CONFIG_HOME=support,
                       XDG_CACHE_HOME=str(home / 'Library/Caches'))
    command = [str(executable), '--', '--smoke-menu' if kind == 'menu' else '--smoke', '--smoke-seconds=4']
    record = {'id': name, 'preset': preset, 'display': display, 'capture': kind, 'command': command[1:],
              'timeoutSeconds': timeout, 'passed': False}
    started = time.monotonic()
    log_path = folder / 'app.log'
    process = None
    try:
        with log_path.open('w') as log:
            process = subprocess.Popen(command, cwd=home, env=environment, stdout=log, stderr=subprocess.STDOUT,
                                       start_new_session=True)
            record['exitCode'] = process.wait(timeout=timeout)
        text = log_path.read_text(errors='replace')
        capture = user / 'captures' / (kind + '.png')
        record['report'] = evaluate(case, text, record['exitCode'], capture, legacy)
        shutil.copyfile(capture, folder / (kind + '.png'))
        record['passed'] = True
    except subprocess.TimeoutExpired:
        record['error'] = f'No completed startup smoke within {timeout}s'
    except Exception as error:
        record['error'] = str(error)
    finally:
        if process and process.poll() is None:
            stop(process)
        record['seconds'] = round(time.monotonic() - started, 3)
        if not record['passed'] and log_path.exists():
            record['logTail'] = tail(log_path.read_text(errors='replace'))
    print(f'  {"PASS" if record["passed"] else "FAIL"}: {name} ({record["seconds"]}s)'
          + ('' if record['passed'] else ' - ' + record['error']), flush=True)
    return record


def run_cases(executable, output, scratch, timeout, cold_timeout, legacy):
    # A fresh hosted VM compiles Metal pipelines on its first launch (observed
    # 60–110 seconds). Keep that one cold launch bounded separately; every later
    # case retains the ordinary deadline and every failed attempt remains fatal.
    return [run_case(executable, case, output, scratch, max(timeout, cold_timeout) if index == 0 else timeout, legacy)
            for index, case in enumerate(CASES)]


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--app', type=Path, required=True, help='Exported Blast Circuit.app (prefer one extracted from the release ZIP)')
    parser.add_argument('--output', type=Path, help='Fresh receipt directory (default: artifacts/qa/<time>-startup-gate)')
    parser.add_argument('--timeout', type=int, default=90, help='Seconds per launch, including first-use graphics preparation')
    parser.add_argument('--cold-timeout', type=int, default=180, help='Seconds for the single first launch on a cold Metal renderer')
    parser.add_argument('--host-desktop', action='store_true',
                        help='Explicitly allow fullscreen launches on this physical Mac desktop (preferences stay isolated)')
    parser.add_argument('--accept-legacy-report', action='store_true',
                        help='Diagnose a published build that predates the settings report; never valid as a release gate')
    args = parser.parse_args(argv)
    if platform.system() != 'Darwin':
        parser.error('The startup gate launches the macOS app bundle; run it on macOS')
    model = host_model()
    if not disposable_desktop(model) and not args.host_desktop:
        parser.error('Refusing to take over a physical desktop with fullscreen launches; '
                     'run in CI or an isolated VM, or pass --host-desktop explicitly')
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ')
    output = (args.output or ROOT / 'artifacts/qa' / (stamp + '-startup-gate')).resolve()
    if output.exists() and any(output.iterdir()):
        parser.error('Use a fresh --output directory for every run')
    output.mkdir(parents=True, exist_ok=True)
    app = args.app.resolve()
    executable, identity = bundle(app)
    print(f'Startup gate: v{identity["version"]} build {identity["build"]} on {model}', flush=True)
    started = time.monotonic()
    with tempfile.TemporaryDirectory(prefix='blast-startup-gate-') as scratch:
        cases = run_cases(executable, output, Path(scratch), args.timeout, args.cold_timeout, args.accept_legacy_report)
    passed = all(case['passed'] for case in cases)
    summary = {'schemaVersion': 1, 'gate': 'saved-settings-startup', 'passed': passed,
               'legacyReport': args.accept_legacy_report, 'app': {'path': str(app), **identity},
               'host': {'model': model, 'os': platform.mac_ver()[0], 'ci': os.environ.get('GITHUB_ACTIONS') == 'true'},
               'seconds': round(time.monotonic() - started, 3), 'cases': cases}
    (output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    print(json.dumps({'passed': passed, 'summary': str(output / 'summary.json')}), flush=True)
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(main())

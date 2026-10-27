#!/usr/bin/env python3
"""Canonical local/CI verification profiles with owned processes and durable receipts."""
import argparse
import contextlib
import datetime
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
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
PROFILES = ('public', 'managed', 'headless', 'graphical', 'package')
CONTROLLER_FILES = ('libSDL3.dylib', 'libusb-1.0.dylib', 'SDL-LICENSE.txt', 'libusb-LICENSE.txt')
FOOTERS = {
    'Bomber.Game.Tests': r'^Passed (?P<count>\d+) game integration checks\.$',
    'Bomber.Client.Tests': r'^PASS: (?P<count>\d+) client contract and session checks$',
    'Bomber.Ai.Tests': r'^PASS: (?P<count>\d+) AI behavior, timing, determinism and allocation checks$',
    'Bomber.Audio.Tests': r'^PASS (?P<count>\d+) original PCM cues: [^\r\n]+$',
    'Bomber.Cluster.Tests': r'^ALL CLUSTER PLACEMENT CHECKS PASSED \((?P<count>\d+)\)$',
    'network-smoke': r'^ALL NETWORK SMOKE CHECKS PASSED(?: \([^\r\n]+\))?$',
    'adaptive-network': r'^PASS (?P<count>\d+) adaptive network checks$',
    'fleet-network': r'^PASS (?P<count>\d+) real fleet infrastructure checks$',
    'Bomber.DesktopUpdate.Tests': r'^PASS (?P<count>\d+) desktop updater handoff, replacement and rollback checks$',
    'release-probe': r'^PASS (?P<count>\d+) release connection and cleanup checks$',
    'ai-soak': r'^PASS (?P<count>\d+) gameplay soak scenarios; [^\r\n]+$',
}


def verify_footer(log, pattern):
    matches = list(re.finditer(pattern, Path(log).read_text(errors='replace'), re.MULTILINE))
    if len(matches) != 1:
        raise RuntimeError('Expected exactly one positive test-completion footer; exit zero alone does not prove tests ran')
    evidence = {'completionFooter': matches[0].group(0)}
    count = matches[0].groupdict().get('count')
    if count is not None:
        if int(count) <= 0:
            raise RuntimeError('Test-completion footer reported zero checks')
        evidence['checks'] = int(count)
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
                    # On macOS a fully terminated orphan/zombie group can
                    # report EPERM instead of ESRCH after its leader is reaped.
                    # The direct owned process has already completed above.
                    pass
            return
        except ProcessLookupError:
            process.wait()
            return
        except subprocess.TimeoutExpired:
            continue
    process.wait()


def version(command):
    try:
        result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=15)
        return result.stdout.strip() if result.returncode == 0 else 'unavailable'
    except (OSError, subprocess.TimeoutExpired):
        return 'unavailable'


class Suite:
    def __init__(self, profile, output, godot, desktop=None, vm=None):
        self.profile, self.output, self.godot = profile, output, godot
        self.desktop = desktop or ('isolated' if platform.system() == 'Darwin' else 'shared')
        self.vm = vm or os.environ.get('BLAST_QA_VM', 'blast-circuit-qa')
        self.steps = []
        self._server_count = 0
        self.started = time.monotonic()
        self.provenance = {
            'revision': version(['git', 'rev-parse', 'HEAD']),
            'dirty': bool(version(['git', 'status', '--porcelain'])),
            'python': platform.python_version(), 'platform': platform.platform(),
            'dotnet': version(['dotnet', '--version']),
        }
        if profile in ('headless', 'graphical', 'package'):
            self.provenance['godot'] = version([godot, '--version'])

    def step(self, name, command, timeout=300, validate=None, engine_log=False, footer=None):
        record = {'name': name, 'command': [str(item) for item in command], 'passed': False,
                  'log': str(self.output / f'{len(self.steps):02}-{name}.log')}
        self.steps.append(record)
        started = time.monotonic()
        process = None
        print(f'QA {self.profile}: {name}', flush=True)
        try:
            with open(record['log'], 'w') as log:
                process = subprocess.Popen(record['command'], cwd=ROOT, stdout=log,
                                           stderr=subprocess.STDOUT, start_new_session=os.name == 'posix')
                record['pid'] = process.pid
                record['exitCode'] = process.wait(timeout=timeout)
            if record['exitCode'] != 0:
                raise RuntimeError(f'Command exited {record["exitCode"]}; inspect {record["log"]}')
            if engine_log and re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|FATAL:)', Path(record['log']).read_text(errors='replace')):
                raise RuntimeError('Godot logged an engine/script error')
            if validate:
                record['evidence'] = validate()
            if footer:
                record.setdefault('evidence', {}).update(verify_footer(record['log'], footer))
            record['passed'] = True
        except subprocess.TimeoutExpired:
            record['error'] = f'Timed out after {timeout}s'
            record['timedOut'] = True
        except Exception as error:
            record['error'] = str(error)
        finally:
            if process:
                stop(process)
            record['seconds'] = round(time.monotonic() - started, 3)
        print(f'  {"PASS" if record["passed"] else "FAIL"}: {name}', flush=True)
        return record['passed']

    def require(self, name, command, **kwargs):
        if not self.step(name, command, **kwargs):
            raise RuntimeError(f'Required step failed: {name}')

    def build(self, project, configuration='Debug'):
        name = Path(project).stem if project.endswith('.csproj') else Path(project).name
        self.require('build-' + name, ['dotnet', 'build', project, '-c', configuration, '--nologo', '-m:1'])

    @contextlib.contextmanager
    def server(self, dll=None, simulation_capacity=None):
        if simulation_capacity is not None and (type(simulation_capacity) is not int or not 0 <= simulation_capacity <= 32):
            raise ValueError('simulation capacity must be an integer from 0 to 32')
        self._server_count += 1
        log_path = self.output / ('server.log' if self._server_count == 1 else f'server-{self._server_count}.log')
        process = None
        started = time.monotonic()
        record = {'name': 'owned-server', 'passed': False, 'log': str(log_path)}
        self.steps.append(record)
        try:
            with log_path.open('w') as log:
                # Kestrel chooses a fresh port itself; never probe or reuse a user's server.
                executable = dll or ROOT / 'src/Bomber.Server/bin/Debug/net10.0/Bomber.Server.dll'
                record['assembly'] = str(executable)
                overrides = [] if simulation_capacity is None else [f'--Realtime:Resources:simulation={simulation_capacity}']
                record['simulationCapacityOverride'] = simulation_capacity
                process = subprocess.Popen(['dotnet', str(executable), *overrides,
                                            '--urls', 'http://127.0.0.1:0'], cwd=ROOT, stdout=log,
                                           stderr=subprocess.STDOUT, start_new_session=os.name == 'posix')
                deadline = time.monotonic() + 30
                origin = None
                while time.monotonic() < deadline:
                    if process.poll() is not None:
                        raise RuntimeError('Owned server exited before readiness')
                    match = re.search(r'Now listening on:\s+(http://127\.0\.0\.1:\d+)', log_path.read_text(errors='replace'))
                    if match:
                        candidate = match.group(1)
                        try:
                            with urllib.request.urlopen(candidate + '/realtime/v1/games', timeout=1) as response:
                                if any(item['id'] == 'blast' for item in json.load(response)['value']):
                                    origin = candidate
                                    break
                        except Exception:
                            pass
                    time.sleep(.1)
                if not origin:
                    raise RuntimeError('Owned server did not become ready within 30s')
                record['origin'] = origin
                yield origin
                if process.poll() is not None:
                    raise RuntimeError('Owned server exited during verification')
                record['passed'] = True
        except BaseException as error:
            record['error'] = str(error)
            raise
        finally:
            if process:
                stop(process)
            record['seconds'] = round(time.monotonic() - started, 3)

    def playtest(self, scenario, origin=None, app=None, headless=False):
        destination = self.output / 'playtest' / scenario
        catalog = json.loads((ROOT / 'assets/testing/scenarios.json').read_text())
        entry = next(item for item in catalog['scenarios'] if item['id'] == scenario)
        deadline = entry['timeout']
        command = [sys.executable, 'tools/playtest/run.py', '--scenario', scenario,
                   '--godot', self.godot, '--timeout', str(deadline), '--output', str(destination),
                   '--desktop', self.desktop, '--vm', self.vm]
        if origin:
            command += ['--server', origin]
        if app:
            command += ['--app', str(app)]
        if headless:
            command += ['--headless']
        outer_deadline = max(1800, deadline + 60) if self.desktop == 'isolated' and not headless else deadline + 60
        self.require('playtest-' + scenario, command, timeout=outer_deadline,
                     validate=lambda: validate_playtest(destination, headless))

    def finish(self, fatal=None):
        if fatal:
            self.steps.append({'name': 'profile-setup-orchestration', 'passed': False, 'error': fatal, 'seconds': 0})
        passed = bool(self.steps) and all(step['passed'] for step in self.steps)
        result = {'schemaVersion': 1, 'profile': self.profile, 'passed': passed,
                  'seconds': round(time.monotonic() - self.started, 3), 'provenance': self.provenance,
                  'stepCount': len(self.steps), 'failedSteps': sum(not step['passed'] for step in self.steps),
                  'steps': self.steps}
        (self.output / 'summary.json').write_text(json.dumps(result, indent=2) + '\n')
        xml = ET.Element('testsuite', name='blast-circuit-' + self.profile, tests=str(len(self.steps)),
                         failures=str(result['failedSteps']), time=str(result['seconds']))
        properties = ET.SubElement(xml, 'properties')
        for key, value in self.provenance.items():
            ET.SubElement(properties, 'property', name=key, value=str(value))
        for step in self.steps:
            case = ET.SubElement(xml, 'testcase', name=step['name'], classname=self.profile, time=str(step['seconds']))
            if not step['passed']:
                ET.SubElement(case, 'failure', message=step.get('error', 'Failed')).text = step.get('error', 'Failed')
            if step.get('log'):
                ET.SubElement(case, 'system-out').text = 'Log: ' + step['log']
        ET.ElementTree(xml).write(self.output / 'junit.xml', encoding='utf-8', xml_declaration=True)
        print(json.dumps({'passed': passed, 'profile': self.profile, 'summary': str(self.output / 'summary.json')}), flush=True)
        return 0 if passed else 1


def validate_playtest(destination, headless):
    receipts = list(destination.glob('*/runner-summary.json'))
    if len(receipts) != 1:
        raise RuntimeError('Expected exactly one fresh playtest runner receipt')
    runner = json.loads(receipts[0].read_text())
    engine = json.loads((receipts[0].parent / 'summary.json').read_text())
    if not runner.get('passed') or not engine.get('passed') or engine.get('assertions', 0) <= 0:
        raise RuntimeError('Playtest reports failed or zero assertions')
    if runner.get('runtimeErrorCount', 0) or (not headless and not engine.get('screenshots')):
        raise RuntimeError('Playtest contains runtime errors or missing graphical captures')
    return {'receipt': str(receipts[0]), 'assertions': engine['assertions'],
            'screenshots': len(engine.get('screenshots', []))}


def validate_soak(path):
    report = json.loads(path.read_text())
    if len(report.get('results', [])) != 72 or report.get('fairnessChecks', 0) <= 0:
        raise RuntimeError('AI soak receipt is incomplete')
    for item in [report, *report['results']]:
        for key in ('passed', 'Passed'):
            if key in item and item[key] is not True:
                raise RuntimeError('AI soak receipt explicitly reports a failure')
    limit = report.get('idleLimitTicks')
    rate = report.get('tickRate')
    if type(limit) is not int or limit <= 0 or type(rate) is not int or rate <= 0:
        raise RuntimeError('AI soak receipt is missing valid liveness limits')
    longest = 0
    identities = set()
    for result in report['results']:
        if any(type(result.get(key)) is not int or result[key] <= 0
               for key in ('Ticks', 'ActiveTicks', 'EligibleBotTicks', 'MovingBotTicks', 'Bombs')):
            raise RuntimeError('AI soak scenario has no positive observed gameplay activity')
        idle = result.get('LongestOpenIdleTicks')
        if type(idle) is not int or idle < 0 or idle > limit:
            raise RuntimeError('AI soak scenario exceeds its reported safe-exit liveness limit')
        identity = tuple(result.get(key) for key in ('Stage', 'Difficulty', 'Bots', 'Seed'))
        if any(value is None for value in identity) or identity in identities:
            raise RuntimeError('AI soak receipt contains missing or duplicate scenario identities')
        identities.add(identity)
        longest = max(longest, idle)
    return {'receipt': str(path), 'scenarios': len(report['results']),
            'longestOpenIdleSeconds': longest / rate, 'idleLimitSeconds': limit / rate}


def controller_files(directory):
    missing = [name for name in CONTROLLER_FILES if not (directory / name).is_file() or (directory / name).stat().st_size == 0]
    if missing:
        raise RuntimeError('Controller backend is incomplete: ' + ', '.join(missing))
    return {'switch2UsbBackend': True, 'files': list(CONTROLLER_FILES)}


def ensure_controller_backend(suite):
    if platform.system() != 'Darwin' or platform.machine().lower() not in ('arm64', 'aarch64'):
        raise RuntimeError('The full macOS package requires a Darwin ARM64 host')
    native = ROOT / 'native/macos'
    try:
        evidence = controller_files(native)
    except RuntimeError:
        suite.require('controller-backend-build', ['bash', 'tools/controllers/bootstrap-sdl.sh'], timeout=600,
                      validate=lambda: controller_files(native))
    else:
        suite.steps.append({'name': 'controller-backend-reuse', 'passed': True, 'seconds': 0, 'evidence': evidence})


def execute(suite, app=None):
    if suite.profile == 'public':
        suite.step('public-boundaries', [sys.executable, 'tools/ci/public_checks.py'])
        for folder in ('ci', 'cluster', 'networking', 'packaging', 'party', 'playtest', 'qa'):
            suite.step(folder + '-contracts', [sys.executable, '-m', 'unittest', 'discover', '-s', 'tools/' + folder, '-p', 'test_*.py'])
        suite.build('tests/Bomber.Release.Probe')
        suite.build('tests/Bomber.Audio.Tests')
        suite.step('audio', ['dotnet', 'run', '--no-build', '--project', 'tests/Bomber.Audio.Tests'], footer=FOOTERS['Bomber.Audio.Tests'])
        suite.build('tests/Bomber.DesktopUpdate.Tests')
        suite.step('desktop-update', ['dotnet', 'run', '--no-build', '--project', 'tests/Bomber.DesktopUpdate.Tests'], footer=FOOTERS['Bomber.DesktopUpdate.Tests'])
        return
    if suite.profile == 'managed':
        projects = ['Bomber.Game.Tests', 'Bomber.Client.Tests', 'Bomber.Ai.Tests', 'Bomber.Audio.Tests', 'Bomber.Cluster.Tests', 'Bomber.DesktopUpdate.Tests', 'Bomber.Smoke']
        for project in projects:
            suite.build('tests/' + project)
        suite.build('tests/Bomber.Release.Probe')
        published = ROOT / 'artifacts/server-release'
        suite.require('publish-server', ['dotnet', 'publish', 'src/Bomber.Server/Bomber.Server.csproj',
                                       '-c', 'Release', '--self-contained', 'false', '-p:UseAppHost=false',
                                       '--nologo', '-m:1', '-o', str(published)])
        for project in projects[:-1]:
            suite.step(project, ['dotnet', 'run', '--no-build', '--project', 'tests/' + project], footer=FOOTERS[project])
        soak = suite.output / 'ai-soak.json'
        suite.step('ai-soak', ['dotnet', 'run', '--no-build', '--project', 'tests/Bomber.Ai.Tests', '--', '--soak', '--output=' + str(soak)],
                   timeout=600, validate=lambda: validate_soak(soak), footer=FOOTERS['ai-soak'])
        suite.require('stage-server-delivery', [sys.executable, 'tools/ci/package_server.py', '--stage-only'])
        with suite.server(dll=ROOT / 'artifacts/server-staged/Bomber.Server.dll') as origin:
            suite.require('release-admission', [sys.executable, 'tools/ci/verify_deployment.py',
                          '--targets', json.dumps({'staged': origin}), '--expected-sha', suite.provenance['revision'],
                          '--output', str(suite.output / 'release-admission.json')])
            suite.require('release-connection', ['dotnet', 'run', '--no-build', '--project', 'tests/Bomber.Release.Probe', '--', origin],
                          footer=FOOTERS['release-probe'])
            suite.require('network-smoke', ['dotnet', 'run', '--no-build', '--project', 'tests/Bomber.Smoke', '--', origin],
                          footer=FOOTERS['network-smoke'])
        with suite.server(dll=ROOT / 'artifacts/server-staged/Bomber.Server.dll', simulation_capacity=0) as origin:
            suite.require('network-adaptive-relay', ['dotnet', 'run', '--no-build', '--project', 'tests/Bomber.Smoke', '--', origin, '--adaptive-peer-only'],
                          footer=FOOTERS['adaptive-network'])
        for project, folder in (('src/Bomber.Cluster', 'Blast.Cluster'),
                                ('burbuja-kernel/plugins/Burbuja.Plugins.Realtime.Redis', 'Realtime.Redis')):
            suite.require('publish-' + folder, ['dotnet', 'publish', project, '-c', 'Release', '-m:1',
                          '-o', str(ROOT / 'artifacts/cluster-plugins' / folder)])
        suite.require('stage-cluster-plugins', [sys.executable, 'tools/ci/package_server.py', '--stage-plugins'])
        suite.require('network-fleet', ['dotnet', 'run', '--no-build', '--project', 'tests/Bomber.Cluster.Tests', '--',
                      '--fleet', str(ROOT / 'artifacts/server-staged/Bomber.Server.dll'), str(ROOT / 'artifacts/server-staged/plugins'), str(suite.output / 'fleet')],
                      timeout=360, footer=FOOTERS['fleet-network'])
        if all(step['passed'] for step in suite.steps):
            suite.require('package-tested-server', [sys.executable, 'tools/ci/package_server.py'])
        return
    suite.require('webrtc-backend', [sys.executable, 'tools/networking/bootstrap_webrtc.py'], timeout=180)
    suite.build('Bomberman64Multiplayer.csproj')
    suite.build('src/Bomber.Server/Bomber.Server.csproj')
    suite.require('godot-resource-import', [suite.godot, '--headless', '--path', str(ROOT), '--editor', '--import', '--quit'],
                  timeout=180, engine_log=True)
    if suite.profile == 'package':
        app = app or ROOT / 'artifacts/exports/Blast Circuit.app'
        ensure_controller_backend(suite)
        suite.require('controller-backend-runtime', [sys.executable, 'tools/controllers/check-macos-backend.py', str(ROOT / 'native/macos')])
        suite.require('macos-export', [sys.executable, 'tools/packaging/package-macos.py', '--godot', suite.godot, '--output', str(app)], timeout=600)
        suite.steps.append({'name': 'controller-bundle-audit', 'passed': True, 'seconds': 0,
                            'evidence': controller_files(app / 'Contents/Frameworks/Controllers')})
        suite.playtest('package-smoke', app=app, headless=True)
        with suite.server() as origin:
            suite.playtest('peer-rooms', origin=origin, app=app, headless=True)
            suite.playtest('adaptive-rooms', origin=origin, app=app, headless=True)
            suite.playtest('adaptive-slow-poll', origin=origin, app=app, headless=True)
    elif suite.profile == 'headless':
        suite.playtest('core-headless', headless=True, app=app)
        with suite.server() as origin:
            suite.playtest('peer-rooms', origin=origin, headless=True, app=app)
            suite.playtest('adaptive-rooms', origin=origin, headless=True, app=app)
            suite.playtest('adaptive-slow-poll', origin=origin, headless=True, app=app)
    elif suite.desktop == 'isolated':
        suite.playtest('all', app=app)
    else:
        with suite.server() as origin:
            suite.playtest('all', origin=origin, app=app)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profile', choices=PROFILES, required=True)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--godot', default=os.environ.get('GODOT', '/Applications/Godot_mono.app/Contents/MacOS/Godot'))
    parser.add_argument('--app', type=Path)
    parser.add_argument('--desktop', choices=('isolated', 'shared'),
                        help='macOS graphical QA defaults to an isolated VM; shared explicitly takes the host desktop')
    parser.add_argument('--vm', help='Dedicated stopped Tart VM (default: BLAST_QA_VM or blast-circuit-qa)')
    args = parser.parse_args(argv)
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ')
    output = (args.output or ROOT / 'artifacts/qa' / (stamp + '-' + args.profile)).resolve()
    fatal = None
    if output.exists() and any(output.iterdir()):
        # Preserve a previous receipt rather than turning its success into the
        # new run's failure. The failed request gets its own durable receipt.
        output = output / ('rejected-' + stamp)
        fatal = 'Requested QA output was not empty; use a fresh path for every run'
    output.mkdir(parents=True, exist_ok=True)
    suite = Suite(args.profile, output, args.godot, args.desktop, args.vm)
    try:
        if not fatal:
            execute(suite, app=args.app.resolve() if args.app else None)
    except (Exception, KeyboardInterrupt) as error:
        fatal = str(error) or type(error).__name__
    return suite.finish(fatal)


if __name__ == '__main__':
    raise SystemExit(main())

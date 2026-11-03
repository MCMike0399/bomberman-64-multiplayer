"""Frozen exported-game timing measurement only; never a release qualification."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import time

import desktop_packages as desktop
import startup_lifecycle as lifecycle
import verify_published as public

ROOT = Path(__file__).resolve().parents[2]
SOURCE = '6226d248adc5bc0341c495a7e6288ee3786fca19'
BASELINE = 'af93089f385290de2f30954ef2822bd4051324da'
REPO = 'MCMike0399/bomberman-64-multiplayer'
RELEASE = {'version': '0.45.0', 'build': '56', 'protocolRevision': 6, 'admissionToken': '6/0.45.0/56'}
OUTPUT = ROOT / 'artifacts/windows-startup-phases'
ERRORS = ('ERROR:', 'SCRIPT ERROR:', 'Unhandled exception', 'Unhandled Exception', 'FATAL:',
          'BLAST_SHUTDOWN_DRAIN_FAILED', 'BLAST_SHUTDOWN_FINALIZERS_FAILED', 'BLAST_SHUTDOWN_SAVE_FAILED',
          'BLAST_SHUTDOWN_WATCHDOG_FIRED', 'BLAST_SHUTDOWN_REPORT_FAILED')


def save(path, data):
    path.write_text(json.dumps(data, indent=2) + '\n')


def owned_processes(executable, timeout=10):
    script = ("Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,ExecutablePath,"
              "@{Name='Created';Expression={$_.CreationDate.ToUniversalTime().ToString('O')}} | ConvertTo-Json -Compress")
    result = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command', script],
                            capture_output=True, text=True, check=True, timeout=timeout)
    rows = json.loads(result.stdout) if result.stdout.strip() else []
    if isinstance(rows, dict): rows = [rows]
    expected = {str(executable).casefold(), str(executable.parent / 'BlastCircuit.Player.exe').casefold()}
    return [r for r in rows if (r.get('ExecutablePath') or '').casefold() in expected]


def stop_owned(executable, record, deadline):
    killed = []
    def remaining():
        value = deadline - time.monotonic()
        if value <= 0: raise TimeoutError('Aggregate diagnostic cleanup budget exhausted')
        return value
    def query(): return owned_processes(executable, min(10, remaining()))
    for old in query():
        current = next((r for r in query() if r['ProcessId'] == old['ProcessId']), None)
        if current != old: continue
        result = subprocess.run(['taskkill', '/PID', str(old['ProcessId']), '/T', '/F'],
                                capture_output=True, text=True, timeout=remaining())
        killed.append({'identity': old, 'exitCode': result.returncode})
    alive = query()
    while alive and time.monotonic() < deadline:
        time.sleep(min(.2, remaining())); alive = query()
    record.update(forcedCleanup=killed, remainingOwnedProcesses=alive)
    if alive: raise RuntimeError('Owned exported-game process survived bounded diagnostic cleanup')


def measure(executable, folder, kind):
    folder.mkdir()
    preferences = folder / 'preferences.cfg'
    preferences.write_text('[player]\nautomaticUpdates=false\n[video]\ngraphicsPreset="high"\nfullscreen=true\nresolution=Vector2i(0, 0)\n')
    command = [str(executable), '--rendering-driver', 'd3d12', '--',
               '--smoke-menu' if kind == 'menu' else '--smoke', '--smoke-seconds=4',
               '--smoke-preferences=' + str(preferences), '--smoke-output=' + str(folder)]
    record = {'mode': 'diagnostic-only', 'releaseQualification': False, 'originalWholeProcessCeilingSeconds': 240,
              'diagnosticObservationCeilingSeconds': 270, 'kind': kind, 'command': command,
              'timingPrecision': '100 ms polling; conservative observed exit verdict, not the original wait(timeout=240) result',
              'events': [], 'originalCeilingExceeded': False}
    started = time.monotonic(); epoch = time.time(); seen = set(); process = None; collector = None; collector_log = None
    collector_started = None
    def event(name, **data):
        if name in seen: return
        seen.add(name); record['events'].append({'event': name, 'seconds': time.monotonic() - started, **data})
        save(folder / 'phases.json', record)
    def observe(final=False):
        text = (folder / 'runtime.log').read_text(errors='replace')
        capture = folder / (kind + '.png')
        if capture.exists():
            event('capture-written', fileWrittenSeconds=capture.stat().st_mtime - epoch, observedAfterProcessExit=final)
        if 'GODOT_SMOKE_OK' in text: event('smoke-ok-observed', observedAfterProcessExit=final)
        if (folder / 'shutdown.json').exists(): event('shutdown-report-written', observedAfterProcessExit=final)
    try:
        with (folder / 'runtime.log').open('w') as log:
            process = subprocess.Popen(command, cwd=folder, env=lifecycle.game_environment(), stdout=log, stderr=subprocess.STDOUT)
            record['launcherPid'] = process.pid; event('spawned')
            while process.poll() is None:
                observe()
                elapsed = time.monotonic() - started
                if elapsed >= 240 and not record['originalCeilingExceeded']:
                    record['originalCeilingExceeded'] = True; event('original-ceiling-exceeded')
                    identities = owned_processes(executable); record['processesAtOriginalCeiling'] = identities
                    engine = next((r for r in identities if r['ExecutablePath'].lower().endswith('blastcircuit.player.exe')), None)
                    if engine:
                        collector_log = (folder / 'dump-collector.log').open('w')
                        collector = subprocess.Popen([sys.executable, str(ROOT / 'tools/ci/startup_lifecycle.py'),
                            '--dump-windows', str(engine['ProcessId']), '--output', str(folder), '--image', engine['ExecutablePath']],
                            env=lifecycle.game_environment(), stdout=collector_log, stderr=subprocess.STDOUT)
                        collector_started = time.monotonic()
                        event('native-collector-started', enginePid=engine['ProcessId'])
                if collector is not None and collector.poll() is None and time.monotonic() - collector_started >= 20:
                    collector.kill(); event('native-collector-budget-exceeded')
                if time.monotonic() - started >= 270: event('diagnostic-ceiling-exceeded'); break
                time.sleep(.1)
            record['exitCode'] = process.poll(); record['observedSeconds'] = time.monotonic() - started
            if process.poll() is not None:
                observe(final=True)
                event('process-exited', exitCode=process.returncode)
    finally:
        cleanup_started = time.monotonic(); cleanup_deadline = cleanup_started + 30
        def remaining():
            value = cleanup_deadline - time.monotonic()
            if value <= 0: raise TimeoutError('Aggregate diagnostic cleanup budget exhausted')
            return value
        cleanup_errors = []
        try:
            if collector is not None and collector.poll() is None: collector.kill()
            if process is not None:
                try: stop_owned(executable, record, cleanup_deadline)
                except Exception as error: cleanup_errors.append(error)
                if process.poll() is None: process.kill()
            for owned in (process, collector):
                if owned is not None:
                    try: owned.wait(timeout=remaining())
                    except Exception as error: cleanup_errors.append(error)
            if collector is not None and collector.poll() is not None:
                record['nativeCollectorExitCode'] = collector.returncode
                dump = folder / 'native.dmp'
                record['nativeDumpCaptured'] = collector.returncode == 0 and dump.is_file() and dump.read_bytes()[:4] == b'MDMP'
            if time.monotonic() > cleanup_deadline: cleanup_errors.append(TimeoutError('Diagnostic cleanup exceeded its aggregate budget'))
            record['cleanupErrors'] = [str(error) for error in cleanup_errors]
            if cleanup_errors: raise ExceptionGroup('Diagnostic cleanup failed', cleanup_errors)
        finally:
            record['cleanupSeconds'] = time.monotonic() - cleanup_started
            if collector_log is not None: collector_log.close()
            observe(final=process is not None and process.poll() is not None)
            text = (folder / 'runtime.log').read_text(errors='replace')
            record['runtimeErrors'] = [s for s in text.splitlines() if any(x in s for x in ERRORS)]
            record['rawWarnings'] = [s for s in text.splitlines() if 'WARNING:' in s]
            record['smokeLines'] = [s for s in text.splitlines() if 'GODOT_SMOKE_' in s]
            for name in ('shutdown.json', 'renderer-selection.json'):
                try: record[name] = json.loads((folder / name).read_text())
                except (OSError, ValueError) as error: record[name] = {'readError': str(error)}
            record['observedProcessExitedCleanlyWithinOriginalCeiling'] = (record.get('exitCode') == 0
                and record.get('observedSeconds', 271) <= 240 and not record['originalCeilingExceeded']
                and not record['runtimeErrors'] and len(record['smokeLines']) == 1)
            save(folder / 'phases.json', record)
    return record


def baseline_inputs():
    directory = OUTPUT / 'baseline-inputs'; directory.mkdir()
    release = public.api(f'repos/{REPO}/releases/tags/v0.45.0')
    if release['draft'] or release['target_commitish'] != BASELINE: raise RuntimeError('Frozen stable baseline identity changed')
    assets = {a['name']: a for a in release['assets']}
    for name in ('Blast-Circuit-release.json', 'Blast-Circuit-release.json.sig'):
        public.download(assets[name]['browser_download_url'], directory / name)
    public.verify_signature(directory / 'Blast-Circuit-release.json')
    delivery = json.loads((directory / 'Blast-Circuit-release.json').read_text())
    if delivery['revision'] != BASELINE or delivery['release'] != RELEASE: raise RuntimeError('Signed baseline differs')
    for name in ('Blast-Circuit-windows-x64.zip', 'Blast-Circuit-windows-x64.zip.sha256', 'Blast-Circuit-windows-x64-metadata.json'):
        public.download(assets[name]['browser_download_url'], directory / name)
        entry = delivery['assets'][name]
        if desktop.digest(directory / name) != entry['sha256'] or (directory / name).stat().st_size != entry['size']:
            raise RuntimeError('Baseline bytes differ from signed delivery')
    return directory


def main():
    if os.name != 'nt' or os.environ.get('GITHUB_ACTIONS') != 'true': raise RuntimeError('Use only the disposable Windows CI desktop')
    OUTPUT.mkdir(parents=True, exist_ok=False)
    spec = importlib.util.spec_from_file_location('verifier', ROOT / 'tools/packaging/verify-desktop.py')
    verifier = importlib.util.module_from_spec(spec); spec.loader.exec_module(verifier)
    report = {'mode': 'diagnostic-only', 'graphicsQualified': False, 'releaseQualification': False,
              'ordering': 'candidate then baseline; High-menu then High-gameplay; not the original nine-case sequence, cold-start, or throughput comparison',
              'runnerSource': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(), 'variants': []}
    try:
        for name, directory, source in [('candidate', ROOT / 'artifacts', SOURCE), ('baseline', baseline_inputs(), BASELINE)]:
            metadata = json.loads((directory / 'Blast-Circuit-windows-x64-metadata.json').read_text())
            if metadata['revision'] != source or metadata['release'] != RELEASE: raise RuntimeError('Wrong frozen payload')
            desktop.inspect_archive(directory, 'windows-x64', source, RELEASE)
            folder = OUTPUT / name; folder.mkdir(); installed = folder / 'installed'; installed.mkdir()
            verifier.extract(directory / 'Blast-Circuit-windows-x64.zip', installed)
            executable = installed / 'Blast-Circuit-windows-x64/BlastCircuit.exe'
            row = {'variant': name, 'source': source, 'archiveSha256': desktop.digest(directory / 'Blast-Circuit-windows-x64.zip'),
                   'engineSha256': desktop.digest(executable.parent / 'BlastCircuit.Player.exe'), 'cases': []}
            report['variants'].append(row)
            for kind in ('menu', 'gameplay'):
                row['cases'].append(measure(executable, folder / kind, kind))
                save(OUTPUT / 'diagnostic.json', report)
        report['measurementCompleted'] = True
    finally:
        save(OUTPUT / 'diagnostic.json', report)
    print(json.dumps({'measurementCompleted': True, 'releaseQualification': False,
                      'observedOriginalProcessExitVerdicts': [[c['observedProcessExitedCleanlyWithinOriginalCeiling'] for c in r['cases']] for r in report['variants']]}))


if __name__ == '__main__': main()

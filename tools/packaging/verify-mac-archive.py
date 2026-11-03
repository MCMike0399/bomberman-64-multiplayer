#!/usr/bin/env python3
"""Run complete packaged Mac gameplay and graphical journeys from the actual DMG-installed payload."""
import argparse
import importlib.util
import json
from pathlib import Path
import platform
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import crossplay_runtime
import desktop_validation as validation
import desktop_packages as desktop
from promote_release import inspect_package, inspect_server


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    parser.add_argument('--functional-only',action='store_true',help='Installed headless/native network journeys only; never graphical qualification')
    parser.add_argument('--peer-room-repeats', type=int, choices=range(1, 4), default=3, help='Three fresh native peer processes are required for qualification; lower counts cannot produce an accepted receipt')
    args = parser.parse_args()
    if platform.system() != 'Darwin' or platform.machine().lower() != 'arm64':
        raise RuntimeError('Complete Mac archive verification requires the Apple Silicon test desktop')
    directory = args.directory.resolve()
    metadata = json.loads((directory / 'build-metadata.json').read_text())
    revision, release = metadata['revision'], metadata['release']
    inspect_package(directory, revision, release['version'], release)
    inspect_server(directory, revision, release['version'], release)
    output = directory / 'Blast-Circuit-macos-arm64-regression-details'
    output.mkdir()
    settings = {'os': 'macOS', 'driver': 'metal'}
    spec = importlib.util.spec_from_file_location('qa', ROOT / 'tools/qa/run.py')
    qa = importlib.util.module_from_spec(spec); spec.loader.exec_module(qa)
    verifier_spec = importlib.util.spec_from_file_location('verifier', ROOT / 'tools/packaging/verify-desktop.py')
    verifier = importlib.util.module_from_spec(verifier_spec); verifier_spec.loader.exec_module(verifier)
    cases = []; native_repeats = []
    try:
        executable = crossplay_runtime.install(directory, 'macos-arm64', output)
        app = executable.parents[2]
        verifier.extract(directory / 'Blast-Circuit-server-dotnet10.zip', output)
        suite = qa.Suite('mac-archive', output, 'unused', desktop='shared')
        with suite.server(dll=output / 'Blast-Circuit-server-dotnet10/Bomber.Server.dll') as origin:
            for scenario in (validation.HEADLESS if args.functional_only else (*validation.HEADLESS,*validation.GRAPHICAL)):
                for repeat in range(args.peer_room_repeats if scenario == 'peer-rooms' else 1):
                    previous = set(output.iterdir())
                    graphical = scenario in validation.GRAPHICAL
                    command = [sys.executable, str(ROOT / 'tools/playtest/run.py'), '--app', str(app),
                               '--scenario', scenario, '--server', origin, '--desktop', 'shared', '--output', str(output)]
                    command += ['--rendering-driver', 'metal'] if graphical else ['--headless']
                    if scenario == 'peer-rooms': command.append('--engine-verbose')
                    # The hosted Metal run was still capturing journey 56/61 at
                    # the catalog's 1500-second cutoff. Allow the complete archive
                    # tour to finish without changing any journey's own waits.
                    if scenario == 'all': command += ['--timeout', '2400']
                    completed = subprocess.run(command, cwd=ROOT, check=False, timeout=2700 if scenario == 'all' else 1800)
                    reports = [p / 'summary.json' for p in output.iterdir() if p not in previous and (p / 'summary.json').exists()]
                    if len(reports) != 1:
                        if scenario == 'peer-rooms':
                            native_repeats.append({'index': repeat + 1, 'revision': revision, 'passed': False, 'error': 'missing unique scenario receipt'})
                            (output / 'native-peer-repeats.json').write_text(json.dumps({'requested': args.peer_room_repeats, 'completed': native_repeats}, indent=2) + '\n')
                        raise RuntimeError('Missing unique Mac exported scenario receipt')
                    report = json.loads(reports[0].read_text())
                    runner = json.loads((reports[0].parent / 'runner-summary.json').read_text())
                    if scenario == 'peer-rooms':
                        native_repeats.append({'index': repeat + 1, 'revision': revision, 'passed': False,
                            'runnerPassed': completed.returncode == 0 and runner.get('passed') is True and runner.get('errors') == [],
                            'runtimeWarnings': runner.get('runtimeWarnings', []), 'report': {**report, 'runtimeErrors': runner.get('runtimeErrors')}})
                        (output / 'native-peer-repeats.json').write_text(json.dumps({'requested': args.peer_room_repeats, 'completed': native_repeats}, indent=2) + '\n')
                    if completed.returncode != 0: raise subprocess.CalledProcessError(completed.returncode, command)
                    if runner.get('passed') is not True or runner.get('errors') != []: raise RuntimeError('Mac exported runner failed')
                    report['runtimeErrors'] = runner.get('runtimeErrors')
                    if 'displayRestart' in runner: report['displayRestart'] = runner['displayRestart']
                    validation.inspect_report(report, scenario, settings, revision, release, graphical)
                    if scenario == 'peer-rooms':
                        native_repeats[-1]['passed'] = True
                        (output / 'native-peer-repeats.json').write_text(json.dumps({'requested': args.peer_room_repeats, 'completed': native_repeats}, indent=2) + '\n')
                    if repeat == 0: cases.append(report)
        receipt={'schemaVersion': 1, 'passed': True,
            'revision': revision, 'release': release, 'archiveSha256': desktop.digest(directory / 'Blast-Circuit-macos-arm64.zip'),
            'downloadSha256': desktop.digest(directory / 'Blast-Circuit-macos-arm64.dmg'),
            'serverSha256': desktop.digest(directory / 'Blast-Circuit-server-dotnet10.zip'), 'cases': cases,
            'scope':'functional-regression'if args.functional_only else'complete-regression',
            'graphicsQualified':False if args.functional_only else None,
            'nativePeerRepeatsRequested':args.peer_room_repeats,'nativePeerRepeats':native_repeats}
        validation.inspect_mac(receipt,revision,release,receipt['archiveSha256'],receipt['downloadSha256'],
                               receipt['serverSha256'],require_graphics=not args.functional_only)
        (directory / 'Blast-Circuit-macos-arm64-regression.json').write_text(json.dumps(receipt,indent=2)+'\n')
    finally:
        if (output / 'installed').exists(): shutil.rmtree(output / 'installed')
    print('PASS DMG-installed Mac functional regression'if args.functional_only else'PASS full DMG-installed Mac gameplay, multiplayer and graphical regression')


if __name__ == '__main__': main()

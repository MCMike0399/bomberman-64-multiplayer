#!/usr/bin/env python3
"""Diagnostic only: attribute visual-parity engine errors to the thread that raised them.

Runs the installed package's unmodified visual-parity fixture with the QA-only
--diagnose-error-threads observer. Every runtime error stays in the result;
nothing here qualifies graphics, changes a release verdict or filters output.
"""
import argparse
import json
from pathlib import Path
import re
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools' / 'ci'))
import crossplay_runtime  # noqa: E402
import visual_parity  # noqa: E402


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--target', default='windows-x64')
    parser.add_argument('--driver', default='d3d12', choices=('d3d12', 'vulkan'))
    parser.add_argument('--stages', default='blizzard-battle,top-rules')
    parser.add_argument('--repeats', type=int, default=3)
    parser.add_argument('--timeout', type=int, default=900)
    args = parser.parse_args()
    if not 1 <= args.repeats <= 6:
        raise SystemExit('repeats must be 1-6')
    args.output.mkdir(parents=True, exist_ok=False)
    report = {'scope': 'Diagnostic thread attribution only; not GPU qualification or a release verdict',
              'driver': args.driver, 'runs': [], 'errors': []}
    executable = crossplay_runtime.install(args.directory, args.target, args.output)
    try:
        for index in range(args.repeats):
            for stage in args.stages.split(','):
                case = args.output / stage / f'run-{index}'
                case.mkdir(parents=True)
                command = [str(executable), '--rendering-driver', args.driver, '--windowed', '--max-fps', '30',
                           '--fixed-fps', '30', '--disable-vsync', '--', '--display-windowed', '--playtest=visual-parity',
                           '--playtest-output=' + str(case), '--parity-stage=' + stage, '--diagnose-error-threads']
                row = {'stage': stage, 'index': index, 'command': command}
                report['runs'].append(row)
                started = time.monotonic()
                try:
                    with (case / 'runtime.log').open('w') as log:
                        result = subprocess.run(command, cwd=case, stdout=log, stderr=subprocess.STDOUT, timeout=args.timeout)
                    row['exitCode'] = result.returncode
                except subprocess.TimeoutExpired:
                    row['exitCode'] = None
                    row['timedOut'] = True
                    subprocess.run(['taskkill', '/IM', 'BlastCircuit.exe', '/T', '/F'], capture_output=True, timeout=30)
                row['seconds'] = round(time.monotonic() - started, 1)
                text = (case / 'runtime.log').read_text(errors='replace')
                row['runtimeErrors'] = sum(1 for line in text.splitlines()
                                           if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|FATAL:)', line))
                row['freeRidErrors'] = text.count('(free_rid) can only be called from the render thread')
                row['leakWarnings'] = re.findall(r'WARNING: \d+ RIDs of type "[^"]+" were leaked', text)
                attribution = case / 'error-threads.json'
                if attribution.is_file():
                    errors = json.loads(attribution.read_text())['errors']
                    groups = {}
                    for error in errors:
                        key = (error['rationale'] or error['code'])[:90], error['phase'], error['isGodotMainThread'], \
                            error['workerPoolTaskId'] != -1, error['workerPoolGroupId'] != -1, error['managedThreadName']
                        groups[key] = groups.get(key, 0) + 1
                    row['attribution'] = [{'message': k[0], 'phase': k[1], 'mainThread': k[2], 'inWorkerTask': k[3],
                                           'inWorkerGroup': k[4], 'managedThreadName': k[5], 'count': v}
                                          for k, v in groups.items()]
                for name, right in (('same-draw', 'parity-base-a-1.png'), ('fresh-target', 'parity-base-b-0.png')):
                    left = case / 'parity-base-a-0.png'
                    if left.is_file() and (case / right).is_file():
                        _, code = visual_parity.pair(left, case / right, case / name, controlled=False)
                        row[name] = code
                print(json.dumps({k: row.get(k) for k in ('stage', 'index', 'exitCode', 'seconds', 'freeRidErrors',
                                                           'runtimeErrors', 'same-draw', 'fresh-target', 'attribution')}),
                      flush=True)
    finally:
        uninstall = executable.parent / 'unins000.exe'
        if args.target == 'windows-x64' and uninstall.is_file():
            subprocess.run([str(uninstall), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'], timeout=120)
        (args.output / 'shader-rid-threads.json').write_text(json.dumps(report, indent=2) + '\n')


if __name__ == '__main__':
    main()

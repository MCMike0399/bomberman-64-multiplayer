#!/usr/bin/env python3
"""Collect every fault row, preserving failed behavior as failed behavior."""
import argparse
import json
from pathlib import Path
import platform
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
BASELINE = '2f018368d4f6a539561f9f374ce540575f541acc'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, required=True)
    parser.add_argument('--variant', choices=('baseline', 'candidate'), required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    linux = platform.system() == 'Linux'
    cases = [('blackout-3', 'tc' if linux else 'proxy'), ('blackout-10', 'tc' if linux else 'proxy'), ('reset', 'proxy')]
    if linux: cases += [('blackout-45', 'tc'), ('blackout-60', 'tc'), ('upstream', 'proxy'), ('downstream', 'proxy'), ('jitter', 'tc'), ('normal', 'proxy')]
    source = args.source_root.resolve()
    rows = []; failed = False
    for case, driver in cases:
        destination = output / case
        command = [sys.executable, str(ROOT / 'tools/ci/network_blackout.py'), '--case', case, '--driver', driver,
                   '--source-root', str(source), '--server-assembly', str(source / 'src/Bomber.Server/bin/Debug/net10.0/Bomber.Server.dll'),
                   '--output', str(destination)]
        if args.variant == 'candidate': command.append('--assert-resilience')
        try:
            result = subprocess.run(command, cwd=ROOT, timeout=850)
            data = json.loads((destination / 'measurement.json').read_text())
            row = {key: value for key, value in data.items() if key != 'visibleSamples'}
            row.update(exitCode=result.returncode, scenario=case, driver=driver)
            failed |= result.returncode != 0
        except (OSError, ValueError, subprocess.SubprocessError) as error:
            row = {'scenario': case, 'driver': driver, 'measurementCompleted': False, 'error': str(error)}
            failed = True
        rows.append(row)
        (output / 'matrix.json').write_text(json.dumps({'variant': args.variant, 'platform': platform.system(),
            'productSource': subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip(),
            'measurementOrCandidateGatePassed': not failed, 'baselineBehaviorIsNotAcceptance': True, 'rows': rows}, indent=2) + '\n')
    if failed: raise SystemExit(1)


if __name__ == '__main__': main()

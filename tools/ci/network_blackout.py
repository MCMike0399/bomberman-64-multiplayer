#!/usr/bin/env python3
"""Run a bounded network-fault witness against this harness's owned loopback server."""
import argparse
import json
import math
import subprocess
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/qa'))
from run import Suite


def validate_measurement(path, scenario, strict):
    data = json.loads(path.read_text())
    checks = data.get('checks')
    if (data.get('measurementCompleted') is not True or data.get('scenario') != scenario or
            not isinstance(checks, dict) or not checks or any(type(v) is not bool for v in checks.values()) or
            data.get('conforms') is not all(checks.values()) or not data.get('visibleSamples') or
            len(data.get('maxVisibleSnapshotGapSeconds', [])) != 3 or
            any(type(x) not in (int, float) or not math.isfinite(x) for x in data['maxVisibleSnapshotGapSeconds'])):
        raise RuntimeError('Incomplete or contradictory network-fault measurement')
    if strict and data['conforms'] is not True:
        raise RuntimeError('Resilience assertions failed: ' + ', '.join(k for k, v in checks.items() if not v))
    return {'measurementCompleted': True, 'conforms': data['conforms'],
            'failedBehaviorChecks': [k for k, v in checks.items() if not v]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--case', default='blackout-10')
    parser.add_argument('--driver', default='proxy', choices=('proxy', 'tc'))
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--server-assembly', type=Path, default=ROOT / 'artifacts/server-staged/Bomber.Server.dll')
    parser.add_argument('--assert-resilience', action='store_true')
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--probe-assembly', type=Path)
    parser.add_argument('--runtime-identity', type=Path)
    parser.add_argument('--fast-receive-recovery', choices=('on', 'off'), default='on')
    args = parser.parse_args()
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    source = args.source_root.resolve()
    suite = Suite('managed', output, 'unused')
    suite.provenance['productSource'] = subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip()
    if args.runtime_identity:
        import hashlib
        identity = json.loads(args.runtime_identity.read_text())
        for name, expected in identity['files'].items():
            if hashlib.sha256((args.runtime_identity.parent / name).read_bytes()).hexdigest() != expected:
                raise RuntimeError('Runtime fixture identity mismatch: ' + name)
        suite.provenance['productSource'] = identity['productSource']
        suite.provenance['probeSource'] = identity['probeSource']
        (output / 'runtime-identity.json').write_text(json.dumps(identity, indent=2) + '\n')
    with suite.server(dll=args.server_assembly.resolve()) as origin:
        prefix = ['dotnet', str(args.probe_assembly.resolve())] if args.probe_assembly else ['dotnet', 'run', '--no-build', '--project', str(source / 'tests/Bomber.Smoke'), '--']
        command = [*prefix, origin,
                   '--blackout-only', '--blackout-case=' + args.case, '--blackout-driver=' + args.driver,
                   '--blackout-output=' + str(output / 'measurement.json'), '--fast-receive-recovery=' + args.fast_receive_recovery]
        if args.assert_resilience: command.append('--assert-resilience')
        passed = suite.step('network-blackout', command, timeout=750,
                            validate=lambda: validate_measurement(output / 'measurement.json', args.case, args.assert_resilience))
    if args.driver == 'tc':
        cleanup = subprocess.run(['tc', 'qdisc', 'show', 'dev', 'lo'], capture_output=True, text=True, check=True)
        (output / 'netem-cleanup.txt').write_text(cleanup.stdout)
        if '7b00:' in cleanup.stdout or '7b30:' in cleanup.stdout:
            passed = False
    (output / 'harness.json').write_text(json.dumps({'steps': suite.steps, 'provenance': suite.provenance}, indent=2) + '\n')
    if not passed: raise SystemExit(1)


if __name__ == '__main__': main()

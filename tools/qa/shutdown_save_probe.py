#!/usr/bin/env python3
"""Require a real game to exit, drain and report failure when preferences are read-only."""
import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
import time

ROOT = Path(__file__).resolve().parents[2]


def run(executable, output, source_project=False):
    output.mkdir(parents=True, exist_ok=False)
    preferences = output / 'readonly-preferences.cfg'
    preferences.write_text('[video]\ngraphicsPreset="low"\nfullscreen=false\n')
    preferences.chmod(0o444)
    command = [str(executable), '--headless']
    if source_project:
        command += ['--path', str(ROOT)]
    command += ['--', '--smoke-menu', '--smoke-seconds=1',
                f'--smoke-preferences={preferences}', f'--smoke-output={output}']
    started = time.monotonic()
    try:
        with (output / 'runtime.log').open('w') as log:
            process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=os.name == 'posix')
            try:
                code = process.wait(timeout=15)
                timed_out = False
            except subprocess.TimeoutExpired:
                timed_out = True
                if os.name == 'posix':
                    os.killpg(process.pid, signal.SIGKILL)
                else:
                    subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], capture_output=True, timeout=15)
                code = process.wait(timeout=10)
    finally:
        preferences.chmod(0o644)
    text = (output / 'runtime.log').read_text(errors='replace')
    receipt_path = output / 'shutdown.json'
    shutdown = json.loads(receipt_path.read_text()) if receipt_path.is_file() else None
    passed = (not timed_out and code != 0 and 'BLAST_SHUTDOWN_SAVE_FAILED' in text and
              isinstance(shutdown, dict) and shutdown.get('savedStateFlushed') is False and
              shutdown.get('drain', {}).get('completed') is True and
              shutdown.get('watchdogEnabled') is False and shutdown.get('watchdogFired') is False)
    report = {'scope': 'Expected save-failure regression, not a clean product QA pass',
              'passed': passed, 'command': command, 'exitCode': code, 'timedOut': timed_out,
              'seconds': time.monotonic() - started, 'shutdown': shutdown}
    (output / 'save-failure-probe.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report))
    return 0 if passed else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--executable', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--source-project', action='store_true')
    args = parser.parse_args()
    raise SystemExit(run(args.executable.resolve(), args.output.resolve(), args.source_project))

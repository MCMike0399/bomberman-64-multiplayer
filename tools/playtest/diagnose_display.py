"""Replay the archived display journey and sample only its owned fresh child before timeout."""
import json
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]


def main():
    output = ROOT / 'artifacts/display-diagnostic'
    output.mkdir()
    app = output / 'Blast Circuit.app'
    subprocess.run(['ditto', '-x', '-k', str(ROOT / 'artifacts/Blast-Circuit-macos-arm64.zip'), str(output)], check=True)
    command = [sys.executable, str(ROOT / 'tools/playtest/run.py'), '--app', str(app), '--scenario',
               'display-settings', '--desktop', 'shared', '--output', str(output), '--rendering-driver', 'metal']
    samples = []
    with (output / 'launcher.log').open('w') as log:
        process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT)
    try:
        deadline = time.monotonic() + 600
        seen = {}
        while process.poll() is None:
            if time.monotonic() > deadline:
                raise TimeoutError('Focused display diagnostic exceeded its bound')
            rows = subprocess.check_output(['ps', '-axo', 'pid=,command='], text=True).splitlines()
            for row in rows:
                pid, _, args = row.strip().partition(' ')
                if '--playtest-restore-display' not in args or str(output) not in args:
                    continue
                first = seen.setdefault(pid, time.monotonic())
                if time.monotonic() - first >= 30 and pid not in samples:
                    destination = output / ('child-' + pid + '-sample.txt')
                    subprocess.run(['sample', pid, '3', '10', '-file', str(destination)],
                                   stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, check=True, timeout=15)
                    samples.append(pid)
            time.sleep(1)
        if process.returncode:
            raise RuntimeError('Focused exported display journey failed; inspect retained child sample and receipts')
    finally:
        if process.poll() is None:
            process.terminate()
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired: process.kill(); process.wait()
        (output / 'diagnostic.json').write_text(json.dumps({'diagnosticOnly': True, 'samples': samples,
            'exitCode': process.returncode}, indent=2) + '\n')


if __name__ == '__main__': main()

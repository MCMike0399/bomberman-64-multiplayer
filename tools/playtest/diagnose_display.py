"""Replay the archived display journey and sample only its owned fresh child before timeout."""
import json
import re
import shutil
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
    fixture = None
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
                if fixture is None:
                    identifier = re.search(r'--playtest-preferences=([a-f0-9]{32})', args)
                    if identifier:
                        preference = Path.home() / 'Library/Application Support/Godot/app_userdata/Blast Circuit' / ('qa-preferences-' + identifier[1] + '.cfg')
                        shutil.copy2(preference, output / 'child-preferences.cfg')
                        fixture = (identifier[1], preference)
                if time.monotonic() - first >= 30 and pid not in samples:
                    destination = output / ('child-' + pid + '-sample.txt')
                    subprocess.run(['sample', pid, '3', '10', '-file', str(destination)],
                                   stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, check=True, timeout=15)
                    samples.append(pid)
            time.sleep(1)
        if fixture is None:
            raise RuntimeError('The original display journey never launched its fresh child')
        # Replay the identical persisted-settings child once the parent has exited.
        # This diagnoses resident-renderer contention without changing its deadline.
        identifier, preference = fixture
        shutil.copy2(output / 'child-preferences.cfg', preference)
        serial = output / 'serial-child'
        serial.mkdir()
        command = [str(app / 'Contents/MacOS/Blast Circuit'), '--windowed', '--max-fps', '30', '--',
                   '--playtest=display-settings', '--playtest-restore-display', '--playtest-preferences=' + identifier,
                   '--playtest-output=' + str(serial)]
        try:
            with (serial / 'runtime.log').open('w') as log:
                child = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=90)
            from contracts import validate_summary
            report = json.loads((serial / 'summary.json').read_text())
            errors = validate_summary(report, serial, 'display-settings', False)
            if child.returncode or errors:
                raise RuntimeError('Serial fresh child failed: ' + ', '.join(errors))
            print('PASS serial fresh display child with the original 90-second deadline')
        finally:
            preference.unlink(missing_ok=True)
    finally:
        if process.poll() is None:
            process.terminate()
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired: process.kill(); process.wait()
        (output / 'diagnostic.json').write_text(json.dumps({'diagnosticOnly': True, 'samples': samples,
            'exitCode': process.returncode}, indent=2) + '\n')


if __name__ == '__main__': main()

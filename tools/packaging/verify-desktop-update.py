#!/usr/bin/env python3
"""Retain actual game-update diagnostics and reject silent success or renderer/script errors."""
import argparse
import json
from pathlib import Path
import platform
import re
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import desktop_packages as desktop
import desktop_update_gates as gates


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=desktop.TARGETS, required=True)
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    args = parser.parse_args()
    settings, item = desktop.TARGETS[args.target], desktop.names(args.target)
    if platform.system() != settings['os']:
        raise RuntimeError('Actual game updates require the target OS')
    directory = args.directory.resolve()
    metadata = json.loads((directory / item['metadata']).read_text())
    output = directory / (item['root'] + '-updater-details') / uuid.uuid4().hex
    output.mkdir(parents=True)
    receipt = {'schemaVersion': 1, 'passed': False, 'platform': args.target}
    try:
        with (output / 'runtime.log').open('w') as log:
            result = subprocess.run(['dotnet', 'run', '--project', str(ROOT / 'tests/Bomber.DesktopUpdate.GameTests'), '-m:1', '--',
                                     str(directory), str(directory / 'update-fixture'), str(output / 'transactions')],
                                    cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=720)
        text = (output / 'runtime.log').read_text(errors='replace')
        errors = [line for line in text.splitlines() if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|Unhandled Exception|FATAL:)', line)]
        footer = 'PASS actual desktop game updater: authenticated feed/download, running-app handoff, healthy relaunch and rollback'
        if result.returncode != 0 or text.splitlines().count(footer) != 1 or errors:
            raise RuntimeError('Actual game updater failed; inspect retained runtime.log')
        receipt = json.loads((output / 'transactions/summary.json').read_text())
        receipt['runtimeErrors'] = errors
        gates.inspect(receipt, args.target, metadata['revision'], metadata['release'], desktop.digest(directory / item['archive']))
    finally:
        (directory / item['updater_test']).write_text(json.dumps(receipt, indent=2) + '\n')
    print('PASS actual archived ' + args.target + ' updater and rollback')


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Keep an owned native-size display alive only on disposable macOS CI runners."""
import argparse
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
from crossplay import stop_launcher


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stop', action='store_true')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/macos-test-display')
    args = parser.parse_args(); output = args.output.resolve()
    if platform.system() != 'Darwin' or os.environ.get('GITHUB_ACTIONS') != 'true':
        raise RuntimeError('Display setup is restricted to the disposable hosted Mac desktop')
    if args.stop:
        stop_launcher(output); return
    output.mkdir(parents=True)
    binary = output / 'display-helper'
    subprocess.run(['clang', '-fobjc-arc', '-framework', 'AppKit', '-framework', 'CoreGraphics',
                    str(Path(__file__).with_suffix('.m')), '-o', str(binary)], check=True, timeout=60)
    ready = output / 'display.json'
    with (output / 'runtime.log').open('w') as log:
        process = subprocess.Popen([str(binary), str(ready)], stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    (output / 'launcher.json').write_text(json.dumps({'pid': process.pid, 'marker': str(output)}))
    deadline = time.monotonic() + 20
    while not ready.exists():
        if process.poll() is not None or time.monotonic() > deadline: raise RuntimeError('Owned virtual display failed; inspect its retained runtime.log')
        time.sleep(.1)
    print(ready.read_text())


if __name__ == '__main__': main()

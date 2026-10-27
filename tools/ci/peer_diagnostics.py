#!/usr/bin/env python3
"""Replay only the retained native eight-peer failure and retain bounded non-credential diagnostics."""
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
import desktop_packages as desktop
from promote_release import inspect_server


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


def redact(value):
    if isinstance(value, dict): return {key: '<REDACTED>' if 'token' in key.lower() else redact(item) for key, item in value.items()}
    if isinstance(value, list): return [redact(item) for item in value]
    if isinstance(value, str): return re.sub(r'(?i)(token=)[^&\s]+', r'\1<REDACTED>', value)
    return value


def main():
    target = sys.argv[1]
    if target not in desktop.TARGETS: raise ValueError('Unknown target')
    directory = ROOT / 'artifacts'; item = desktop.names(target)
    metadata = json.loads((directory / item['metadata']).read_text())
    desktop.inspect_archive(directory, target, metadata['revision'], metadata['release'])
    inspect_server(directory, metadata['revision'], metadata['release']['version'], metadata['release'])
    verifier = load('peer_verifier', ROOT / 'tools/packaging/verify-desktop.py')
    output = directory / 'native-peer-diagnostics'; output.mkdir()
    staged = output / 'runtime'; staged.mkdir()
    verifier.extract(directory / item['archive'], staged)
    verifier.extract(directory / 'Blast-Circuit-server-dotnet10.zip', staged)
    qa = load('peer_qa', ROOT / 'tools/qa/run.py'); suite = qa.Suite('peer-diagnostic', output, 'unused')
    with suite.server(dll=staged / 'Blast-Circuit-server-dotnet10/Bomber.Server.dll') as origin:
        result = subprocess.run([sys.executable, str(ROOT / 'tools/playtest/run.py'), '--executable',
            str(staged / item['root'] / desktop.TARGETS[target]['executable']), '--scenario', 'peer-capacity',
            '--headless', '--desktop', 'shared', '--server', origin, '--output', str(output)], timeout=150)
    events = []
    for trace in output.glob('*/trace.jsonl'):
        for line in trace.read_text(errors='replace').splitlines():
            event = json.loads(line)
            if event.get('type') == 'peer-capacity-failure': events.append(redact(event))
    (output / 'peer-diagnostic.json').write_text(json.dumps({'mode': 'diagnostic-only', 'target': target,
        'revision': metadata['revision'], 'exitCode': result.returncode, 'events': events}, indent=2) + '\n')
    print(json.dumps({'mode': 'diagnostic-only', 'exitCode': result.returncode, 'events': events}, indent=2))


if __name__ == '__main__': main()

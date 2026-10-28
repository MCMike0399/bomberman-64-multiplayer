#!/usr/bin/env python3
"""Replay focused graphical online journeys on an installed exact payload; diagnostic evidence only."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
import desktop_packages as desktop
import desktop_validation as validation
from promote_release import inspect_server
from peer_diagnostics import redact

def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


def main():
    target, scenarios = sys.argv[1], sys.argv[2:]
    if target not in desktop.TARGETS or not scenarios: raise ValueError('Usage: online_diagnostics.py TARGET SCENARIO...')
    directory = ROOT / 'artifacts'; item = desktop.names(target); settings = desktop.TARGETS[target]
    metadata = json.loads((directory / item['metadata']).read_text())
    desktop.inspect_archive(directory, target, metadata['revision'], metadata['release'])
    inspect_server(directory, metadata['revision'], metadata['release']['version'], metadata['release'])
    verifier = load('online_verifier', ROOT / 'tools/packaging/verify-desktop.py')
    output = directory / 'online-diagnostics'; output.mkdir()
    staged = output / 'runtime'; staged.mkdir()
    verifier.extract(directory / item['archive'], staged)
    verifier.extract(directory / 'Blast-Circuit-server-dotnet10.zip', staged)
    executable = staged / item['root'] / settings['executable']
    if target == 'windows-x64':
        installed = output / 'Installed game with spaces'
        subprocess.run([str(directory / 'Blast-Circuit-windows-x64-Setup.exe'), '/VERYSILENT', '/SUPPRESSMSGBOXES',
            '/NORESTART', '/CURRENTUSER', '/DIR=' + str(installed)], check=True, timeout=180)
        for name, digest in metadata['files'].items():
            if desktop.digest(installed / name) != digest: raise RuntimeError('Installed diagnostic payload differs')
        executable = installed / settings['executable']
    qa = load('online_qa', ROOT / 'tools/qa/run.py')
    runner = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    results = []
    for scenario in scenarios:
        case = output / scenario; case.mkdir()
        suite = qa.Suite('online-diagnostic', case, 'unused', desktop='shared')
        started = time.monotonic()
        # Same owned-server lease as software-rendered graphical parts.
        with suite.server(dll=staged / 'Blast-Circuit-server-dotnet10' / 'Bomber.Server.dll',
                          peer_host_lease=validation.SOFTWARE_PEER_HOST_LEASE) as origin:
            code = subprocess.run([sys.executable, str(ROOT / 'tools/playtest/run.py'), '--executable', str(executable),
                '--scenario', scenario, '--desktop', 'shared', '--server', origin, '--rendering-driver', settings['driver'],
                '--timeout', '5400', '--output', str(case)], timeout=5700).returncode
        summaries = list(case.glob('*/summary.json'))
        summary = json.loads(summaries[0].read_text()) if summaries else {}
        results.append({'scenario': scenario, 'exitCode': code, 'seconds': round(time.monotonic() - started, 1),
                        'passed': summary.get('passed'), 'assertions': summary.get('assertions'),
                        'failure': (summary.get('failure') or '').split('\n')[0] or None})
    report = {'mode': 'diagnostic-only', 'target': target, 'payloadRevision': metadata['revision'], 'runnerRevision': runner,
              'githubSha': os.environ.get('GITHUB_SHA'), 'peerHostLeaseSeconds': validation.SOFTWARE_PEER_HOST_LEASE,
              'results': results}
    (output / 'online-diagnostic.json').write_text(json.dumps(redact(report), indent=2) + '\n')
    print(json.dumps(redact(report), indent=2))


if __name__ == '__main__': main()

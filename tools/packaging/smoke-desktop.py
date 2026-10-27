#!/usr/bin/env python3
"""Run the exact desktop archive on its target OS; distinguish headless and GPU evidence."""
import argparse
import json
from pathlib import Path
import platform
import stat
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import desktop_packages as desktop


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=desktop.TARGETS, required=True)
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    parser.add_argument('--graphical', action='store_true', help='Use an owned desktop and require the intended renderer and captures')
    args = parser.parse_args()
    settings, item = desktop.TARGETS[args.target], desktop.names(args.target)
    if platform.system() != settings['os'] or platform.machine().lower() not in ('x86_64', 'amd64'):
        raise RuntimeError('Exported smoke must run on the target OS and x64 CPU')
    directory = args.directory.resolve()
    metadata = json.loads((directory / item['metadata']).read_text())
    desktop.inspect_archive(directory, args.target, metadata['revision'], metadata['release'])
    with tempfile.TemporaryDirectory(prefix='blast-exported-smoke-') as temporary:
        extracted = Path(temporary)
        with zipfile.ZipFile(directory / item['archive']) as bundle:
            for entry in bundle.infolist():
                # inspect_archive already rejects traversal, links and unexpected files.
                path = Path(bundle.extract(entry, extracted))
                if platform.system() != 'Windows':
                    path.chmod(stat.S_IMODE(entry.external_attr >> 16))
        # Retain failures and real captures after the temporary extracted client is removed.
        output = directory / (item['root'] + '-smoke-details')
        output.mkdir(exist_ok=True)
        previous = set(output.iterdir())
        command = [sys.executable, str(ROOT / 'tools/playtest/run.py'), '--executable',
                   str(extracted / item['root'] / settings['executable']), '--scenario', 'package-smoke',
                   '--output', str(output), '--desktop', 'shared']
        command += ['--rendering-driver', settings['driver']] if args.graphical else ['--headless']
        subprocess.run(command, cwd=ROOT, check=True, timeout=480)
        reports = [folder / 'summary.json' for folder in output.iterdir() if folder not in previous and (folder / 'summary.json').is_file()]
        if len(reports) != 1:
            raise RuntimeError('Missing unique exported smoke report')
        report = json.loads(reports[0].read_text())
        expected = dict(metadata['release'], revision=metadata['revision'])
        if report.get('platform') != settings['os'] or report.get('release') != expected:
            raise RuntimeError('Exported executable release/platform differs from archive metadata')
        if args.graphical and (report.get('renderingDriver') != settings['driver'] or
                               report.get('renderingMethod') != 'forward_plus' or not report.get('screenshots') or
                               any(word in report.get('adapter', '').lower() for word in ('software', 'llvmpipe', 'swiftshader', 'basic render', 'warp'))):
            raise RuntimeError('Graphical smoke did not use the intended native GPU renderer')
        receipt = {'schemaVersion': 1, 'passed': report.get('passed'), 'target': args.target,
                   'revision': metadata['revision'], 'release': metadata['release'],
                   'archiveSha256': desktop.digest(directory / item['archive']), 'os': report['platform'],
                   'scenario': report.get('scenario'), 'assertions': report.get('assertions'),
                   'mode': 'graphical' if args.graphical else 'headless',
                   'driver': report.get('renderingDriver'), 'adapter': report.get('adapter'),
                   'limits': 'Physical controllers, GPU performance and cross-platform Internet play require separate validation.'}
        (directory / item['smoke']).write_text(json.dumps(receipt, indent=2) + '\n')
    print('PASS exported ' + args.target + ' ' + receipt['mode'] + ' smoke')


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Run the full archived game against an owned archived server on its actual target OS."""
import argparse
import contextlib
import importlib.util
import json
import os
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
import desktop_validation as validation
import download_formats as downloads
from promote_release import inspect_server


def extract(archive, destination):
    # Callers validate path/type/inventory before reaching extraction.
    with zipfile.ZipFile(archive) as bundle:
        for entry in bundle.infolist():
            path = Path(bundle.extract(entry, destination))
            if os.name == 'posix' and not entry.is_dir():
                path.chmod(stat.S_IMODE(entry.external_attr >> 16))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=desktop.TARGETS, required=True)
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    parser.add_argument('--graphical', action='store_true', help='Run every graphical gameplay/UI/network journey on an owned desktop')
    parser.add_argument('--software-renderer', action='store_true', help='Allow a bounded two-hour all-suite on a verified CPU renderer; provides no native GPU performance evidence')
    args = parser.parse_args()
    if args.software_renderer and not args.graphical:
        parser.error('--software-renderer requires --graphical')
    item, settings = desktop.names(args.target), desktop.TARGETS[args.target]
    if platform.system() != settings['os'] or platform.machine().lower() not in ('x86_64', 'amd64'):
        raise RuntimeError('Packaged regression must run on its actual target OS and x64 CPU')
    directory = args.directory.resolve()
    metadata = json.loads((directory / item['metadata']).read_text())
    revision, release = metadata['revision'], metadata['release']
    desktop.inspect_archive(directory, args.target, revision, release)
    downloads.inspect(directory, args.target, revision, release)
    inspect_server(directory, revision, release['version'], release=release)
    output = directory / (item['root'] + '-regression-details')
    output.mkdir(exist_ok=True)
    # Reuse the canonical owned Kestrel port/readiness/cleanup lifecycle, without building source.
    spec = importlib.util.spec_from_file_location('desktop_qa', ROOT / 'tools/qa/run.py')
    qa = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(qa)
    cases = []
    with tempfile.TemporaryDirectory(prefix='blast-packaged-regression-') as temporary:
        extracted = Path(temporary)
        extract(directory / item['archive'], extracted)
        extract(directory / 'Blast-Circuit-server-dotnet10.zip', extracted)
        executable = extracted / item['root'] / settings['executable']
        if args.target == 'windows-x64':
            installed = extracted / 'Installed game with spaces'
            subprocess.run([str(directory / downloads.FILES[args.target]), '/VERYSILENT', '/SUPPRESSMSGBOXES',
                            '/NORESTART', '/CURRENTUSER', '/DIR=' + str(installed)], check=True, timeout=180)
            for name, digest in metadata['files'].items():
                if desktop.digest(installed / name) != digest:
                    raise RuntimeError('Installer changed or omitted a packaged file: ' + name)
            executable = installed / settings['executable']
        else:
            executable = directory / downloads.FILES[args.target]
            executable.chmod(0o755)
        suite = qa.Suite('desktop', output, 'unused', desktop='shared')
        server = extracted / 'Blast-Circuit-server-dotnet10' / 'Bomber.Server.dll'
        with suite.server(dll=server) as origin:
            for scenario in validation.GRAPHICAL if args.graphical else validation.HEADLESS:
                previous = set(output.iterdir())
                command = [sys.executable, str(ROOT / 'tools/playtest/run.py'), '--executable',
                           str(executable), '--scenario', scenario,
                           '--output', str(output), '--desktop', 'shared', '--server', origin]
                command += ['--rendering-driver', settings['driver']] if args.graphical else ['--headless']
                timeout=7200 if args.software_renderer and scenario=='all' else 1500
                if args.software_renderer:command += ['--timeout',str(timeout)]
                environment = os.environ.copy()
                if args.target == 'linux-x64':
                    # Modern AppImage runtime supports extraction on CI without a FUSE mount.
                    environment['APPIMAGE_EXTRACT_AND_RUN'] = '1'
                subprocess.run(command, cwd=ROOT, env=environment, check=True, timeout=timeout+300)
                reports = [folder / 'summary.json' for folder in output.iterdir()
                           if folder not in previous and (folder / 'summary.json').is_file()]
                if len(reports) != 1:
                    raise RuntimeError('Missing unique exported regression report: ' + scenario)
                report = json.loads(reports[0].read_text())
                runner = json.loads((reports[0].parent / 'runner-summary.json').read_text())
                if runner.get('passed') is not True or runner.get('exitCode') != 0 or runner.get('errors') != []:
                    raise RuntimeError('Exported runner failed: ' + scenario)
                report['runtimeErrors'] = runner.get('runtimeErrors')
                if 'displayRestart' in runner: report['displayRestart'] = runner['displayRestart']
                validation.inspect_report(report, scenario, settings, revision, release, args.graphical)
                cases.append(report)
        receipt = {'schemaVersion': 2, 'passed': True, 'target': args.target, 'revision': revision,
                   'release': release, 'archiveSha256': desktop.digest(directory / item['archive']),
                   'serverSha256': desktop.digest(directory / 'Blast-Circuit-server-dotnet10.zip'),
                   'os': settings['os'], 'suite': 'graphical-regression' if args.graphical else 'headless-regression',
                   'downloadSha256': desktop.digest(directory / downloads.FILES[args.target]),
                   'cases': cases, 'softwareRendererBudget': args.software_renderer,
                   'limits': 'Physical devices, native GPU performance and mixed-OS Internet play require separate evidence.'}
        validation.inspect_receipt(receipt, settings, revision, release)
        (directory / item['graphical' if args.graphical else 'smoke']).write_text(json.dumps(receipt, indent=2) + '\n')
    print('PASS full packaged ' + args.target + ' ' + receipt['suite'])


if __name__ == '__main__':
    main()

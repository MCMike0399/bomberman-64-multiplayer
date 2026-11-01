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



def rotated_peer_probe(executable, output, origin, failures, validate):
    """One additional observation; the caller's original failures stay latched."""
    output.mkdir(parents=True,exist_ok=True)
    command=[sys.executable,str(ROOT/'tools/playtest/run.py'),'--executable',str(executable),
             '--scenario','peer-capacity','--output',str(output),'--desktop','shared','--server',origin,'--headless']
    before=len(failures);record={'scope':'Reverse logical labels on the same executable/server, new process/guest identities/ports.',
                                'passed':False,'originalFailureCount':before,'command':command}
    try:
        subprocess.run(command,cwd=ROOT,env={**os.environ,'BLAST_QA_PEER_JOIN_ORDER':'reverse'},check=True,timeout=300)
        validate(output,True);record['passed']=True
    except (OSError,ValueError,KeyError,TypeError,RuntimeError,subprocess.SubprocessError)as error:
        record['error']=str(error);failures.append('peer-capacity-rotation: '+str(error))
    record['remainingFailureCount']=len(failures)
    (output/'rotation-runner.json').write_text(json.dumps(record,indent=2)+'\n')
    return record


def repeat(output, scenario, count, scenario_command, receipt_path, revision):
    """Diagnostic loop: COUNT fresh processes of one case. It never writes a regression
    receipt, so it cannot qualify anything; any run that does not pass fails it."""
    output.mkdir(parents=True, exist_ok=True)
    receipt = {'schemaVersion': 1, 'scope': 'Diagnostic repeat; not qualification. Any crash or failure blocks.',
               'scenario': scenario, 'requested': count, 'revision': revision, 'runs': []}
    for index in range(count):
        previous = set(output.iterdir())
        command, environment, timeout = scenario_command(scenario, output)
        row = {'index': index}
        try:
            row['runnerExitCode'] = subprocess.run(command, cwd=ROOT, env=environment, timeout=timeout + 300).returncode
        except subprocess.SubprocessError as error:
            row['runnerError'] = str(error)
        folders = [folder for folder in output.iterdir() if folder not in previous and folder.is_dir()]
        runner = {}
        if len(folders) == 1 and (folders[0] / 'runner-summary.json').is_file():
            row['folder'] = folders[0].name
            runner = json.loads((folders[0] / 'runner-summary.json').read_text())
        capture = runner.get('linuxCrashCapture') or {}
        row.update(passed=runner.get('passed') is True and runner.get('exitCode') == 0 and runner.get('errors') == [],
                   exitCode=runner.get('exitCode'), assertions=runner.get('assertions'), errors=runner.get('errors'),
                   summaryPresent=bool(folders) and (folders[0] / 'summary.json').is_file(),
                   inferredSignal=capture.get('inferredSignal'), seconds=runner.get('seconds'),
                   memoryKiBBefore=capture.get('memoryKiBBefore'), memoryKiBAfter=capture.get('memoryKiBAfter'))
        # A crash is a game process that ended abnormally without writing its summary.
        row['crashed'] = not row['passed'] and (row['exitCode'] not in (0, None) or 'runnerError' in row) and not row['summaryPresent']
        receipt['runs'].append(row)
        receipt_path.write_text(json.dumps(receipt, indent=2) + '\n')
    runs = receipt['runs']
    receipt.update(completed=len(runs), passed=sum(row['passed'] for row in runs), crashed=sum(row['crashed'] for row in runs),
                   failed=sum(not row['passed'] and not row['crashed'] for row in runs))
    receipt['allPassed'] = receipt['passed'] == count
    receipt_path.write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps({key: receipt[key] for key in ('scenario', 'completed', 'passed', 'crashed', 'failed')}))
    if not receipt['allPassed']:
        raise RuntimeError('Diagnostic repeat did not pass every run: ' + scenario)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=desktop.TARGETS, required=True)
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    parser.add_argument('--graphical', action='store_true', help='Run every graphical gameplay/UI/network journey on an owned desktop')
    parser.add_argument('--disposable-desktop', action='store_true', help='Confirm this Windows VM/profile is reserved for installer tests')
    parser.add_argument('--software-renderer', action='store_true', help='Allow bounded multi-hour tours on a verified CPU or Windows WARP renderer; provides no native GPU performance evidence')
    parser.add_argument('--cases', help='Comma-separated graphical cases this runner owns; writes a part receipt for --merge')
    parser.add_argument('--part', help='Unique label for this runner\'s part receipt')
    parser.add_argument('--merge', action='store_true', help='Combine every part receipt into the complete graphical receipt without running the game')
    parser.add_argument('--default-renderer', action='store_true', help='Exercise the Windows launcher default; actual Vulkan remains required by the graphical receipt')
    parser.add_argument('--repeat', metavar='CASE:COUNT', help='Diagnostic only: run one graphical case COUNT times in fresh processes and write a pass/crash receipt; writes no regression receipt and fails if any run does not pass')
    args = parser.parse_args()
    if args.default_renderer and (not args.graphical or args.target != 'windows-x64'):
        parser.error('--default-renderer requires Windows graphical mode')
    if args.software_renderer and not args.graphical:
        parser.error('--software-renderer requires --graphical')
    if (args.cases or args.part or args.merge) and not args.graphical:
        parser.error('--cases/--part/--merge require --graphical')
    if bool(args.cases) != bool(args.part) or args.merge and args.cases:
        parser.error('--cases and --part go together; --merge runs no cases')
    repeat_case, repeat_count = None, 0
    if args.repeat:
        repeat_case, _, count = args.repeat.partition(':')
        if not args.graphical or args.cases or args.merge or repeat_case not in validation.SHARDED_GRAPHICAL \
                or not count.isdigit() or not 1 <= int(count) <= 50:
            parser.error('--repeat needs --graphical, no --cases/--merge, a required graphical case and COUNT 1-50')
        repeat_count = int(count)
    owned = args.cases.split(',') if args.cases else list(validation.SHARDED_GRAPHICAL)
    if args.part and not args.part.replace('-', '').isalnum():
        parser.error('--part must be alphanumeric with dashes')
    if any(case not in validation.SHARDED_GRAPHICAL for case in owned) or len(set(owned)) != len(owned):
        parser.error('--cases must name distinct required graphical cases')
    item, settings = desktop.names(args.target), desktop.TARGETS[args.target]
    # Merging only reads receipts and hashes bytes; it never runs the game.
    if not args.merge and args.target == 'windows-x64' and os.environ.get('GITHUB_ACTIONS') != 'true' and not args.disposable_desktop:
        raise RuntimeError('Windows installer regression requires a disposable VM/profile; use --disposable-desktop only there')
    if not args.merge and (platform.system() != settings['os'] or platform.machine().lower() not in ('x86_64', 'amd64')):
        raise RuntimeError('Packaged regression must run on its actual target OS and x64 CPU')
    directory = args.directory.resolve()
    metadata = json.loads((directory / item['metadata']).read_text())
    revision, release = metadata['revision'], metadata['release']
    desktop.inspect_archive(directory, args.target, revision, release)
    downloads.inspect(directory, args.target, revision, release)
    inspect_server(directory, revision, release['version'], release=release)
    if args.merge:
        parts = [json.loads(path.read_text()) for path in sorted(directory.glob(item['graphical'][:-5] + '-part-*.json'))]
        receipt = validation.merge_parts(parts, settings, revision, release)
        for key, name in (('archiveSha256', item['archive']), ('serverSha256', 'Blast-Circuit-server-dotnet10.zip'),
                          ('downloadSha256', downloads.FILES[args.target])):
            if receipt[key] != desktop.digest(directory / name):
                raise RuntimeError('Graphical parts did not test these exact bytes: ' + name)
        (directory / item['graphical']).write_text(json.dumps(receipt, indent=2) + '\n')
        print('PASS merged ' + str(len(parts)) + ' graphical parts for ' + args.target)
        return
    output = directory / (item['root'] + '-regression-details')
    output.mkdir(exist_ok=True)
    # Reuse the canonical owned Kestrel port/readiness/cleanup lifecycle, without building source.
    spec = importlib.util.spec_from_file_location('desktop_qa', ROOT / 'tools/qa/run.py')
    qa = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(qa)
    cases = []
    with tempfile.TemporaryDirectory(prefix='blast-packaged-regression-') as temporary, contextlib.ExitStack() as cleanup:
        extracted = Path(temporary)
        extract(directory / item['archive'], extracted)
        extract(directory / 'Blast-Circuit-server-dotnet10.zip', extracted)
        executable = extracted / item['root'] / settings['executable']
        if args.target == 'windows-x64':
            installed = extracted / 'Installed game with spaces'
            subprocess.run([str(directory / downloads.FILES[args.target]), '/VERYSILENT', '/SUPPRESSMSGBOXES',
                            '/NORESTART', '/CURRENTUSER', '/DIR=' + str(installed)], check=True, timeout=180)
            cleanup.callback(subprocess.run, [str(installed / 'unins000.exe'), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'],
                             check=True, timeout=180)
            for name, digest in metadata['files'].items():
                if desktop.digest(installed / name) != digest:
                    raise RuntimeError('Installer changed or omitted a packaged file: ' + name)
            executable = installed / settings['executable']
        else:
            executable = directory / downloads.FILES[args.target]
            executable.chmod(0o755)
        suite = qa.Suite('desktop', output, 'unused', desktop='shared')
        server = extracted / 'Blast-Circuit-server-dotnet10' / 'Bomber.Server.dll'
        failures = []
        # Owned server only: on verified software renderers a peer host can draw
        # one frame per second, beyond the production six-second lease. Headless
        # native gates and Mac keep the production lease.
        lease = validation.SOFTWARE_PEER_HOST_LEASE if args.software_renderer else None
        def scenario_command(scenario, destination):
            command = [sys.executable, str(ROOT / 'tools/playtest/run.py'), '--executable',
                       str(executable), '--scenario', scenario,
                       '--output', str(destination), '--desktop', 'shared', '--server', origin]
            if args.graphical:
                if not args.default_renderer:command += ['--rendering-driver', settings['driver']]
            else:command += ['--headless']
            # Software rendering is slow: retained WARP spent ~90 min in
            # graphics-settings alone and ~5 min per stage load. Each tour
            # gets its own process and a bounded allowance below the job limit.
            heavy = scenario in validation.TOURS or scenario == 'menu-preview'
            timeout = (12600 if heavy else 5400) if args.software_renderer else 1500
            if args.software_renderer:command += ['--timeout',str(timeout)]
            environment = os.environ.copy()
            environment['BLAST_QA_PEER_JOIN_ORDER']='normal'
            if args.target == 'linux-x64':
                # Modern AppImage runtime supports extraction on CI without a FUSE mount.
                environment['APPIMAGE_EXTRACT_AND_RUN'] = '1'
            return command, environment, timeout
        with suite.server(dll=server, peer_host_lease=lease) as origin:
            if repeat_case:
                repeat(directory / (item['root'] + '-repeat-' + repeat_case), repeat_case, repeat_count, scenario_command,
                       directory / (item['root'] + '-repeat-' + repeat_case + '.json'), revision)
                return
            for scenario in [case for case in validation.SHARDED_GRAPHICAL if case in owned] if args.graphical else validation.HEADLESS:
                # Keep running this runner's remaining cases after a failure so one
                # native run reports every failing case; the receipt still fails.
                try:
                    previous = set(output.iterdir())
                    command, environment, timeout = scenario_command(scenario, output)
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
                except (RuntimeError, subprocess.SubprocessError) as error:
                    failures.append(scenario + ': ' + str(error))
            if not args.graphical and args.target=='windows-x64'and os.environ.get('BLAST_QA_PEER_ROTATION')=='true':
                rotated_peer_probe(executable,directory/(item['root']+'-peer-rotation-details'),origin,failures,qa.validate_playtest)
            if failures:
                raise RuntimeError('Exported regression failed: ' + '; '.join(failures))
        receipt = {'schemaVersion': 2, 'passed': True, 'target': args.target, 'revision': revision,
                   'release': release, 'archiveSha256': desktop.digest(directory / item['archive']),
                   'serverSha256': desktop.digest(directory / 'Blast-Circuit-server-dotnet10.zip'),
                   'os': settings['os'], 'suite': 'graphical-regression' if args.graphical else 'headless-regression',
                   'downloadSha256': desktop.digest(directory / downloads.FILES[args.target]),
                   'cases': cases, 'softwareRendererBudget': args.software_renderer, 'peerHostLeaseSeconds': lease or 6,
                   'limits': 'Physical devices, native GPU performance and mixed-OS Internet play require separate evidence.'}
        if args.part:
            receipt.update(suite='graphical-part', part=args.part)
            (directory / (item['graphical'][:-5] + '-part-' + args.part + '.json')).write_text(json.dumps(receipt, indent=2) + '\n')
            print('PASS packaged ' + args.target + ' graphical part ' + args.part + ': ' + ', '.join(owned))
            return
        validation.inspect_receipt(receipt, settings, revision, release)
        (directory / item['graphical' if args.graphical else 'smoke']).write_text(json.dumps(receipt, indent=2) + '\n')
    print('PASS full packaged ' + args.target + ' ' + receipt['suite'])


if __name__ == '__main__':
    main()

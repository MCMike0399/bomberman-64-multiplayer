#!/usr/bin/env python3
"""Run two fixed replays through an actual installed desktop download on disposable CI."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import zipfile

import crossplay
import crossplay_runtime
import desktop_packages as desktop
import download_formats
import native_execution

ROOT = Path(__file__).resolve().parents[2]
validate_summary = crossplay_runtime.validate_summary

SPEC = importlib.util.spec_from_file_location('authority_comparer', ROOT / 'tools/qa/compare_authority_replay.py')
comparer = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(comparer)


def assembly_inventory(directory, target):
    """Actual archive bytes, excluding the detached updater's separate modules."""
    archive = directory / ('Blast-Circuit-macos-arm64.zip' if target == 'macos-arm64' else desktop.names(target)['archive'])
    wanted = {'Bomber.Game.dll': set(), 'Bomberman64Multiplayer.dll': set()}
    with zipfile.ZipFile(archive) as bundle:
        for entry in bundle.infolist():
            name = Path(entry.filename).name
            if name in wanted and 'updater' not in Path(entry.filename).parts:
                import hashlib
                wanted[name].add(hashlib.sha256(bundle.read(entry)).hexdigest())
    if any(len(digests) != 1 for digests in wanted.values()):
        raise RuntimeError('Player archive lacks unique main authority/entry assembly inventories')
    return wanted


def run(target, directory, output, expected_revision):
    execution=native_execution.context(expected_revision,target,ROOT)
    if platform.system() != crossplay_runtime.SYSTEMS[target][0]:
        raise RuntimeError('Replay must execute on its actual target OS')
    output.mkdir(parents=True, exist_ok=False)
    metadata = crossplay_runtime.metadata(directory, target)
    if metadata['revision'] != expected_revision: raise RuntimeError('Downloaded payload differs from the explicitly requested source')
    report = {'schemaVersion': 1, 'mode': 'diagnostic-only', 'passed': False, 'target': target,
              'revision': metadata['revision'], 'requestedRevision': expected_revision, 'release': metadata['release'],'executionContext':execution,
              'serverScope': 'No server or renderer; exact LocalMatch ticks through shipped assemblies.', 'runs': [], 'errors': []}
    executable = None
    try:
        executable = crossplay_runtime.install(directory, target, output)
        inventory = assembly_inventory(directory, target)
        archive = directory / ('Blast-Circuit-macos-arm64.zip' if target == 'macos-arm64' else desktop.names(target)['archive'])
        report.update(assemblyInventory={name: next(iter(digests)) for name, digests in inventory.items()},
                      archiveSha256=desktop.digest(archive), downloadSha256=desktop.digest(directory / download_formats.FILES[target]),
                      runnerRevision=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip())
        files = []
        os.environ.pop('APPIMAGE_EXTRACT_AND_RUN', None)  # exercise the normal FUSE path
        for index in range(2):
            case = output / f'run-{index}'
            command = [str(executable), '--headless', '--', '--playtest=authority-replay', '--playtest-output=' + str(case)]
            process = crossplay.launch(command, case, cwd=case)
            try:
                code = process.wait(timeout=120)
            except subprocess.TimeoutExpired:
                crossplay.stop_launcher(case)
                raise RuntimeError('Bounded packaged authority replay timed out')
            finally:
                crossplay.stop_child(process)
            (case / 'launcher.log').rename(case / 'runtime.log')
            summary = json.loads((case / 'summary.json').read_text())
            errors = validate_summary(summary, case, 'authority-replay', True)
            runtime = (case / 'runtime.log').read_text(errors='replace')
            native_errors = [line for line in runtime.splitlines() if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|FATAL:)', line)]
            replay_file = case / 'authority-replay.jsonl.gz'
            warnings = [line for line in runtime.splitlines() if re.search(r'(^|\s)WARNING:', line)]
            item = {'index': index, 'exitCode': code, 'assertions': summary.get('assertions'), 'runtimeErrors': native_errors, 'runtimeWarnings': warnings,
                    'validationErrors': errors, 'file': f'run-{index}/authority-replay.jsonl.gz'}
            report['runs'].append(item)
            if code != 0 or errors or native_errors or summary.get('release') != dict(metadata['release'], revision=metadata['revision']):
                raise RuntimeError('Packaged replay did not produce a complete zero-error matching-release receipt')
            loaded = comparer.load(replay_file); header = loaded['header']; provenance = header['provenance']
            if header['shared']['sourceIdentity'] != metadata['revision'] or provenance['platform'] != crossplay_runtime.SYSTEMS[target][1]:
                raise RuntimeError('Replay source/platform differs from installed payload')
            if (provenance['authorityAssemblySha256'] not in inventory['Bomber.Game.dll'] or
                    provenance['entryAssemblySha256'] not in inventory['Bomberman64Multiplayer.dll']):
                raise RuntimeError('Replay did not bind its loaded MVIDs to the archived authority/entry module hashes')
            item.update(fileSha256=desktop.digest(replay_file), provenance=provenance, records=len(loaded['records']))
            files.append(replay_file)
        comparison, comparison_code = comparer.compare(files[0], files[1], output / 'same-package-repeat')
        if report['runs'][0]['provenance'] != report['runs'][1]['provenance']:
            raise RuntimeError('Fresh-process repeats changed runtime/module provenance')
        report['repeatComparisonSha256'] = desktop.digest(output / 'same-package-repeat/report.json')
        if comparison_code != 0:
            raise RuntimeError('Fresh-process package replay differs; inspect first observable/canary divergence')
        report['passed'] = True
    except (OSError, ValueError, RuntimeError, KeyError, EOFError, subprocess.SubprocessError) as error:
        report['errors'].append(str(error))
    finally:
        if target == 'windows-x64' and executable is not None:
            uninstaller = executable.parent / 'unins000.exe'
            if uninstaller.exists():
                try:
                    subprocess.run([str(uninstaller), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'], check=True, timeout=120)
                except subprocess.SubprocessError as error:
                    report['passed'] = False; report['errors'].append('Owned replay uninstall failed: ' + str(error))
        (output / 'authority-replay-native.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({'target': target, 'passed': report['passed'], 'errors': report['errors']}))
    return 0 if report['passed'] else 1


def aggregate(directory):
    targets = ('macos-arm64', 'windows-x64', 'linux-x64')
    reports = [json.loads((directory / target / 'authority-replay-native.json').read_text()) for target in targets]
    if any(report.get('passed') is not True or report.get('target') != target for target, report in zip(targets, reports)):
        raise RuntimeError('Every native package and both fresh-process repeats must pass')
    if any(report['revision'] != reports[0]['revision'] or report['release'] != reports[0]['release'] for report in reports):
        raise RuntimeError('Native replay cohort used different source/release identities')
    for target, report in zip(targets, reports):
        if report.get('schemaVersion') != 1 or report.get('mode') != 'diagnostic-only' or report.get('requestedRevision') != report['revision']:
            raise RuntimeError('Unsupported native replay receipt')
        for field in ('archiveSha256', 'downloadSha256'):
            comparer.hash_field(report.get(field))
        if len(report.get('runs', [])) != 2: raise RuntimeError('Missing fixed fresh-process repetition')
        for index, run in enumerate(report['runs']):
            expected = f'run-{index}/authority-replay.jsonl.gz'
            if (run.get('file') != expected or run.get('exitCode') != 0 or type(run.get('assertions')) is not int or
                    run['assertions'] <= 0 or run.get('runtimeErrors') != [] or run.get('validationErrors') != []):
                raise RuntimeError('Incomplete native repetition receipt: ' + target)
            file = directory / target / expected
            if desktop.digest(file) != run.get('fileSha256'): raise RuntimeError('Native replay output hash differs: ' + target)
            loaded = comparer.load(file); provenance = loaded['header']['provenance']
            shared = loaded['header']['shared']
            expected_release = {'version': report['release']['version'], 'build': report['release']['build'],
                                'protocol': comparer.Number(str(report['release']['protocolRevision'])), 'token': report['release']['admissionToken']}
            if shared['sourceIdentity'] != report['revision'] or shared['release'] != expected_release or provenance != run.get('provenance'):
                raise RuntimeError('Replay output is detached from native source/release/runtime receipt: ' + target)
            for field, name in [('authorityAssemblySha256', 'Bomber.Game.dll'), ('entryAssemblySha256', 'Bomberman64Multiplayer.dll')]:
                if provenance[field] != report.get('assemblyInventory', {}).get(name):
                    raise RuntimeError('Replay module differs from native archived inventory: ' + target)
        if report['runs'][0]['provenance'] != report['runs'][1]['provenance']:
            raise RuntimeError('Native fresh-process repetitions changed module/runtime identity: ' + target)
        repeated, code = comparer.compare(directory / target / report['runs'][0]['file'],
            directory / target / report['runs'][1]['file'], directory / target / 'aggregate-verified-repeat')
        if code != 0: raise RuntimeError('Native receipt falsely reports repeat equality: ' + target)
    comparisons = []
    for target in targets[1:]:
        reference = directory / targets[0] / reports[0]['runs'][0]['file']
        candidate = directory / target / reports[targets.index(target)]['runs'][0]['file']
        report, code = comparer.compare(reference, candidate, directory / ('mac-vs-' + target))
        comparisons.append({'target': target, 'exitCode': code, 'observableEqual': report['observableEqual'],
                            'canaryEqual': report['canaryEqual'], 'report': 'mac-vs-' + target + '/report.json'})
    receipt = {'schemaVersion': 1, 'mode': 'diagnostic-only', 'revision': reports[0]['revision'], 'release': reports[0]['release'],
               'scope': 'Declared observable traces and separate canaries; not full private state or all possible matches.',
               'passed': all(item['exitCode'] == 0 for item in comparisons), 'targets': list(targets), 'comparisons': comparisons, 'reports': reports}
    (directory / 'authority-replay-comparison.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps({'passed': receipt['passed'], 'comparisons': comparisons}))
    return 0 if receipt['passed'] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=('run', 'aggregate'))
    parser.add_argument('--target', choices=crossplay_runtime.SYSTEMS)
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--expected-revision')
    args = parser.parse_args()
    if args.operation == 'aggregate': return aggregate(args.directory.resolve())
    if not args.target or not args.output or not re.fullmatch('[0-9a-f]{40}', args.expected_revision or ''):
        parser.error('run requires --target, --output and an exact --expected-revision')
    return run(args.target, args.directory.resolve(), args.output.resolve(), args.expected_revision)


if __name__ == '__main__':
    sys.exit(main())

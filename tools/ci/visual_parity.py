#!/usr/bin/env python3
"""Collect the unchanged full High fixture from installed packages; compare without tolerances."""
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

import authority_replay
import crossplay
import crossplay_runtime
import desktop_packages as desktop
import download_formats

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('pixel_comparer', ROOT / 'tools/qa/compare_visual_parity.py')
pixels = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(pixels)
# Interior distant village: the old broad rectangle included reflected water.
# Whole-image differences remain reported independently of these feature controls.
ROIS = [dict(name='focused-stone', x=365, y=580, width=80, height=60),
        dict(name='far-scenery', x=440, y=0, width=280, height=24),
        dict(name='water', x=950, y=300, width=300, height=460)]


def pair(left, right, output, controlled=True):
    return pixels.compare(left, right, output,
        reference_manifest=left.with_suffix('.manifest.json') if controlled else None,
        candidate_manifest=right.with_suffix('.manifest.json') if controlled else None,
        diagnostic_only=not controlled)


def effect_control_valid(report, name, exit_code):
    if exit_code != 3 or report.get('encodingCompatible') is not True: return False
    regions = {row['rectangle']['name']: row for row in report.get('rois', [])}
    if set(regions) != {'focused-stone', 'far-scenery', 'water'}: return False
    if name == 'dof':
        return regions['far-scenery']['differingPixels'] > 0 and regions['focused-stone']['exactEqual'] is True
    if name == 'mirror':
        return regions['water']['differingPixels'] > 0 and regions['far-scenery']['exactEqual'] is True
    return False


def compare_captures(case, label, report):
    """Retain every bounded comparison even when an earlier feature is broken."""
    for name, right in [('same-draw', 'parity-base-a-1.png'), ('fresh-target', 'parity-base-b-0.png')]:
        _, exit_code = pair(case / 'parity-base-a-0.png', case / right, case / name)
        report['comparisons'].append({'name': f'{label}/{name}', 'exitCode': exit_code})
        if exit_code != 0:
            report['errors'].append(f'{label}: Controlled full High baseline does not repeat: {name}')
    rois = case / 'control-rois.json'; rois.write_text(json.dumps(ROIS))
    for name, right in [('dof', 'parity-dof-off-0.png'), ('mirror', 'parity-reflection-off-0.png')]:
        control, exit_code = pixels.compare(case / 'parity-base-a-0.png', case / right, case / ('control-' + name),
                                           rois=rois, diagnostic_only=True)
        valid = effect_control_valid(control, name, exit_code)
        report['comparisons'].append({'name': f'{label}/control-{name}', 'passed': valid})
        if not valid: report['errors'].append(f'{label}: Original High effect control failed: {name}')


def run(target, directory, output, expected):
    if os.environ.get('GITHUB_ACTIONS') != 'true' or platform.system() != crossplay_runtime.SYSTEMS[target][0]:
        raise RuntimeError('Visual installer qualification requires its disposable native CI OS')
    output.mkdir(parents=True, exist_ok=False)
    data = crossplay_runtime.metadata(directory, target)
    if data['revision'] != expected: raise RuntimeError('Visual payload differs from requested source')
    report = {'schemaVersion': 1, 'mode': 'diagnostic-only', 'passed': False, 'target': target, 'revision': expected,
              'release': data['release'], 'runs': [], 'comparisons': [], 'errors': []}
    executable = None
    try:
        executable = crossplay_runtime.install(directory, target, output)
        inventory = authority_replay.assembly_inventory(directory, target)
        archive = directory / ('Blast-Circuit-macos-arm64.zip' if target == 'macos-arm64' else desktop.names(target)['archive'])
        engine_name = 'Blast Circuit' if target == 'macos-arm64' else desktop.TARGETS[target]['executable']
        with zipfile.ZipFile(archive) as bundle:
            engines = [entry for entry in bundle.infolist() if Path(entry.filename).name == engine_name]
            if len(engines) != 1: raise RuntimeError('Player archive lacks unique engine executable')
            import hashlib
            engine_sha = hashlib.sha256(bundle.read(engines[0])).hexdigest()
        provenance = {'sourceRevision': expected, 'entryAssemblySha256': next(iter(inventory['Bomberman64Multiplayer.dll'])),
                      'packageSha256': desktop.digest(archive), 'templateSha256': engine_sha,
                      'templateHashMeaning': 'Actual exported engine executable bytes, including embedded payload when present.'}
        provenance_file = output / 'provenance.json'; provenance_file.write_text(json.dumps(provenance))
        report.update(archiveSha256=desktop.digest(archive), downloadSha256=desktop.digest(directory / download_formats.FILES[target]))
        os.environ.pop('APPIMAGE_EXTRACT_AND_RUN', None)
        for index in range(2):
            case = output / f'run-{index}'
            command = [str(executable), '--rendering-driver', crossplay_runtime.SYSTEMS[target][2], '--windowed', '--max-fps', '30',
                       '--fixed-fps', '30', '--disable-vsync', '--', '--display-windowed', '--playtest=visual-parity',
                       '--playtest-output=' + str(case), '--parity-provenance=' + str(provenance_file)]
            process = crossplay.launch(command, case, cwd=case)
            try: code = process.wait(timeout=900)
            except subprocess.TimeoutExpired:
                crossplay.stop_launcher(case); raise RuntimeError('Full High fixture exceeded its bounded software-rendering budget')
            finally: crossplay.stop_child(process)
            (case / 'launcher.log').rename(case / 'runtime.log')
            summary = json.loads((case / 'summary.json').read_text())
            errors = crossplay_runtime.validate_summary(summary, case, 'visual-parity', False)
            text = (case / 'runtime.log').read_text(errors='replace')
            runtime_errors = [line for line in text.splitlines() if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|FATAL:)', line)]
            report['runs'].append({'index': index, 'exitCode': code, 'assertions': summary.get('assertions'), 'validationErrors': errors,
                                   'runtimeErrors': runtime_errors, 'runtimeWarnings': [line for line in text.splitlines() if 'WARNING:' in line]})
            if code or errors or runtime_errors or summary.get('release') != dict(data['release'], revision=expected):
                raise RuntimeError('Full High fixture failed native renderer/evidence checks')
            if summary.get('renderingDriver') != crossplay_runtime.SYSTEMS[target][2] or summary.get('renderingMethod') != 'forward_plus':
                raise RuntimeError('Full High fixture used an unexpected renderer')
            compare_captures(case, f'run-{index}', report)
        repeated, code = pair(output / 'run-0/parity-base-a-0.png', output / 'run-1/parity-base-a-0.png', output / 'fresh-process')
        report['comparisons'].append({'name': 'fresh-process', 'exitCode': code})
        if code != 0: report['errors'].append('Controlled full High differs across fresh processes')
        report['passed'] = not report['errors']
    except (OSError, ValueError, RuntimeError, KeyError, TypeError, subprocess.SubprocessError) as error:
        report['errors'].append(str(error))
    finally:
        if target == 'windows-x64' and executable is not None and (executable.parent / 'unins000.exe').exists():
            try: subprocess.run([str(executable.parent / 'unins000.exe'), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'], check=True, timeout=120)
            except subprocess.SubprocessError as error:
                report['passed'] = False; report['errors'].append('Owned uninstall failed: ' + str(error))
        (output / 'visual-parity-native.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({'target': target, 'passed': report['passed'], 'errors': report['errors']}))
    return 0 if report['passed'] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=crossplay_runtime.SYSTEMS, required=True)
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--expected-revision', required=True)
    args = parser.parse_args()
    if not re.fullmatch('[0-9a-f]{40}', args.expected_revision): parser.error('Expected an exact source SHA')
    return run(args.target, args.directory.resolve(), args.output.resolve(), args.expected_revision)


if __name__ == '__main__': sys.exit(main())

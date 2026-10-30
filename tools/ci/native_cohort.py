"""Free exact packaged replay and captured-input checks; no graphics qualification."""
import argparse
import json
import re
from pathlib import Path
import shutil

import authority_replay
import mesh_inputs


def collection(directory, target, stage):
    report = json.loads((directory / 'mesh-inputs-native.json').read_text())
    mesh_inputs.require(isinstance(report, dict), 'Invalid native stage receipt')
    mesh_inputs.require(report.get('passed') is True and report.get('graphicsQualified') is False and
                        report.get('nativeCI') is True and report.get('mode') == 'mesh-inputs-diagnostic' and
                        report.get('target') == target and report.get('stage') == stage and report.get('errors') == [],
                        'Missing/failed native stage collection: ' + target + '/' + stage)
    mesh_inputs.require(re.fullmatch('[0-9a-f]{40}', report.get('revision', '')) is not None,
                        'Native stage source is missing')
    for key in ('archiveSha256', 'downloadSha256'):
        mesh_inputs.require(re.fullmatch('[0-9a-f]{64}', report.get(key, '')) is not None, 'Missing native byte identity')
    runs = report.get('runs', [])
    mesh_inputs.require(len(runs) == 1 and runs[0].get('exitCode') == 0 and
                        runs[0].get('validationErrors') == [] and runs[0].get('runtimeErrors') == [] and
                        runs[0].get('meshInputsValidated') is True and runs[0].get('sceneMetadataValidated') is True and
                        type(runs[0].get('assertions')) is int and runs[0]['assertions'] > 0,
                        'Incomplete native stage execution')
    return {key: report[key] for key in ('revision', 'archiveSha256', 'downloadSha256')}


def compare_inputs(directory, output):
    """All seven stages require all three native collections and exact inputs."""
    directory = Path(directory); output = Path(output); output.mkdir(parents=True, exist_ok=False)
    targets = ('macos-arm64', 'windows-x64', 'linux-x64')
    inputs = []; stages = []; bindings = {}
    for stage in mesh_inputs.STAGES:
        folders = {t: directory / ('native-contracts-' + t) / 'inputs' / stage for t in targets}
        valid = {}; errors = {}
        for target, folder in folders.items():
            try:
                identity = collection(folder, target, stage)
                replay = json.loads((directory / ('native-contracts-' + target) / 'replay/authority-replay-native.json').read_text())
                mesh_inputs.require(replay.get('passed') is True and
                                    identity == {key: replay.get(key) for key in identity},
                                    'Replay/input source/archive/download identity differs')
                mesh_inputs.require(target not in bindings or bindings[target] == identity,
                                    'Stage changed source/archive/download identity')
                bindings[target] = identity; valid[target] = identity
            except (OSError, ValueError, KeyError, TypeError) as error:
                errors[target] = str(error)
        comparisons = []
        for target in targets[1:]:
            row = {'target': target, 'stage': stage, 'rawUnusedBuilderExitCode': None,
                   'exactCapturedInputExitCode': 2}
            try:
                mesh_inputs.require(not errors, 'Missing/invalid stage inventories: ' + json.dumps(errors, sort_keys=True))
                mesh_inputs.require(len({v['revision'] for v in valid.values()}) == 1, 'Stage native source identities differ')
                reference = folders[targets[0]] / 'run-0'; candidate = folders[target] / 'run-0'
                destination = output / stage / target
                captured = mesh_inputs.compare_captured(reference, candidate, destination / 'captured',
                                                       stage=stage, expected_revision=valid[target]['revision'])
                raw = mesh_inputs.compare(reference, candidate, destination / 'unused-builders')
                row.update(exactCapturedInputExitCode=captured, rawUnusedBuilderExitCode=raw)
            except (OSError, ValueError, KeyError, TypeError) as error:
                row['error'] = str(error)
            comparisons.append(row); inputs.append(row)
        stages.append({'stage': stage, 'passed': len(valid) == 3 and all(r['exactCapturedInputExitCode'] == 0 for r in comparisons),
                       'collectionErrors': errors, 'comparisons': comparisons})
    result = {'passed': all(s['passed'] for s in stages), 'graphicsQualified': False,
              'requiredStages': list(mesh_inputs.STAGES), 'stages': stages, 'inputs': inputs, 'bindings': bindings}
    (output / 'stages.json').write_text(json.dumps(result, indent=2) + '\n')
    return result


def aggregate(directory,output):
    directory=Path(directory);output=Path(output);output.mkdir(parents=True,exist_ok=False)
    for target in ('macos-arm64','windows-x64','linux-x64'):
        source=directory/('native-contracts-'+target)/'replay'
        shutil.copytree(source,output/'replay'/target,ignore=shutil.ignore_patterns('installed'))
    replay=authority_replay.aggregate(output/'replay',observable_required=True)
    scenes = compare_inputs(directory, output / 'scene-inputs')
    result={'schemaVersion':1,'passed':replay==0 and scenes['passed'],
            'graphicsQualified':False,'replayExitCode':replay,'inputs':scenes['inputs'],'stages':scenes['stages'],'requiredStages':scenes['requiredStages'],
            'scope':'Exact shipped observable replay and captured CPU inputs only; raw independent probes retained, no native GPU/image claim.'}
    (output/'cohort.json').write_text(json.dumps(result,indent=2)+'\n');return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--directory',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();result=aggregate(args.directory,args.output)
    raise SystemExit(0 if result['passed']else 1)

"""Temporary public-API diagnostic overlay; never edits compiled game or shaders."""
from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
CONTROLS = ('ssil', 'ssao', 'glow', 'shadows', 'msaa')


@contextmanager
def overlay(executable, output, control):
    if control not in CONTROLS:
        raise ValueError('Unknown diagnostic effect control')
    config = executable.parent / 'override.cfg'
    if config.exists() or config.is_symlink():
        raise ValueError('Refuse to replace an existing installation override')
    script = output / 'effect-controller.gd'
    scene = output / 'effect-controller.tscn'
    shutil.copy2(ROOT / 'tools/qa/frozen_effect_controller.gd', script)
    scene.write_text('[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path=' +
                     json.dumps(script.resolve().as_posix()) + ' id="1"]\n[node name="Controller" type="Node"]\nscript = ExtResource("1")\n')
    contents = '[application]\nrun/main_scene=' + json.dumps(scene.resolve().as_posix()) + '\n'
    config.write_text(contents)
    digest = hashlib.sha256(config.read_bytes()).hexdigest()
    try:
        yield {'mode':'external-effect-control', 'control':control, 'graphicsQualified':False,
               'scriptSha256':hashlib.sha256(script.read_bytes()).hexdigest(), 'overrideSha256':digest,
               'scope':'Original executable/PCK/managed assembly unchanged; diagnostic setting changes only'}
    finally:
        if not config.is_file() or config.is_symlink() or hashlib.sha256(config.read_bytes()).hexdigest() != digest:
            raise RuntimeError('Diagnostic override ownership changed; retain and report instead of deleting')
        config.unlink()


def run(target, directory, output, revision, driver, control):
    import crossplay, crossplay_runtime, download_formats, mesh_inputs, visual_parity
    if target != 'windows-x64' or control not in CONTROLS or driver not in ('d3d12', 'vulkan'):
        raise ValueError('Effect bisection is an explicit Windows-only diagnostic')
    output.mkdir(parents=True, exist_ok=False)
    metadata = crossplay_runtime.metadata(directory, target)
    if metadata['revision'] != revision: raise ValueError('Wrong immutable payload')
    download = download_formats.inspect(directory, target, revision, metadata['release'])
    report = {'scope':'Diagnostic external effect control; never original High qualification',
              'graphicsQualified':False, 'source':revision, 'control':control, 'driver':driver,
              'download':download, 'runs':[], 'comparisons':[], 'errors':[]}
    executable = None
    try:
        executable = crossplay_runtime.install(directory, target, output)
        with overlay(executable, output, control) as manifest:
            report['controller'] = manifest
            for index in range(2):
                case = output / f'run-{index}'
                command = [str(executable),'--rendering-driver',driver,'--windowed','--max-fps','30',
                           '--fixed-fps','30','--disable-vsync','--','--display-windowed',
                           '--playtest=visual-parity','--playtest-output='+str(case),'--qa-bisect-control='+control]
                process = crossplay.launch(command, case, cwd=case)
                try: code = process.wait(timeout=300)
                finally: crossplay.stop_child(process)
                (case/'launcher.log').rename(case/'runtime.log')
                summary = json.loads((case/'summary.json').read_text())
                errors = crossplay_runtime.validate_summary(summary, case, 'visual-parity', False)
                text = (case/'runtime.log').read_text(errors='replace')
                native_errors = [line for line in text.splitlines() if 'ERROR:' in line]
                observation = json.loads((case/'visual-parity-observations.json').read_text())
                hook = json.loads((case/'effect-control.json').read_text())
                mesh_inputs.load(case)
                if code or errors or native_errors or hook.get('control') != control or not hook.get('observations'):
                    raise RuntimeError('Diagnostic control failed native execution; retain logs')
                if observation['driver'] != driver or not visual_parity.hardware_adapter_matches(observation, 'T4'):
                    raise RuntimeError('Diagnostic control selected the wrong API/hardware')
                rows = hook['observations']
                if control in ('ssil','ssao','glow') and any(row[control+'After'] for row in rows):
                    raise RuntimeError('Requested environment omission was not applied')
                if control == 'msaa' and any(row['msaaAfter'] != 0 for row in rows):
                    raise RuntimeError('Requested MSAA omission was not applied')
                if control == 'shadows' and any(any(row['shadowsAfter']) for row in rows):
                    raise RuntimeError('Requested shadow omission was not applied')
                report['runs'].append({'exitCode':code,'device':observation['adapter'],'driver':observation['driver'],
                                       'runtimeErrors':native_errors,'assertions':summary['assertions'],
                                       'appliedDraws':len(rows), 'controllerReceipt':str(case/'effect-control.json')})
                for name, right in [('same-draw','parity-base-a-1.png'),('fresh-target','parity-base-b-0.png')]:
                    result, comparison_code = visual_parity.pixels.compare(case/'parity-base-a-0.png',case/right,
                                                                         case/name,diagnostic_only=True)
                    report['comparisons'].append({'name':f'run-{index}/'+name,'exactEqual':result['exactEqual'],
                                                  'diagnosticExitCode':comparison_code})
            result, code = visual_parity.pixels.compare(output/'run-0/parity-base-a-0.png',output/'run-1/parity-base-a-0.png',
                                                        output/'fresh-process',diagnostic_only=True)
            report['comparisons'].append({'name':'fresh-process','exactEqual':result['exactEqual'],'diagnosticExitCode':code})
    except (OSError, ValueError, RuntimeError, KeyError, subprocess.SubprocessError) as error:
        report['errors'].append(str(error))
    finally:
        if executable is not None and (executable.parent/'unins000.exe').is_file():
            result = subprocess.run([str(executable.parent/'unins000.exe'),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART'],timeout=120)
            if result.returncode: report['errors'].append('Owned diagnostic installation did not uninstall')
        report['collectionComplete'] = len(report['runs']) == 2 and not report['errors']
        report['repeatExact'] = bool(report['comparisons']) and all(row['exactEqual'] for row in report['comparisons'])
        (output/'effect-bisection.json').write_text(json.dumps(report,indent=2)+'\n')
    return 0 if report['collectionComplete'] else 1


if __name__ == '__main__':
    import argparse
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target',required=True);parser.add_argument('--expected-revision',required=True)
    parser.add_argument('--directory',type=Path,default=ROOT/'artifacts');parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--driver',choices=('d3d12','vulkan'),default='d3d12');parser.add_argument('--control',choices=CONTROLS,required=True)
    args=parser.parse_args()
    raise SystemExit(run(args.target,args.directory.resolve(),args.output.resolve(),args.expected_revision,args.driver,args.control))

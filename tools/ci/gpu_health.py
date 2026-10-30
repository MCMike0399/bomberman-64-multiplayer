"""Read the actual game's health on a selected Windows API; no product default change."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import uuid
import crossplay_runtime
import desktop_packages as desktop


def run(directory, output, revision, driver, default_driver=False):
    output.mkdir(parents=True,exist_ok=False)
    data=crossplay_runtime.metadata(directory,'windows-x64')
    if data['revision']!=revision: raise ValueError('Wrong frozen payload')
    executable=None;stage=None
    try:
        executable=crossplay_runtime.install(directory,'windows-x64',output)
        token=uuid.uuid4().hex;stage=executable.parent.parent/('.blast-update-'+token);stage.mkdir()
        preferences=output/'preferences.cfg'
        preferences.write_text('[player]\nautomaticUpdates=false\n[video]\ngraphicsPreset="high"\nfullscreen=false\n')
        command=[str(executable),*([] if default_driver else ['--rendering-driver',driver]),'--windowed','--','--display-windowed',
                 '--smoke-menu','--smoke-seconds=4','--smoke-preferences='+str(preferences),
                 '--smoke-output='+str(output),'--update-token='+token]
        with (output/'runtime.log').open('w') as log:
            result=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,timeout=180)
        health=json.loads((stage/'health.json').read_text())
        shutdown=json.loads((output/'shutdown.json').read_text())
        text=(output/'runtime.log').read_text(errors='replace')
        passed=(result.returncode==0 and health['driver']==driver and 't4' in health['adapter'].lower() and
                health['method']=='forward_plus' and health['graphics']=='high' and
                shutdown['sourceRevision']==revision and shutdown['savedStateFlushed'] and shutdown['drain']['completed'] and
                'ERROR:' not in text and 'GODOT_SMOKE_OK' in text)
        report={'scope':'Diagnostic actual-game API/device health; no production API selection',
                'passed':passed,'source':revision,'driver':driver,'health':health,'shutdown':shutdown,
                'driverOverride':None if default_driver else driver,
                'executableSha256':desktop.digest(executable),'command':command,'exitCode':result.returncode}
        (output/'gpu-health.json').write_text(json.dumps(report,indent=2)+'\n')
        return 0 if passed else 1
    finally:
        if stage is not None and stage.is_dir():shutil.rmtree(stage)
        if executable is not None and (executable.parent/'unins000.exe').is_file():
            subprocess.run([str(executable.parent/'unins000.exe'),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART'],check=True,timeout=120)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory',type=Path,default=Path('artifacts'));parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--expected-revision',required=True);parser.add_argument('--driver',choices=('d3d12','vulkan'),required=True)
    parser.add_argument('--default-driver',action='store_true',help='Observe the selected default without an engine API override')
    args=parser.parse_args();raise SystemExit(run(args.directory.resolve(),args.output.resolve(),args.expected_revision,args.driver,args.default_driver))

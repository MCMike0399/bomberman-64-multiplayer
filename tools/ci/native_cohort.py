"""Free exact packaged replay and captured-input checks; no graphics qualification."""
import argparse
import json
from pathlib import Path
import shutil

import authority_replay
import mesh_inputs


def aggregate(directory,output):
    directory=Path(directory);output=Path(output);output.mkdir(parents=True,exist_ok=False)
    for target in ('macos-arm64','windows-x64','linux-x64'):
        source=directory/('native-contracts-'+target)/'replay'
        shutil.copytree(source,output/'replay'/target)
    replay=authority_replay.aggregate(output/'replay',observable_required=True)
    inputs=[]
    reference=directory/'native-contracts-macos-arm64/inputs/run-0'
    for target in ('windows-x64','linux-x64'):
        candidate=directory/('native-contracts-'+target)/'inputs/run-0'
        raw=mesh_inputs.compare(reference,candidate,output/('unused-builder-diagnostics-'+target))
        captured=mesh_inputs.compare_captured(reference,candidate,output/('captured-inputs-'+target))
        inputs.append({'target':target,'rawUnusedBuilderExitCode':raw,'exactCapturedInputExitCode':captured})
    result={'schemaVersion':1,'passed':replay==0 and all(row['exactCapturedInputExitCode']==0 for row in inputs),
            'graphicsQualified':False,'replayExitCode':replay,'inputs':inputs,
            'scope':'Exact shipped observable replay and captured CPU inputs only; raw independent probes retained, no native GPU/image claim.'}
    (output/'cohort.json').write_text(json.dumps(result,indent=2)+'\n');return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--directory',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();result=aggregate(args.directory,args.output)
    raise SystemExit(0 if result['passed']else 1)

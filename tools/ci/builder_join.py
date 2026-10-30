"""Join this run's immutable state/evidence artifacts for pure finalization."""
import argparse
from pathlib import Path
import shutil


def join(directory,output):
    directory=Path(directory);output=Path(output)
    if output.exists():raise FileExistsError('Do not overwrite earlier retained state')
    states=list(directory.glob('builder-state-*-final'));evidence=list(directory.glob('builder-evidence-*'))
    if len(states)!=1 or len(evidence)!=1:raise ValueError('Exact final state and evidence artifacts are required')
    for source in (*states,*evidence):
        if any(path.is_symlink()for path in source.rglob('*')):raise ValueError('Artifact symlinks are not retained source authority')
    shutil.copytree(states[0],output)
    for name in ('evidence','observations'):
        source=evidence[0]/name
        if source.exists():shutil.copytree(source,output/name,dirs_exist_ok=False)
    return output


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--artifacts',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();join(args.artifacts,args.output)

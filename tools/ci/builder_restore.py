"""Recover missing critical state from this job's previously retained artifact."""
import argparse
import json
import os
from pathlib import Path
import shutil

import builder_namespace as names


def restore(state,backup,context):
    state=Path(state);backup=Path(backup)
    plan=json.loads((backup/'plan.json').read_text())
    if (plan.get('account')!=names.ACCOUNT or plan.get('region')!=names.REGION or
            any(plan.get(key)!=context.get(variable)for key,variable in (
                ('controllerRunId','GITHUB_RUN_ID'),('controllerRunAttempt','GITHUB_RUN_ATTEMPT'),('controllerJobId','GITHUB_JOB')))):
        raise ValueError('Recovery artifact is not this exact approved job')
    state.mkdir(parents=True,exist_ok=True);restored=[]
    for name in ('plan.json','template.json','prepared.json','protected-instances.json','creation-manifest.jsonl'):
        source=backup/name;destination=state/name
        if source.is_symlink():raise ValueError('Recovery symlinks cannot authorize cleanup')
        if not destination.exists()and source.is_file():shutil.copyfile(source,destination);restored.append(name)
    (state/'state-restored.json').write_text(json.dumps({'restoredFiles':restored,'scope':'Missing files only; existing later journal entries preserved.'},indent=2)+'\n')
    return restored


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--state',type=Path,required=True)
    parser.add_argument('--backup',type=Path,required=True);args=parser.parse_args();restore(args.state,args.backup,os.environ)

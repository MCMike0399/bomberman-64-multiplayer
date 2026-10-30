"""Queue major qualification from successful current-main Verification; patches stay free."""
import datetime
import json
import os
from pathlib import Path
import subprocess

import release_gpu_policy
import release_identity

REPOSITORY='MCMike0399/bomberman-64-multiplayer'


def eligible(run,source):
    return (run.get('head_repository',{}).get('full_name')==REPOSITORY and run.get('head_sha')==source and
            run.get('head_branch')=='main'and run.get('path')=='.github/workflows/ci.yml'and
            run.get('event')in ('push','workflow_dispatch')and run.get('status')=='completed'and run.get('conclusion')=='success')


if __name__=='__main__':
    def gh(*args):return subprocess.check_output(['gh',*args],text=True,timeout=120)
    mode=os.environ.get('REQUESTED_MODE','audit');run_id=os.environ.get('VERIFICATION_RUN','')
    if mode not in ('audit','rehearse','qualify'):raise SystemExit('Unknown builder mode')
    source=gh('api',f'repos/{REPOSITORY}/git/ref/heads/main');source=json.loads(source)['object']['sha']
    checkout=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
    approved=True;reason='Explicit free audit'
    if mode!='audit':
        if not run_id.isdigit():raise SystemExit('An exact successful Verification run is required')
        run=json.loads(gh('api',f'repos/{REPOSITORY}/actions/runs/{run_id}'))
        approved=checkout==source and eligible(run,source);reason='Current-main Verification'if approved else'Superseded or untrusted Verification'
        if approved and mode=='qualify':
            try:policy=release_gpu_policy.derive(REPOSITORY,release_identity.check_exports()['version'],gh)
            except ValueError as error:approved=False;reason=str(error)
            else:approved=policy['requiresGpu'];reason='Major/minor qualification'if approved else'Patch uses free functional/package gates'
    expiry=os.environ.get('FIXED_EXPIRY','')or(datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(minutes=160)).isoformat()
    parsed=datetime.datetime.fromisoformat(expiry)
    if parsed.tzinfo is None:raise SystemExit('Fixed expiry must have a UTC offset')
    outputs={'run':str(approved).lower(),'mode':mode,'source':source,'verification-run-id':run_id,'fixed-expiry':parsed.astimezone(datetime.timezone.utc).isoformat()}
    with Path(os.environ['GITHUB_OUTPUT']).open('a')as output:
        for key,value in outputs.items():output.write(key+'='+value+'\n')
    with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a')as summary:
        summary.write(f'Builder selection: {reason}. Source `{source}`. Mode `{mode}`. Fixed expiry `{outputs["fixed-expiry"]}`.\n')

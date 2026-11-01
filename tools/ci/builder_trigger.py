"""Select major qualification from successful current-main Verification; patches stay free.
Paid selection needs no human click: builder_guard dedupes and fails closed on cleanup/spend."""
import datetime
import json
import os
from pathlib import Path
import subprocess

import builder_approval
import builder_guard
import release_gpu_policy
import release_identity
import render_fingerprint

REPOSITORY='MCMike0399/bomberman-64-multiplayer'
MODES=('audit','rehearse','permissions-smoke','qualify')
REQUIRED_ARTIFACTS={'Blast-Circuit-server-dotnet10','Blast-Circuit-macos-arm64-ad-hoc','Blast-Circuit-macos-arm64-regression',
                    'managed-receipts','headless-receipts','release-gate-startup','release-gate-update','release-gate-crossplay','native-contract-comparison',
                    *(f'Blast-Circuit-{target}'+suffix for target in ('windows-x64','linux-x64')for suffix in ('','-download','-native','-smoke'))}


def eligible(run,source):
    return (run.get('head_repository',{}).get('full_name')==REPOSITORY and run.get('head_sha')==source and
            run.get('head_branch')=='main'and run.get('path')=='.github/workflows/ci.yml'and
            run.get('event')in ('push','workflow_dispatch')and run.get('status')=='completed'and run.get('conclusion')=='success')


def free_selection(mode,checkout,source):
    """Audit and permissions-smoke need no Verification run and ignore the paid hold."""
    if mode=='permissions-smoke':
        # No instance boots, so no prepared GPU payload is needed; the smoke still
        # exercises the real current-main template and controller code.
        return (True,'FREE permissions smoke (no instances, dry-run launches)')if checkout==source else(False,'Superseded permissions-smoke checkout')
    return True,'Explicit free audit'


def complete_artifacts(artifacts):
    names={row['name']for row in artifacts if not row.get('expired')}
    return REQUIRED_ARTIFACTS<=names


if __name__=='__main__':
    def gh(*args):return subprocess.check_output(['gh',*args],text=True,timeout=120)
    mode=os.environ.get('REQUESTED_MODE','audit');run_id=os.environ.get('VERIFICATION_RUN','')
    if mode not in MODES:raise SystemExit('Unknown builder mode')
    source=gh('api',f'repos/{REPOSITORY}/git/ref/heads/main');source=json.loads(source)['object']['sha']
    checkout=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
    approved,reason=free_selection(mode,checkout,source)
    if mode not in ('audit','permissions-smoke'):
        if not run_id.isdigit():raise SystemExit('An exact successful Verification run is required')
        run=json.loads(gh('api',f'repos/{REPOSITORY}/actions/runs/{run_id}'))
        approved=checkout==source and eligible(run,source);reason='Current-main Verification'if approved else'Superseded or untrusted Verification'
        if approved:
            pages=json.loads(gh('api',f'repos/{REPOSITORY}/actions/runs/{run_id}/artifacts?per_page=100','--paginate','--slurp'))
            approved=complete_artifacts([item for page in pages for item in page['artifacts']])
            if not approved:reason='Incomplete native artifact set; docs-only or skipped lanes cannot supply GPU inputs'
        if approved and mode=='qualify':
            try:policy=release_gpu_policy.derive(REPOSITORY,release_identity.check_exports()['version'],gh)
            except ValueError as error:approved=False;reason=str(error)
            else:approved=policy['requiresGpu'];reason='Major/minor qualification'if approved else'Patch uses free functional/package gates'
    if mode=='qualify' and os.environ.get('SANCUS_BUILDER_PAID_HOLD')!='false':
        approved=False;reason='Paid qualification hold: rehearsal and plan review must finish first'
    qualification={}
    if approved and mode=='qualify':
        # No human click: dedupe, zero-leftover outcome and recorded-spend guards decide.
        qualification=builder_guard.key(release_identity.check_exports()['version'],render_fingerprint.calculate()['sha256'])
        refusal=builder_guard.refusal(qualification,os.environ.get('GITHUB_EVENT_NAME')=='workflow_run',os.environ.get('DIAGNOSED_FAILURE_RUN',''),
                                      builder_approval.ledger(),os.environ.get('GITHUB_RUN_ID',''))
        if refusal:approved=False;reason=refusal
    expiry=os.environ.get('FIXED_EXPIRY','')or(datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(minutes=180)).isoformat()
    parsed=datetime.datetime.fromisoformat(expiry)
    if parsed.tzinfo is None:raise SystemExit('Fixed expiry must have a UTC offset')
    approval={}
    if approved and mode=='qualify':
        approval=builder_approval.build(source,run_id,parsed.astimezone(datetime.timezone.utc).isoformat(),builder_approval.ledger())
        builder_approval.validate(approval,source,run_id,approval['fixedCampaignExpiryUtc'],builder_approval.ledger())
        path=Path('artifacts/builder-approval-plan.json');path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(approval,indent=2)+'\n')
        (path.parent/builder_guard.KEY_FILE).write_text(json.dumps(qualification,indent=2)+'\n')
    outputs={'approval-plan':json.dumps(approval,separators=(',',':')),'run':str(approved).lower(),'mode':mode,'source':source,'verification-run-id':run_id,'fixed-expiry':parsed.astimezone(datetime.timezone.utc).isoformat()}
    with Path(os.environ['GITHUB_OUTPUT']).open('a')as output:
        for key,value in outputs.items():output.write(key+'='+value+'\n')
    with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a')as summary:
        if approval:summary.write(builder_approval.summary(approval))
        summary.write(f'Builder selection: {reason}. Source `{source}`. Mode `{mode}`. Fixed expiry `{outputs["fixed-expiry"]}`.\n')

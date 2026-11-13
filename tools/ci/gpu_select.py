#!/usr/bin/env python3
"""Select the workstation Linux GPU lane for one exact, successful current-main Verification run.

Automatic runs (after Verification) qualify only versions whose release policy requires real
GPUs (major/minor). A manual `qualify` dispatch may qualify any complete current-main run. The
workstation is owned hardware, so there is no spend ledger, paid hold or cleanup audit; runners
are single-use. Windows is selected separately in the same run by builder_trigger (Sancus AWS).
"""
import datetime
import json
import os
from pathlib import Path
import subprocess

import builder_trigger
import release_gpu_policy
import release_identity

REPOSITORY = builder_trigger.REPOSITORY
CAMPAIGN_HOURS = 6


def select(event, run, source, checkout, artifacts, policy=None):
    """Return (approved, reason). `policy` is only consulted for automatic runs."""
    if checkout != source:
        return False, 'Superseded checkout; only current main can qualify'
    if not builder_trigger.eligible(run, source):
        return False, 'Superseded or untrusted Verification'
    if not builder_trigger.complete_artifacts(artifacts):
        return False, 'Incomplete native artifact set; docs-only or skipped lanes cannot supply GPU inputs'
    if event == 'workflow_run':
        if policy is None or not policy.get('requiresGpu'):
            return False, 'Patch uses free functional/package gates; dispatch manually to qualify anyway'
        return True, 'Major/minor qualification on the workstation'
    if event == 'workflow_dispatch':
        return True, 'Explicit workstation qualification'
    return False, 'Unsupported trigger'


def main():
    def gh(*args):
        return subprocess.check_output(['gh', *args], text=True, timeout=120)
    event = os.environ.get('GITHUB_EVENT_NAME', '')
    run_id = os.environ.get('VERIFICATION_RUN', '')
    if not run_id.isdigit():
        raise SystemExit('An exact successful Verification run is required')
    source = json.loads(gh('api', f'repos/{REPOSITORY}/git/ref/heads/main'))['object']['sha']
    checkout = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip()
    run = json.loads(gh('api', f'repos/{REPOSITORY}/actions/runs/{run_id}'))
    pages = json.loads(gh('api', f'repos/{REPOSITORY}/actions/runs/{run_id}/artifacts?per_page=100', '--paginate', '--slurp'))
    artifacts = [item for page in pages for item in page['artifacts']]
    policy = None
    if event == 'workflow_run':
        try:
            policy = release_gpu_policy.derive(REPOSITORY, release_identity.check_exports()['version'], gh)
        except ValueError as error:
            policy = {'requiresGpu': False, 'error': str(error)}
    approved, reason = select(event, run, source, checkout, artifacts, policy)
    expiry = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(hours=CAMPAIGN_HOURS)).replace(microsecond=0)
    outputs = {'run': str(approved).lower(), 'source': source, 'verification-run-id': run_id,
               'expiry': expiry.isoformat().replace('+00:00', 'Z')}
    with Path(os.environ['GITHUB_OUTPUT']).open('a') as output:
        for key, value in outputs.items():
            output.write(key + '=' + value + '\n')
    with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a') as summary:
        summary.write(f'Workstation Linux GPU selection: {reason}. Source `{source}`, Verification `{run_id}`, '
                      f'fixed campaign expiry `{outputs["expiry"]}`.\n')
    print(reason)


if __name__ == '__main__':
    main()

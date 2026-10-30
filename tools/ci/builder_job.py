"""Record the actual hosted job start, not an optimistic phase-start timestamp."""
import argparse
import datetime
import json
import os
from pathlib import Path
import subprocess

JOB_TIMEOUT_SECONDS=210*60
CAMPAIGN_SECONDS=160*60


def select(jobs,runner):
    active=[job for job in jobs if job['status']=='in_progress']
    matching=[job for job in active if job.get('runner_name')==runner]
    selected=matching or active
    if len(selected)!=1:raise ValueError('Cannot uniquely identify this hosted job start')
    return selected[0]


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--timeout-seconds',type=int,default=JOB_TIMEOUT_SECONDS);args=parser.parse_args()
    if args.timeout_seconds not in (180,JOB_TIMEOUT_SECONDS):raise SystemExit('Unknown reviewed hosted timeout')
    run=os.environ['GITHUB_RUN_ID']
    if not run.isdigit():raise SystemExit('Actual workflow run required')
    data=json.loads(subprocess.check_output(['gh','api',f'repos/MCMike0399/bomberman-64-multiplayer/actions/runs/{run}/jobs?filter=latest&per_page=100'],text=True,timeout=90))
    job=select(data['jobs'],os.environ.get('RUNNER_NAME'))
    started=datetime.datetime.fromisoformat(job['started_at']);expiry=started+datetime.timedelta(seconds=CAMPAIGN_SECONDS)
    result={'jobStartedAtUtc':started.isoformat(),'jobTimeoutSeconds':args.timeout_seconds,'jobMarginSeconds':900,
            'cleanupBudgetSeconds':1200,'fixedCampaignExpiryUtc':expiry.isoformat(),'jobId':str(job['id']),
            'controllerRunId':run,'controllerRunAttempt':os.environ['GITHUB_RUN_ATTEMPT'],'controllerJobId':os.environ['GITHUB_JOB']}
    args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(result,indent=2)+'\n')

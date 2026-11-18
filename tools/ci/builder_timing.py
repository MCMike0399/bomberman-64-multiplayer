"""Fixed campaign/job envelope and per-phase credential headroom."""
import datetime
import json
import os
import time

CLEANUP_SECONDS=1200
MARGIN_SECONDS=900
CREDENTIAL_MARGIN_SECONDS=120
# Owned guest bootstrap window, measured from the end of launch inside the core
# grant (raised from 600 after campaign 2 attempt 2; Windows reboots once). Raised to
# 1,200 s on 2026-11-17: both parallel lanes must now be ready inside it.
BOOTSTRAP_SECONDS=1200
# Controller stops collecting this long before the fixed expiry (cleanup headroom).
CONTROLLER_STOP_SECONDS=20*60
# Two parallel Windows lanes (user decision 2026-11-17). The remaining lane's replay+suite
# step (builder_guest.REMAINING_STEP_SECONDS=5,220 s) is admitted only with its handoff,
# gate and publish reserve (HANDOFF_PUBLISH_RESERVE_SECONDS=1,500 s) still ahead of the
# 20-minute cleanup boundary, so it must start by E-(1,200+1,500+5,220)=E-7,920 s.
# Nominal intent-to-ready is ~25 min (stack, staging, ~20 min Windows boot) plus ~4 min of
# desktop/engine/installed-launcher, so CreateStack must leave >= 7,920+1,740 = 9,660 s; a
# later launch would allocate two instances that cannot qualify. 9,900 s refuses it first.
# Runs 44012050675/44199687411 created their stack ~4-7 min after selecting a 3-hour expiry
# (~10,400 s of headroom). The fixed expiry itself is never moved.
MINIMUM_LAUNCH_HEADROOM_SECONDS=9900
# Grants are unchanged by the two-lane split: `core` waits for the core lane's terminal
# checkpoint (and the remaining lane's dispatch), `acknowledge` publishes the core handoff,
# `final` waits for the remaining lane. `core`/`final` are also capped at E-20 min.
PHASE_SECONDS={'intent':300,'intent-published':60,'launch':1200,'core':5400,'acknowledge':120,'final':5700,'terminate':300,'retain':600,'cleanup':1200}


def epoch(value):
    if not isinstance(value,str)or not value:raise ValueError('An explicit credential/job timestamp is required')
    if value.startswith('"'):value=json.loads(value)  # configure-aws-credentials Date output.
    parsed=datetime.datetime.fromisoformat(value)
    if parsed.tzinfo is None:raise ValueError('Timestamp must include its UTC offset')
    return parsed.timestamp()


def envelope(plan):
    timing=plan['timing'];started=epoch(timing['jobStartedAtUtc'])
    timeout=timing['jobTimeoutSeconds'];margin=timing.get('jobMarginSeconds',MARGIN_SECONDS)
    cleanup=timing.get('cleanupBudgetSeconds',CLEANUP_SECONDS)
    if timeout<=0 or margin<MARGIN_SECONDS or cleanup!=CLEANUP_SECONDS:raise ValueError('Job/cleanup timing policy differs')
    deadline=started+timeout
    if epoch(plan['fixedCampaignExpiryUtc'])+cleanup>deadline-margin:
        raise ValueError('Fixed expiry plus cleanup does not fit before job timeout and margin')
    return {'jobStartedEpoch':started,'jobDeadlineEpoch':deadline,'jobMarginSeconds':margin,'cleanupBudgetSeconds':cleanup}


def check_credentials(seconds,expiration=None,now=None):
    now=time.time()if now is None else now
    expiry=epoch(expiration if expiration is not None else os.environ.get('BUILDER_CREDENTIAL_EXPIRATION',''))
    if expiry-now<seconds+CREDENTIAL_MARGIN_SECONDS:raise ValueError('Remaining credential lifetime is below the requested phase budget')
    return expiry


def grant(plan,phase,operator=False,now=None,expiration=None):
    now=time.time()if now is None else now
    if phase not in PHASE_SECONDS:raise ValueError('Unknown controller phase')
    if operator:
        return {'phase':phase,'budgetSeconds':PHASE_SECONDS[phase],'mode':'operator',
                'credentialScope':'Operator authorization is checked against AWS; hosted OIDC expiration proof is not claimed.'}
    envelope_error=None
    try:timing=envelope(plan)
    except (ValueError,KeyError)as error:
        if phase not in ('terminate','retain','cleanup'):raise
        envelope_error=str(error);timing={'jobDeadlineEpoch':None}
    expiry=epoch(expiration if expiration is not None else os.environ.get('BUILDER_CREDENTIAL_EXPIRATION',''))
    available_credentials=expiry-now-CREDENTIAL_MARGIN_SECONDS
    if phase in ('terminate','retain','cleanup'):
        # Cleanup still attempts the available bounded work after a cancellation
        # or late failure. Pre-allocation envelope checks remain strict.
        budget=min(PHASE_SECONDS[phase],available_credentials)
    else:
        budget=min(PHASE_SECONDS[phase],timing['jobDeadlineEpoch']-timing['jobMarginSeconds']-now)
        if phase in ('core','final'):budget=min(budget,epoch(plan['fixedCampaignExpiryUtc'])-CLEANUP_SECONDS-now)
        if available_credentials<budget:raise ValueError('Credential lifetime is shorter than the phase budget; re-assume before starting '+phase)
    if budget<1:raise ValueError('No valid credential/time budget remains for '+phase)
    return {'phase':phase,'budgetSeconds':int(budget),'mode':'hosted','credentialExpiresEpoch':expiry,
            'credentialRemainingSeconds':int(expiry-now),'jobDeadlineEpoch':timing['jobDeadlineEpoch'],
            'jobDeadlineAlreadyPassed':timing['jobDeadlineEpoch']is not None and now>=timing['jobDeadlineEpoch'],
            'planEnvelopeError':envelope_error,'cleanupCredentialShortfall':phase=='cleanup'and budget<CLEANUP_SECONDS}


if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser();parser.add_argument('--check-seconds',type=int);args=parser.parse_args()
    if args.check_seconds is not None:
        check_credentials(args.check_seconds);raise SystemExit(0)
    # Only the non-secret STS expiration is persisted between hosted steps.
    value=epoch(os.environ['BUILDER_NEW_EXPIRATION'])
    normalized=datetime.datetime.fromtimestamp(value,datetime.timezone.utc).isoformat()
    with open(os.environ['GITHUB_ENV'],'a')as stream:stream.write('BUILDER_CREDENTIAL_EXPIRATION='+normalized+'\n')

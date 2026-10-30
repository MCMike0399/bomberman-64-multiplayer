"""Resume/cleanup orchestration with independent error boundaries per stage."""
import argparse
import json
from pathlib import Path
import threading
import time

import builder_campaign as campaign
import builder_cleanup as cleanup
import builder_journal as journal
import builder_s3


def run_cleanup_stages(stages):
    errors=[];completed=[]
    for name,operation in stages:
        try:operation();completed.append(name)
        except (Exception,KeyboardInterrupt) as error:errors.append({'stage':name,'error':str(error)})
    return {'completedStages':completed,'stageErrors':errors}


def wait_terminated(plan,identities,deadline):
    wait=threading.Event()
    for identity in identities:
        while time.time()<deadline:
            result=campaign.aws(plan,'ec2','describe-instances','--instance-ids',identity,
                                missing=('InvalidInstanceID.NotFound',))
            if result is None:break
            values=[item for row in result.get('Reservations',[])for item in row.get('Instances',[])]
            if len(values)!=1:raise ValueError('Exact termination inventory is ambiguous')
            if values[0]['State']['Name']=='terminated':break
            wait.wait(min(5,max(0,deadline-time.time())))
        else:raise TimeoutError('Owned instance has not terminated: '+identity)


def cleanup_owned(plan,deadline,retain_evidence):
    """Evidence errors never prevent independently ownership-authorized cleanup.

    Every deletion still passes the strict exact-stack authorization. A failed
    authorization is a concrete leftover, never a reason to widen the scope.
    """
    errors=[];completed=[]
    def stage(name,operation):
        result=run_cleanup_stages([(name,operation)])
        errors.extend(result['stageErrors']);completed.extend(result['completedStages'])
    stage('reconcile-exact-intent',lambda:campaign.reconcile(plan))
    entries=[row for row in campaign.rows(plan)if row['event']=='CREATED'and row['type']=='AWS::CloudFormation::Stack']
    if not entries:
        unresolved=any(row['event']=='INTENT'and row['type']=='AWS::CloudFormation::Stack'for row in campaign.rows(plan))
        result={'cleanupComplete':False,'state':'UNRESOLVED_CREATE_INTENT'if unresolved else'NO_CREATE_INTENT',
                'stageErrors':errors,'completedStages':completed}
        # Not observed is deliberately not converted to a clean-resource claim.
        (campaign.ROOT/'cleanup-controller.json').write_text(json.dumps(result,indent=2)+'\n')
        return result
    if len(entries)!=1:raise ValueError('Ambiguous created stacks; no mutation authorized')
    sid=entries[0]['id']
    stage('synchronize-exact-children',lambda:campaign.sync(plan,best_effort=True))
    stage('retain-evidence',retain_evidence)

    def instances():
        return [row['id']for row in journal.effective_created(campaign.rows(plan)).values()
                if row['type']=='AWS::EC2::Instance']

    def stop_writers():
        # Authorize immediately before mutation, never from stale prefix matches.
        authorized=campaign.authorize(plan)
        ids=instances()
        if authorized is None:
            wait_terminated(plan,ids,deadline)
            return
        if ids:campaign.aws(plan,'ec2','terminate-instances','--instance-ids',*ids)
        wait_terminated(plan,ids,deadline)

    stage('terminate-owned-writers',stop_writers)

    def purge():
        client=builder_s3.Objects(lambda *args:campaign.aws(plan,*args),plan['runId'])
        if builder_s3.verify_absent(client.aws,plan['runId']):return
        def authorize_bucket():
            if campaign.authorize(plan)is None:raise ValueError('Bucket remains but exact stack ownership is unavailable')
        builder_s3.purge_owned(client,authorize_bucket,
                              lambda:wait_terminated(plan,instances(),deadline),
                              time.monotonic()+max(0,deadline-time.time()))

    stage('empty-delete-owned-staging',purge)
    stage('delete-exact-stack',lambda:campaign.delete(plan))
    stage('wait-exact-stack-deletion',lambda:campaign.wait_stack(plan,sid,'delete',deadline))
    receipt=None
    def verify():
        nonlocal receipt
        receipt=cleanup.final(plan,stage_errors=errors)
    stage('verify-every-owned-id-and-sweep',verify)
    result={'cleanupComplete':bool(receipt and receipt['allCreatedResourcesGone']),
            'state':'VERIFIED_GONE'if receipt and receipt['allCreatedResourcesGone']else'INCOMPLETE',
            'stageErrors':errors,'completedStages':completed,
            'evidenceComplete':bool(receipt and receipt['evidenceComplete'])}
    (campaign.ROOT/'cleanup-controller.json').write_text(json.dumps(result,indent=2)+'\n')
    return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--state',type=Path,required=True)
    parser.add_argument('--cleanup-seconds',type=int,default=1200)
    args=parser.parse_args()
    if not 1<=args.cleanup_seconds<=1800:raise SystemExit('Cleanup observation bound must be 1–1800 seconds')
    campaign.configure(args.state);cleanup.configure(args.state)
    plan=json.loads(campaign.PLAN.read_text());campaign.actor(plan)
    # Cleanup-only never provisions, registers runners or retries tests.
    result=cleanup_owned(plan,time.time()+args.cleanup_seconds,lambda:cleanup.evidence(plan))
    print(json.dumps(result,indent=2))
    if not result['cleanupComplete']:raise SystemExit(1)

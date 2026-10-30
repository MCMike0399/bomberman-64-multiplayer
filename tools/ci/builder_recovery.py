"""Map discovered leftovers to retained exact journals before any cleanup."""
import json
from pathlib import Path

import builder_journal as journal
import builder_namespace as names


def aliases(row,plan):
    identity=row['id'];result={identity}
    if row['details'].get('arn'):result.add(row['details']['arn'])
    kind=row['type'];base=f'arn:aws:ec2:{names.REGION}:{names.ACCOUNT}:'
    for resource_type,arn_kind in {'AWS::EC2::Instance':'instance','implicit:EBS':'volume',
            'implicit:ENI':'network-interface','AWS::EC2::SecurityGroup':'security-group'}.items():
        if kind==resource_type:result.add(base+arn_kind+'/'+identity)
    if kind=='AWS::S3::Bucket':result.add('arn:aws:s3:::'+identity)
    if kind=='AWS::Scheduler::ScheduleGroup':
        result.add(f'arn:aws:scheduler:{names.REGION}:{names.ACCOUNT}:schedule-group/{identity}')
    if kind=='AWS::Scheduler::Schedule':
        group=names.resource_name(plan['runId'],'expiry-group')
        name=identity.split('/')[-1];result.add(name)
        result.add(f'arn:aws:scheduler:{names.REGION}:{names.ACCOUNT}:schedule/{group}/{name}')
    return result


def match_states(leftovers,states):
    mapping={}
    for state in states:
        state=Path(state).resolve();plan=json.loads((state/'plan.json').read_text())
        if (plan['account'],plan['region'])!=(names.ACCOUNT,names.REGION):raise ValueError('Prior state belongs to another account/region')
        for row in journal.effective_created(journal.read_journal(state/'creation-manifest.jsonl',plan)).values():
            for identity in aliases(row,plan):mapping.setdefault(identity,set()).add(state)
    matched=set();unknown=[]
    for row in leftovers:
        candidates=mapping.get(row['id'],set())
        if len(candidates)!=1:unknown.append(row)
        else:matched.update(candidates)
    if unknown:raise ValueError('Unproven or ambiguous leftovers: no prefix/tag-only deletion authorized: '+json.dumps(unknown))
    return sorted(matched)


def recover(leftovers,states,cleanup_one):
    """Caller supplies downloaded trusted-main state artifacts and bounded cleanup.

    Journal matching only selects a candidate. cleanup_one must independently
    revalidate live stack membership, tags, template, account and immutable IDs.
    A final fresh discovery must still pass before any new launch.
    """
    matched=match_states(leftovers,states);results=[]
    for state in matched:
        try:results.append({'state':str(state),'receipt':cleanup_one(state)})
        except (Exception,KeyboardInterrupt)as error:results.append({'state':str(state),'error':str(error)})
    return {'attemptedStates':results,'newLaunchAuthorized':False,
            'next':'Repeat noncreating discovery; only a clean fresh audit permits preparation.'}

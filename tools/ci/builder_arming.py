"""Prepared bytes and exact live expiry wiring; never a hardware qualification."""
import hashlib
import json
import re
from pathlib import Path

import builder_namespace as names
import builder_journal as journal

ROOT=None


def configure(root):
    global ROOT
    ROOT=Path(root).resolve()


def verify_prepared(plan):
    if ROOT is None:raise ValueError('Explicit campaign state required')
    if not re.fullmatch(r'sancus-builder-20[0-9]{6}-[0-9a-f]{7}-[0-9a-f]{16}',plan.get('runId','')):
        raise ValueError('New campaigns require the full 64-bit random nonce namespace')
    receipt=json.loads((ROOT/'prepared.json').read_text())
    for key in ('account','region','runId','payloadSource','fixedCampaignExpiryUtc'):
        if receipt.get(key)!=plan.get(key):raise ValueError('Preparation belongs to another campaign or expiry')
    if receipt.get('preparationComplete')is not True or not receipt.get('files'):raise ValueError('Preparation incomplete')
    if receipt.get('templateSha256')!=journal.canonical_hash((ROOT/'template.json').read_text()):
        raise ValueError('Prepared template changed')
    if receipt.get('planSha256')!=journal.canonical_hash(plan):raise ValueError('Prepared campaign parameters changed')
    for item in receipt['files']:
        value=hashlib.sha256(Path(item['path']).read_bytes()).hexdigest()
        if value!=item['sha256']:raise ValueError('Prepared input changed')
    return receipt


def verify_expiry(plan,stack_entry,logical,role,policy,inline_names,schedules):
    ids=[logical[name+'Instance']['id']for name in ('Linux','Windows')]
    if len(set(ids))!=2:raise ValueError('Two distinct exact instances required')
    run=plan['runId'];group=names.resource_name(run,'expiry-group')
    group_arn=f'arn:aws:scheduler:{names.REGION}:{names.ACCOUNT}:schedule-group/{group}'
    boundary=f'arn:aws:iam::{names.ACCOUNT}:policy/{names.BOUNDARY}'
    expected_trust={'Version':'2012-10-17','Statement':[{
        'Effect':'Allow','Principal':{'Service':'scheduler.amazonaws.com'},'Action':'sts:AssumeRole',
        'Condition':{'StringEquals':{'aws:SourceAccount':names.ACCOUNT},'ArnEquals':{'aws:SourceArn':group_arn}}}]}
    if (role['RoleName']!=logical['ExpiryRole']['id']or role['AssumeRolePolicyDocument']!=expected_trust or
            role.get('PermissionsBoundary',{}).get('PermissionsBoundaryArn')!=boundary):
        raise ValueError('Expiry trust or boundary differs')
    if inline_names!=[names.resource_name(run,'termination')]:raise ValueError('Unexpected expiry inline policies')
    expected={'Version':'2012-10-17','Statement':[{'Effect':'Allow','Action':'ec2:TerminateInstances',
        'Resource':[f'arn:aws:ec2:{names.REGION}:{names.ACCOUNT}:instance/{identity}'for identity in ids],
        'Condition':{'StringEquals':{'ec2:ResourceTag/'+names.RUN_TAG:run}}}]}
    if journal.canonical_hash(policy)!=journal.canonical_hash(expected):raise ValueError('Expiry permissions differ')
    if set(schedules)!={'Linux','Windows'}:raise ValueError('Both actual expiry schedules required')
    for osname,identity in zip(('Linux','Windows'),ids):
        schedule=schedules[osname];target=schedule['Target']
        if (schedule['Name']!=names.resource_name(run,osname.lower()+'-expiry')or schedule['GroupName']!=group or
                schedule['State']!='ENABLED'or schedule['ActionAfterCompletion']!='DELETE'or
                schedule['ScheduleExpression']!='at('+stack_entry['details']['parameters']['ExpiresAt']+')'or
                schedule['ScheduleExpressionTimezone']!='UTC'or schedule['FlexibleTimeWindow']!={'Mode':'OFF'}or
                target['Arn']!='arn:aws:scheduler:::aws-sdk:ec2:terminateInstances'or target['RoleArn']!=role['Arn']or
                json.loads(target['Input'])!={'InstanceIds':[identity]}or
                target['RetryPolicy']!={'MaximumRetryAttempts':3,'MaximumEventAgeInSeconds':60}):
            raise ValueError('Actual schedule target or fixed deadline differs')
    return ids


def verify_live(plan):
    import builder_campaign as campaign
    campaign.actor(plan);campaign.sync(plan);campaign.authorize(plan)
    entry=campaign.stack_entry(plan);sid=entry['id']
    stack=campaign.aws(plan,'cloudformation','describe-stacks','--stack-name',sid)['Stacks'][0]
    if stack['StackStatus']!='CREATE_COMPLETE':raise ValueError('Campaign stack not complete')
    owned=journal.effective_created(campaign.rows(plan))
    logical={row['details']['logicalId']:row for row in owned.values()if row['details'].get('logicalId')}
    role=campaign.aws(plan,'iam','get-role','--role-name',logical['ExpiryRole']['id'])['Role']
    inline=campaign.aws(plan,'iam','list-role-policies','--role-name',role['RoleName'])['PolicyNames']
    policy=campaign.aws(plan,'iam','get-role-policy','--role-name',role['RoleName'],
                        '--policy-name',names.resource_name(plan['runId'],'termination'))['PolicyDocument']
    schedules={osname:campaign.aws(plan,'scheduler','get-schedule','--group-name',names.resource_name(plan['runId'],'expiry-group'),
                                 '--name',logical[osname+'Expiry']['id'].split('/')[-1])for osname in ('Linux','Windows')}
    ids=verify_expiry(plan,entry,logical,role,policy,inline,schedules)
    receipt={**{key:plan[key]for key in ('account','region','runId','payloadSource','fixedCampaignExpiryUtc')},
             'stackArn':sid,'runtimeCleanupArmed':True,'hardwareQualified':False,'instanceIds':ids,
             'verification':'Exact live schedule/trust/inline-policy/boundary wiring; no permission-simulation or device claim'}
    (ROOT/'ready.json').write_text(json.dumps(receipt,indent=2)+'\n')
    return receipt


def set_schedule_auto_delete(plan):
    """Requires the separately reviewed, scoped UpdateSchedule permission.

    AWS::Scheduler::Schedule currently rejects ActionAfterCompletion in CFN.
    Preserve all actual schedule fields; only add self-deletion after verifying
    the exact owned stack and the expected schedule names/group. A later arming
    read validates every target/deadline and refuses readiness on any mismatch.
    """
    import builder_campaign as campaign
    campaign.authorize(plan)
    for osname in ('linux','windows'):
        name=names.resource_name(plan['runId'],osname+'-expiry')
        group=names.resource_name(plan['runId'],'expiry-group')
        schedule=campaign.aws(plan,'scheduler','get-schedule','--name',name,'--group-name',group)
        if schedule['Name']!=name or schedule['GroupName']!=group:raise ValueError('Unexpected schedule identity')
        fields=('Name','GroupName','ScheduleExpression','ScheduleExpressionTimezone','FlexibleTimeWindow','Target','State')
        request={key:schedule[key]for key in fields}
        request['ActionAfterCompletion']='DELETE'
        # Avoid shell interpolation; the AWS wrapper passes a structured argv.
        campaign.aws(plan,'scheduler','update-schedule','--cli-input-json',json.dumps(request))

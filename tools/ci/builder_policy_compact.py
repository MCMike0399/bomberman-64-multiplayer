"""Compact repeated IAM clauses without adding API-valid action/resource pairs.

Resource kinds follow the AWS Service Authorization Reference for EC2, IAM,
CloudFormation, SSM, Scheduler and S3. Unknown scoped actions fail closed.
https://docs.aws.amazon.com/service-authorization/latest/reference/reference_policies_actions-resources-contextkeys.html
"""
import json

import builder_staging

KINDS={}


def register(actions,kinds):
    for action in actions.split():KINDS[action]=set(kinds.split())


register('ec2:RunInstances','instance image subnet security-group network-interface volume')
register('ec2:CreateSecurityGroup','security-group vpc')
register('ec2:CreateTags','instance volume network-interface security-group')
register('ec2:TerminateInstances ec2:GetConsoleOutput','instance')
register('ec2:DeleteSecurityGroup','security-group')
register('ec2:DeleteVolume','volume')
register('ec2:DeleteNetworkInterface','network-interface')
register('iam:CreateRole iam:TagRole iam:GetRole iam:GetRole* iam:ListRoleTags iam:ListRolePolicies iam:GetRolePolicy '
         'iam:PutRolePolicy iam:DeleteRolePolicy iam:DeleteRole iam:PassRole iam:ListAttachedRolePolicies','role')
register('iam:GetPolicy','policy')
register('iam:CreateInstanceProfile iam:GetInstanceProfile iam:AddRoleToInstanceProfile '
         'iam:RemoveRoleFromInstanceProfile iam:DeleteInstanceProfile iam:TagInstanceProfile '
         'iam:ListInstanceProfileTags','instance-profile')
register('cloudformation:CreateStack cloudformation:Describe* cloudformation:DescribeStacks cloudformation:DescribeStackResources '
         'cloudformation:ListStackResources cloudformation:DescribeStackEvents cloudformation:GetTemplate '
         'cloudformation:DeleteStack','stack')
register('scheduler:CreateScheduleGroup scheduler:GetScheduleGroup scheduler:DeleteScheduleGroup '
         'scheduler:ListTagsForResource scheduler:TagResource','schedule-group')
register('scheduler:CreateSchedule scheduler:GetSchedule scheduler:DeleteSchedule scheduler:UpdateSchedule','schedule')
register('ssm:SendCommand','document instance')
register('ssm:GetDocument ssm:DescribeDocument','document')
register(' '.join(builder_staging.BUCKET_ACTIONS),'bucket')
register(' '.join(builder_staging.OBJECT_ACTIONS),'object')


def sequence(value):return value if isinstance(value,list)else[value]


def valid_pair(action,resource):
    if resource=='*':return True
    parts=resource.split(':',5)
    if len(parts)!=6 or parts[0]!='arn':raise ValueError('Expected an ARN or explicit wildcard')
    service=parts[2];kind=parts[5].split('/',1)[0]
    if service=='s3':kind='object'if'/'in parts[5]else'bucket'
    action_service=action.split(':',1)[0]
    if action_service!=service and not(action=='ssm:SendCommand'and service=='ec2'and kind=='instance'):
        return False
    if action not in KINDS:raise ValueError('Unmapped scoped action in builder boundary: '+action)
    return kind in KINDS[action]


def api_pairs(statement):
    condition=json.dumps(statement.get('Condition',{}),sort_keys=True)
    return {(statement['Effect'],action,resource,condition)
            for action in sequence(statement['Action'])for resource in sequence(statement['Resource'])
            if valid_pair(action,resource)}


def compact(policy):
    rows=[{key:value for key,value in row.items()if key!='Sid'}for row in policy['Statement']]
    before=set().union(*(api_pairs(row)for row in rows))
    changed=True
    while changed:
        changed=False
        for i,left in enumerate(rows):
            for j in range(i+1,len(rows)):
                right=rows[j]
                if left['Effect']!=right['Effect']or left.get('Condition')!=right.get('Condition'):continue
                # S3 wildcard bucket ARNs can also match object paths. Preserve
                # every S3 statement verbatim instead of inferring its scope
                # from a slash in an ARN pattern (or introducing redundancy).
                if any(action.startswith('s3:')for row in (left,right)for action in sequence(row['Action'])):continue
                if ('*'in sequence(left['Resource']))!=('*'in sequence(right['Resource'])):continue
                merged={**left,'Action':list(dict.fromkeys(sequence(left['Action'])+sequence(right['Action']))),
                        'Resource':list(dict.fromkeys(sequence(left['Resource'])+sequence(right['Resource'])))}
                # In particular, never combine tagged SG launch authorization
                # with instance termination, or vendor reads with owned writes.
                if api_pairs(merged)!=api_pairs(left)|api_pairs(right):continue
                rows[i]=merged;del rows[j];changed=True;break
            if changed:break
    for row in rows:
        for key in ('Action','Resource'):
            values=sequence(row[key]);row[key]=values[0]if len(values)==1 else values
    if set().union(*(api_pairs(row)for row in rows))!=before:raise ValueError('Compaction widened API permissions')
    return {'Version':policy['Version'],'Statement':rows}

"""Read-only empty-run audit for the first live OIDC proof. Never creates or deletes."""
import argparse
import json
from pathlib import Path
import subprocess

import builder_namespace as names

STANDING={
    f'arn:aws:iam::{names.ACCOUNT}:role/{names.CI_ROLE}',
    f'arn:aws:iam::{names.ACCOUNT}:policy/{names.BOUNDARY}',
}


def expected_standing(arn):
    return arn in STANDING or arn.startswith(
        f'arn:aws:cloudformation:{names.REGION}:{names.ACCOUNT}:stack/{names.IDENTITY_STACK}/')


def audit(aws,require_scoped_role=True):
    caller=aws('sts','get-caller-identity')
    if caller.get('Account')!=names.ACCOUNT or require_scoped_role and not caller.get('Arn','').startswith(
            f'arn:aws:sts::{names.ACCOUNT}:assumed-role/{names.CI_ROLE}/'):
        raise RuntimeError('Empty audit requires the actual scoped builder assumed role')
    checked={};errors=[]
    def observe(kind,read):
        try:checked[kind]=read()
        except Exception as error:
            checked[kind]=None;errors.append({'type':kind,'error':str(error)})
    def own(rows,key):return [row[key]for row in rows if row.get(key,'').startswith(names.PREFIX)]
    tag_filter=['--filters','Name=tag-key,Values='+names.RUN_TAG]
    observe('CFN',lambda:[row['StackId']for row in aws('cloudformation','list-stacks')['StackSummaries']
                         if row['StackName'].startswith(names.PREFIX)and row['StackName']!=names.IDENTITY_STACK
                         and row['StackStatus']!='DELETE_COMPLETE'])
    observe('EC2',lambda:[row['InstanceId']for reservation in aws('ec2','describe-instances',*tag_filter)['Reservations']
                         for row in reservation['Instances']if row['State']['Name']!='terminated'])
    for kind,command,key,id_key in (
            ('EBS','describe-volumes','Volumes','VolumeId'),('ENI','describe-network-interfaces','NetworkInterfaces','NetworkInterfaceId'),
            ('SG','describe-security-groups','SecurityGroups','GroupId'),('EIP','describe-addresses','Addresses','AllocationId'),
            ('KeyPairs','describe-key-pairs','KeyPairs','KeyPairId')):
        observe(kind,lambda command=command,key=key,id_key=id_key:[row[id_key]for row in aws('ec2',command,*tag_filter)[key]])
    observe('Snapshots',lambda:[row['SnapshotId']for row in aws('ec2','describe-snapshots','--owner-ids',names.ACCOUNT,*tag_filter)['Snapshots']])
    observe('AMI',lambda:[row['ImageId']for row in aws('ec2','describe-images','--owners',names.ACCOUNT,*tag_filter)['Images']])
    observe('IAMRoles',lambda:[row['RoleName']for row in aws('iam','list-roles')['Roles']
                              if row['RoleName'].startswith(names.PREFIX)and row['RoleName']!=names.CI_ROLE])
    observe('IAMProfiles',lambda:own(aws('iam','list-instance-profiles')['InstanceProfiles'],'InstanceProfileName'))
    observe('SchedulerSchedules',lambda:own(aws('scheduler','list-schedules','--name-prefix',names.PREFIX).get('Schedules',[]),'Name'))
    observe('SchedulerGroups',lambda:own(aws('scheduler','list-schedule-groups','--name-prefix',names.PREFIX).get('ScheduleGroups',[]),'Name'))
    observe('Logs',lambda:own(aws('logs','describe-log-groups','--log-group-name-prefix',names.PREFIX).get('logGroups',[]),'logGroupName'))
    observe('S3',lambda:own(aws('s3api','list-buckets').get('Buckets',[]),'Name'))
    observe('SSMParameters',lambda:own(aws('ssm','describe-parameters','--parameter-filters',
            'Key=Name,Option=BeginsWith,Values='+names.PREFIX).get('Parameters',[]),'Name'))
    observe('SSMDocuments',lambda:own(aws('ssm','list-documents','--filters','Key=Owner,Values=Self').get('DocumentIdentifiers',[]),'Name'))
    observe('TaggedResources',lambda:[row['ResourceARN']for row in aws('resourcegroupstaggingapi','get-resources',
            '--tag-filters','Key='+names.RUN_TAG).get('ResourceTagMappingList',[])if not expected_standing(row['ResourceARN'])])
    leftovers=[{'type':kind,'id':item}for kind,rows in checked.items()if rows for item in rows]
    return {'schemaVersion':1,'account':names.ACCOUNT,'region':names.REGION,'caller':caller['Arn'],
            'readOnly':True,'resourcesCreated':[],'resourcesDeleted':[],
            'checked':{kind:{'state':'unverified'if rows is None else'present'if rows else'absent','ids':rows}for kind,rows in checked.items()},
            'expectedStandingIdentity':{'stack':names.IDENTITY_STACK,'role':names.CI_ROLE,'boundary':names.BOUNDARY,'provider':'referenced existing provider'},
            'leftovers':leftovers,'errors':errors,'passed':not leftovers and not errors,
            'nonResourceRecords':['CloudTrail events','SSM command history','billing data','CloudFormation DELETE_COMPLETE stack history'],
            'scope':('First live OIDC assume-role and empty discovery proof only; not a paid campaign cleanup receipt, protected-inventory comparison or game qualification.'
                     if require_scoped_role else 'Read-only operator namespace discovery; not an OIDC assume-role proof or game qualification.')}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    def aws(*arguments):
        result=subprocess.run(['aws','--profile','sancus','--region',names.REGION,*arguments,'--output','json'],
                              capture_output=True,text=True,timeout=90)
        if result.returncode:raise RuntimeError('Read-only '+arguments[0]+'/'+arguments[1]+' failed: '+result.stderr.strip())
        return json.loads(result.stdout)
    report=audit(aws);args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,indent=2)+'\n')
    if not report['passed']:raise SystemExit('Builder empty audit failed; retained exact findings, no mutation attempted.')
    print('Builder OIDC empty audit passed; no campaign resources created.')

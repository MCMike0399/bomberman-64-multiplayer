"""Read-only empty-run audit for the first live OIDC proof. Never creates or deletes."""
import argparse
import json
from pathlib import Path
import re
import subprocess

import builder_namespace as names
import builder_retained_journals as retained

STANDING={
    f'arn:aws:iam::{names.ACCOUNT}:role/{names.CI_ROLE}',
    f'arn:aws:iam::{names.ACCOUNT}:policy/{names.BOUNDARY}',
}


def expected_standing(arn):
    return arn in STANDING or arn.startswith(
        f'arn:aws:cloudformation:{names.REGION}:{names.ACCOUNT}:stack/{names.IDENTITY_STACK}/')


def error_code(error):
    code=getattr(error,'code',None)
    if code:return str(code)
    match=re.search(r'An error occurred \(([^)]+)\)',str(error))
    return match[1]if match else None


def aged_out(instance,journals):
    """Empty exact-ID Reservations resolve only with a builder run's retained journal
    proving it CREATED this instance and OBSERVED it terminated. Otherwise AMBIGUOUS."""
    row={'kind':'EC2Instance','authoritative':'AMBIGUOUS:','describeShape':retained.SHAPE,'resolved':False}
    try:found=(journals or retained.default()).instance(instance)
    except Exception as error:found={'evidence':None,'errors':[{'error':str(error)[:300]}]}
    row['journalLookup']={'matchingJournals':found.get('matchingJournals',0),'errors':found.get('errors',[])}
    if found.get('evidence'):row.update(authoritative='AGED_OUT',resolved=True,retainedJournal=found['evidence'])
    return row


def authoritative(aws,arn,journals=None):
    """Read-only describe of one tagging-index hit. The index lags deletion, so
    only an authoritative terminated/NotFound/DELETE_COMPLETE answer resolves it,
    or AGED_OUT: empty exact-ID Reservations plus retained-journal proof.
    Shutting-down, existing, unknown types and every other error stay leftovers."""
    match=re.fullmatch(r'arn:aws:([a-z0-9-]+):([a-z0-9-]*):([0-9]*):(.+)',arn)
    if not match:return {'kind':'unparsed','authoritative':'unknown resource ARN','resolved':False}
    service,region,account,resource=match.groups()
    if account not in ('',names.ACCOUNT)or region not in ('',names.REGION):
        return {'kind':service,'authoritative':'outside the pinned account/region','resolved':False}
    def read(kind,arguments,gone,present):
        try:data=aws(*arguments)
        except Exception as error:
            code=error_code(error)
            if code in gone or (kind=='CloudFormation'and code=='ValidationError'and'does not exist'in str(error)):
                return {'kind':kind,'authoritative':code,'resolved':True}
            return {'kind':kind,'authoritative':'describe-error','errorCode':code,'error':str(error)[:300],'resolved':False}
        state,resolved=present(data)
        return {'kind':kind,'authoritative':state,'resolved':resolved}
    exists=lambda data:('PRESENT',False)
    def instance(data):
        if isinstance(data,dict)and data.get('Reservations')==[]:return retained.SHAPE,False
        states=[row['State']['Name']for reservation in data.get('Reservations',[])for row in reservation.get('Instances',[])]
        if len(states)!=1:return 'AMBIGUOUS:'+','.join(states),False
        return states[0],states[0]=='terminated'
    def stack(data):
        states=[row.get('StackStatus')for row in data.get('Stacks',[])]
        if len(states)!=1:return 'AMBIGUOUS',False
        return states[0],states[0]=='DELETE_COMPLETE'
    kind,_,name=resource.partition('/')
    if service=='ec2'and kind=='instance':
        row=read('EC2Instance',('ec2','describe-instances','--instance-ids',name),('InvalidInstanceID.NotFound',),instance)
        return aged_out(name,journals)if row['authoritative']==retained.SHAPE else row
    if service=='ec2'and kind=='volume':
        return read('EBS',('ec2','describe-volumes','--volume-ids',name),('InvalidVolume.NotFound',),exists)
    if service=='ec2'and kind=='network-interface':
        return read('ENI',('ec2','describe-network-interfaces','--network-interface-ids',name),('InvalidNetworkInterfaceID.NotFound',),exists)
    if service=='ec2'and kind=='security-group':
        return read('SG',('ec2','describe-security-groups','--group-ids',name),('InvalidGroup.NotFound',),exists)
    if service=='s3'and not region and not account and '/'not in resource:
        return read('S3Bucket',('s3api','head-bucket','--bucket',resource,'--expected-bucket-owner',names.ACCOUNT),('NoSuchBucket','404'),exists)
    if service=='iam'and kind=='role':
        return read('IAMRole',('iam','get-role','--role-name',name.rsplit('/',1)[-1]),('NoSuchEntity',),exists)
    if service=='iam'and kind=='instance-profile':
        return read('IAMProfile',('iam','get-instance-profile','--instance-profile-name',name.rsplit('/',1)[-1]),('NoSuchEntity',),exists)
    if service=='scheduler'and kind=='schedule'and name.count('/')==1:
        group,schedule=name.split('/')
        return read('SchedulerSchedule',('scheduler','get-schedule','--group-name',group,'--name',schedule),('ResourceNotFoundException',),exists)
    if service=='scheduler'and kind=='schedule-group'and name:
        return read('SchedulerGroup',('scheduler','get-schedule-group','--name',name),('ResourceNotFoundException',),exists)
    if service=='cloudformation'and kind=='stack':
        return read('CloudFormation',('cloudformation','describe-stacks','--stack-name',arn),(),stack)
    return {'kind':service+':'+kind,'authoritative':'unknown resource type','resolved':False}


def audit(aws,require_scoped_role=True,journals=None):
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
    tagged=[]
    def tagging_index():
        hits=[row['ResourceARN']for row in aws('resourcegroupstaggingapi','get-resources',
              '--tag-filters','Key='+names.RUN_TAG).get('ResourceTagMappingList',[])if not expected_standing(row['ResourceARN'])]
        # Both views per ID: the (lagging) tagging index and the authoritative read.
        tagged.extend({'id':arn,'taggingIndex':'present',**authoritative(aws,arn,journals)}for arn in hits)
        return [row['id']for row in tagged if not row['resolved']]
    observe('TaggedResources',tagging_index)
    leftovers=[{'type':kind,'id':item}for kind,rows in checked.items()if rows for item in rows]
    return {'schemaVersion':1,'account':names.ACCOUNT,'region':names.REGION,'caller':caller['Arn'],
            'readOnly':True,'resourcesCreated':[],'resourcesDeleted':[],
            'checked':{kind:{'state':'unverified'if rows is None else'present'if rows else'absent','ids':rows}for kind,rows in checked.items()},
            'expectedStandingIdentity':{'stack':names.IDENTITY_STACK,'role':names.CI_ROLE,'boundary':names.BOUNDARY,'provider':'referenced existing provider'},
            'taggedResources':tagged,'leftovers':leftovers,'errors':errors,'passed':not leftovers and not errors,
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

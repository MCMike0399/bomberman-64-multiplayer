#!/usr/bin/env python3
"""One exact owned CloudFormation campaign. Preparation is read-only; launch is explicit."""
import argparse,datetime,json,subprocess,uuid,time,threading,os
from pathlib import Path
import builder_journal as safety
import builder_namespace as names
import builder_s3
import builder_staging
import re

ROOT=None;PLAN=None;JOURNAL=None

def configure(state):
    global ROOT,PLAN,JOURNAL
    ROOT=Path(state).resolve()
    if ROOT.is_relative_to(Path(__file__).resolve().parent):raise ValueError('Campaign state must be outside the tool source directory')
    PLAN=ROOT/'plan.json';JOURNAL=ROOT/'creation-manifest.jsonl'


def aws(plan,service,operation,*args,missing=()):
    if (plan.get('profile'),plan.get('region'),plan.get('account'))!=('sancus',names.REGION,names.ACCOUNT):
        raise ValueError('Explicit sancus/us-east-1 account binding required')
    if (plan.get('dryRun')or os.environ.get('BUILDER_DRY_RUN')=='true')and not operation.startswith(('describe-','list-','get-','head-','simulate-')):
        raise ValueError('Dry run refuses every mutating AWS operation')
    command=['aws','--profile',plan['profile'],'--region',plan['region'],'--cli-connect-timeout','8','--cli-read-timeout','25',service,operation,*args,'--output','json']
    result=subprocess.run(command,capture_output=True,text=True,timeout=100)
    if result.returncode:
        if any(code in result.stderr for code in missing):return None
        if service=='s3api':
            match=re.search(r'An error occurred \(([^)]+)\)',result.stderr)
            raise builder_s3.ApiError(match[1]if match else'UnverifiedError',service+'/'+operation+' failed')
        raise RuntimeError(service+'/'+operation+': '+result.stderr.strip())
    return json.loads(result.stdout)if result.stdout.strip()else{}

def actor(plan):
    if not re.fullmatch(names.RUN_PATTERN,plan.get('runId','')):raise ValueError('Invalid campaign namespace')
    identity=aws(plan,'sts','get-caller-identity')
    if identity['Account']!=plan['account']:raise ValueError('Wrong account; no mutation authorized')
    return identity

def rows(plan):
    if JOURNAL is None:raise ValueError('Explicit campaign state directory required')
    return safety.read_journal(JOURNAL,plan)if JOURNAL.exists()else[]

def stack_entry(plan):
    values=[x for x in rows(plan)if x['event']=='CREATED'and x['type']=='AWS::CloudFormation::Stack']
    if len(values)!=1:raise ValueError('Exactly one journaled CREATED stack required')
    return values[0]

def record_created(plan,kind,identity,details,parent=None):
    prior=safety.effective_created(rows(plan)).get((kind,identity))
    if prior is None:
        safety.append(JOURNAL,plan,'CREATED',kind,identity,details,parent)
        return
    if details.get('logicalId') and not prior['details'].get('logicalId'):
        if details.get('cfnStackId')!=prior['details'].get('cfnStackId'):raise ValueError('Cannot enrich resource outside its created stack')
        safety.append(JOURNAL,plan,'STATUS',kind,identity,{'resourceMetadataConfirmation':True,'logicalId':details['logicalId'],'cfnStackId':details['cfnStackId']},parent)
    known=prior['details'].get('uniqueId');new=details.get('uniqueId')
    if known and new and known!=new:raise ValueError('Immutable IAM identity changed')
    if new and not known:
        if not details.get('ownershipVerified') or details.get('cfnStackId')!=prior['details'].get('cfnStackId'):
            raise ValueError('Late identity confirmation is not proven')
        safety.append(JOURNAL,plan,'STATUS',kind,identity,{**details,'identityConfirmation':True},parent)


def reconcile(plan):
    """Recover only the exact durable request; never reissue CreateStack here."""
    actor(plan);existing=[r for r in rows(plan)if r['event']=='CREATED'and r['type']=='AWS::CloudFormation::Stack']
    if existing:return stack_entry(plan)
    intents=[r for r in rows(plan)if r['event']=='INTENT'and r['type']=='AWS::CloudFormation::Stack']
    if not intents:return None
    if len(intents)!=1:raise ValueError('Ambiguous stack creation intents')
    intent=intents[0];details=intent['details']
    response=aws(plan,'cloudformation','describe-stacks','--stack-name',intent['id'],missing=('does not exist',))
    if response is None:
        # OnFailure=DELETE can finish before a lost CreateStack response is
        # recovered. Deleted stacks require their exact ARN, not just the name.
        history=aws(plan,'cloudformation','list-stacks')
        candidates=[row for row in (history or{}).get('StackSummaries',[])if row.get('StackName')==intent['id']]
        if len(candidates)>1:raise ValueError('Ambiguous exact-name stack history; do not adopt or retry')
        if candidates:response=aws(plan,'cloudformation','describe-stacks','--stack-name',candidates[0]['StackId'])
    if response is None:
        safety.append(JOURNAL,plan,'STATUS','creation-reconciliation',intent['id'],{'state':'EXACT_REQUEST_STACK_NOT_OBSERVED','requestToken':details['requestToken']})
        return None
    stack=response['Stacks'][0];arn=stack['StackId'];parts=arn.split(':',5)
    if len(parts)!=6 or parts[3]!=plan['region'] or parts[4]!=plan['account'] or not parts[5].startswith('stack/'+plan['runId']+'/'):
        raise ValueError('Recovered stack identity differs')
    tags={x['Key']:x['Value']for x in stack.get('Tags',[])}
    params={x['ParameterKey']:x['ParameterValue']for x in stack.get('Parameters',[])}
    original=aws(plan,'cloudformation','get-template','--stack-name',arn,'--template-stage','Original')['TemplateBody']
    events=aws(plan,'cloudformation','describe-stack-events','--stack-name',arn)['StackEvents']
    if tags.get(names.RUN_TAG)!=plan['runId'] or tags.get(names.TAG_PREFIX+'source')!=plan['payloadSource'] or params!=details['parameters'] or safety.canonical_hash(original)!=details['templateSha256']:
        raise ValueError('Recovered stack provenance differs')
    if not any(e.get('ResourceType')=='AWS::CloudFormation::Stack'and e.get('ClientRequestToken')==details['requestToken']for e in events):
        raise ValueError('Recovered stack lacks the exact creation request token')
    return safety.append(JOURNAL,plan,'CREATED','AWS::CloudFormation::Stack',arn,{**details,'reconciled':True})


def wait_stack(plan,sid,goal,deadline,observe=None):
    """Observe the same stack until its bounded deadline; never relaunch on timeout."""
    event=threading.Event()
    while time.time()<deadline:
        try:response=aws(plan,'cloudformation','describe-stacks','--stack-name',sid,missing=('does not exist',))
        except (RuntimeError,subprocess.TimeoutExpired)as error:
            if not any(word in str(error).lower()for word in ('timeout','timed out','could not connect','connection','temporarily','throttl')):raise
            safety.append(JOURNAL,plan,'STATUS','stack-observation',sid,{'observationError':str(error),'notTerminal':True})
            event.wait(min(5,max(0,deadline-time.time())))
            continue
        if response is None:
            if goal=='delete':return 'absent'
            raise RuntimeError('Provisioning stack disappeared')
        state=response['Stacks'][0]['StackStatus']
        if observe is not None:observe()
        if state==('CREATE_COMPLETE'if goal=='create'else 'DELETE_COMPLETE'):return state
        if state.endswith('_FAILED')or goal=='create'and state.startswith('ROLLBACK'):
            raise RuntimeError('Stack terminal state: '+state)
        event.wait(min(5,max(0,deadline-time.time())))
    raise TimeoutError('Observation deadline for SAME stack '+sid+'; no new launch authorized')


def protected(plan):
    raw=(ROOT/'protected-instances.json').read_bytes()
    import hashlib
    if hashlib.sha256(raw).hexdigest()!=plan['references']['protectedInstancesSha256']:raise ValueError('Protected baseline changed')
    source=json.loads(raw);ids=set()
    for item in source:
        ids.add(item['id']);ids.update(item.get('interfaces')or[])
        ids.update(v['Ebs']['VolumeId']for v in item.get('volumes',[])if 'Ebs'in v)
        ids.update(v['GroupId']for v in item.get('groups',[]))
    ids.update((plan['references']['vpc'],plan['references']['subnet']))
    ids.update(v['ami']for v in plan['instances'])
    return ids

def metadata(plan,kind,physical):
    bucket=builder_staging.bucket_name(plan['runId'])
    if kind in ('AWS::S3::Bucket','AWS::S3::BucketPolicy'):
        if physical!=bucket:raise ValueError('Unexpected staging bucket identity')
        client=builder_s3.Objects(lambda *args:aws(plan,*args),plan['runId'])
        try:client.call('head-bucket')
        except builder_s3.ApiError as error:
            if error.code in ('404','NoSuchBucket','NotFound'):return {'state':'absent'}
            raise
        tags={row['Key']:row['Value']for row in client.call('get-bucket-tagging').get('TagSet',[])}
        if tags.get(names.RUN_TAG)!=plan['runId']:raise ValueError('Staging bucket run ownership differs')
        location=client.call('get-bucket-location').get('LocationConstraint')or'us-east-1'
        if location!=plan['region']:raise ValueError('Staging bucket region differs')
        if kind=='AWS::S3::BucketPolicy':
            try:document=json.loads(client.call('get-bucket-policy')['Policy'])
            except builder_s3.ApiError as error:
                if error.code=='NoSuchBucketPolicy':return {'state':'absent'}
                raise
            expected=builder_staging.resources(plan['runId'],plan['fixedCampaignExpiryUtc'])['StagingBucketPolicy']['Properties']['PolicyDocument']
            if document!=expected:raise ValueError('Staging policy changed outside the owned template')
        return {'ownershipVerified':True,'region':location}
    if kind=='AWS::IAM::Role':
        data=aws(plan,'iam','get-role','--role-name',physical,missing=('NoSuchEntity',))
        if data:
            role=data['Role'];tags={x['Key']:x['Value']for x in role.get('Tags',[])}
            if tags.get(names.RUN_TAG)!=plan['runId'] or role['Arn'].split(':')[4]!=plan['account']:raise ValueError('IAM role ownership differs')
            return {'uniqueId':role['RoleId'],'arn':role['Arn'],'ownershipVerified':True}
        return {'state':'absent'}
    if kind=='AWS::EC2::SecurityGroup':
        data=aws(plan,'ec2','describe-security-groups','--group-ids',physical,missing=('InvalidGroup.NotFound',))
        if not data:return {'state':'absent'}
        group=data['SecurityGroups'][0];tags={x['Key']:x['Value']for x in group.get('Tags',[])}
        if group['OwnerId']!=plan['account'] or tags.get(names.RUN_TAG)!=plan['runId'] or group['VpcId']!=plan['references']['vpc']:raise ValueError('Security group ownership differs')
        return {'vpc':group['VpcId'],'owner':group['OwnerId']}
    if kind=='AWS::IAM::InstanceProfile':
        data=aws(plan,'iam','get-instance-profile','--instance-profile-name',physical,missing=('NoSuchEntity',))
        if data:
            profile=data['InstanceProfile']
            if profile['InstanceProfileName']!=plan['runId']+'-profile' or profile['Arn'].split(':')[4]!=plan['account']:raise ValueError('Instance profile ownership differs')
            for role in profile.get('Roles',[]):metadata(plan,'AWS::IAM::Role',role['RoleName'])
            return {'uniqueId':profile['InstanceProfileId'],'arn':profile['Arn'],'ownershipVerified':True}
        return {'state':'absent'}
    return {}

def sync(plan,best_effort=False):
    actor(plan);entry=stack_entry(plan);sid=entry['id']
    response=aws(plan,'cloudformation','list-stack-resources','--stack-name',sid,missing=('does not exist',))
    if response is None:return []
    for item in response['StackResourceSummaries']:
        physical=item.get('PhysicalResourceId');kind=item['ResourceType']
        if not physical:continue
        if physical in protected(plan):raise ValueError('CFN physical member overlaps protected baseline')
        base={'logicalId':item['LogicalResourceId'],'cfnStackId':sid}
        try:description=metadata(plan,kind,physical)
        except (RuntimeError,subprocess.TimeoutExpired) as error:
            if not best_effort:raise
            description={'identityPending':True}
            safety.append(JOURNAL,plan,'STATUS',kind,physical,{'syncError':str(error)},sid)
        record_created(plan,kind,physical,{**base,**description},sid)
        if kind=='AWS::EC2::Instance':
            try:details=aws(plan,'ec2','describe-instances','--instance-ids',physical,missing=('InvalidInstanceID.NotFound',))
            except (RuntimeError,subprocess.TimeoutExpired)as error:
                safety.append(JOURNAL,plan,'STATUS',kind,physical,{'inventoryGap':True,'error':str(error)},sid)
                if not best_effort:raise
                continue
            if details is None:
                complete=any(r['event']=='STATUS'and r['id']==physical and r['details'].get('inventoryGap')is False for r in rows(plan))
                if not complete:safety.append(JOURNAL,plan,'STATUS',kind,physical,{'inventoryGap':True,'error':'Instance absent before child inventory captured'},sid)
                continue
            instance=details['Reservations'][0]['Instances'][0]
            tags={x['Key']:x['Value']for x in instance.get('Tags',[])}
            if tags.get(names.RUN_TAG)!=plan['runId']:raise ValueError('Instance ownership tag differs')
            for mapping in instance.get('BlockDeviceMappings',[]):
                if 'Ebs'in mapping:
                    volume=mapping['Ebs']['VolumeId']
                    if volume in protected(plan):raise ValueError('Protected volume referenced by new instance')
                    record_created(plan,'implicit:EBS',volume,{'deleteOnTermination':mapping['Ebs']['DeleteOnTermination']},physical)
            for interface in instance.get('NetworkInterfaces',[]):
                eni=interface['NetworkInterfaceId']
                if eni in protected(plan):raise ValueError('Protected ENI referenced by new instance')
                record_created(plan,'implicit:ENI',eni,{'deleteOnTermination':interface['Attachment']['DeleteOnTermination']},physical)
            observed=any('Ebs'in item for item in instance.get('BlockDeviceMappings',[]))and bool(instance.get('NetworkInterfaces'))
            previously_known=any(r['event']=='STATUS'and r['id']==physical and r['details'].get('inventoryGap')is False for r in rows(plan))
            safety.append(JOURNAL,plan,'STATUS',kind,physical,{'state':instance['State']['Name'],'launchTime':instance['LaunchTime'],'publicIp':instance.get('PublicIpAddress'),'inventoryGap':not(observed or previously_known)},sid)
    return response['StackResourceSummaries']

def creation_spec(plan):
    actor(plan)
    import builder_arming as arming
    arming.configure(ROOT)
    arming.verify_prepared(plan)
    builder_s3.require_unused_name(lambda *args:aws(plan,*args),plan['runId'])
    if plan['estimatedCampaignUsd']>10 or plan['priorEstimateUsd']+plan['estimatedCampaignUsd']>min(50,plan['budgetUsd']):
        raise ValueError('Campaign estimate exceeds authorized run/cumulative budget')
    template=json.loads((ROOT/'template.json').read_text());expiry=datetime.datetime.fromisoformat(plan['fixedCampaignExpiryUtc'])
    if expiry.tzinfo is None:raise ValueError('An explicit UTC-aware campaign expiry is required')
    if expiry.timestamp()-time.time()<plan.get('minimumLaunchHeadroomSeconds',7200):raise ValueError('Insufficient approved fixed-window headroom; no launch')
    if expiry.timestamp()-time.time()>3*3600:raise ValueError('Lifetime exceeds the authorized three-hour maximum')
    expiry=expiry.astimezone(datetime.timezone.utc)
    params={'RunId':plan['runId'],'VpcId':plan['references']['vpc'],'SubnetId':plan['references']['subnet'],'ExpiresAt':expiry.strftime('%Y-%m-%dT%H:%M:%S')}
    return {'templateSha256':safety.canonical_hash(template),'parameters':params,'expiresAtUtc':expiry.isoformat()}


def prepare_intent(plan):
    if any(r['event']=='INTENT'and r['type']=='AWS::CloudFormation::Stack'for r in rows(plan)):
        reconcile(plan)
        raise ValueError('Existing exact intent reconciled; resume it, never create a fresh request')
    details={**creation_spec(plan),'requestToken':str(uuid.uuid4())}
    return safety.append(JOURNAL,plan,'INTENT','AWS::CloudFormation::Stack',plan['runId'],details)


def submit_intent(plan):
    intents=[r for r in rows(plan)if r['event']=='INTENT'and r['type']=='AWS::CloudFormation::Stack']
    if len(intents)!=1:raise ValueError('One persisted exact creation intent required')
    if any(r['event']=='STATUS'and r['type']=='creation-submission'for r in rows(plan)):
        reconcile(plan)
        raise ValueError('Creation was submitted or is uncertain; reconcile, never resend')
    details=intents[0]['details'];current=creation_spec(plan)
    if any(details.get(key)!=value for key,value in current.items()):raise ValueError('Published creation intent changed')
    params=details['parameters'];token=details['requestToken']
    safety.append(JOURNAL,plan,'STATUS','creation-submission',plan['runId'],{'requestToken':token,'state':'SUBMITTED_OR_UNCERTAIN'})
    tags=[{'Key':names.RUN_TAG,'Value':plan['runId']},{'Key':names.TAG_PREFIX+'source','Value':plan['payloadSource']}]
    try:
        result=aws(plan,'cloudformation','create-stack','--stack-name',plan['runId'],'--template-body','file://'+str((ROOT/'template.json').resolve()),'--parameters',json.dumps([{'ParameterKey':k,'ParameterValue':v}for k,v in params.items()]),'--tags',json.dumps(tags),'--capabilities','CAPABILITY_NAMED_IAM','--timeout-in-minutes','15','--on-failure','DELETE','--client-request-token',token)
    except BaseException:
        reconcile(plan)
        raise
    safety.append(JOURNAL,plan,'CREATED','AWS::CloudFormation::Stack',result['StackId'],details)
    print('Created exact stack:',result['StackId'],'expiryUTC',details['expiresAtUtc'])
    return result['StackId']


def launch(plan):
    # Operator fallback has a durable local journal. Hosted orchestration uses
    # prepare_intent, uploads it to GitHub, then calls submit_intent separately.
    prepare_intent(plan)
    return submit_intent(plan)

def authorize(plan):
    identity=actor(plan);entry=stack_entry(plan);sid=entry['id']
    response=aws(plan,'cloudformation','describe-stacks','--stack-name',sid,missing=('does not exist',))
    if response is None:return None
    stack=response['Stacks'][0];parameters={x['ParameterKey']:x['ParameterValue']for x in stack['Parameters']};stack['Tags']={x['Key']:x['Value']for x in stack.get('Tags',[])}
    template=aws(plan,'cloudformation','get-template','--stack-name',sid,'--template-stage','Original')['TemplateBody']
    members=aws(plan,'cloudformation','list-stack-resources','--stack-name',sid)['StackResourceSummaries']
    unique={}
    for row in members:
        if row.get('PhysicalResourceId'):
            item=metadata(plan,row['ResourceType'],row['PhysicalResourceId'])
            if 'uniqueId'in item:
                record_created(plan,row['ResourceType'],row['PhysicalResourceId'],{**item,'cfnStackId':sid},sid)
                unique[(row['ResourceType'],row['PhysicalResourceId'])]=item['uniqueId']
    return safety.authorize_stack_deletion(plan,rows(plan),identity,stack,template,parameters,[(m['ResourceType'],m['PhysicalResourceId'])for m in members if m.get('PhysicalResourceId')],protected(plan),unique)


def authorize_instance(plan,identity):
    """Instance termination does not wait on unrelated IAM/S3 metadata reads."""
    actor(plan);entry=stack_entry(plan);sid=entry['id']
    expected=f"arn:aws:cloudformation:{plan['region']}:{plan['account']}:stack/{plan['runId']}/"
    if not sid.startswith(expected):raise ValueError('Termination stack ARN account/region/namespace differs')
    owned=safety.effective_created(rows(plan)).get(('AWS::EC2::Instance',identity))
    if not owned or owned['parent']!=sid or identity in protected(plan):raise ValueError('Termination ID is not an exact owned child')
    response=aws(plan,'ec2','describe-instances','--instance-ids',identity,missing=('InvalidInstanceID.NotFound',))
    if response is None:return 'absent'
    instance=response['Reservations'][0]['Instances'][0]
    if instance['State']['Name']=='terminated':return 'terminated'
    stack=aws(plan,'cloudformation','describe-stacks','--stack-name',sid)['Stacks'][0]
    tags={row['Key']:row['Value']for row in stack.get('Tags',[])}
    parameters={row['ParameterKey']:row['ParameterValue']for row in stack.get('Parameters',[])}
    original=aws(plan,'cloudformation','get-template','--stack-name',sid,'--template-stage','Original')['TemplateBody']
    if (stack['StackId']!=sid or tags.get(names.RUN_TAG)!=plan['runId']or
            parameters!=entry['details']['parameters']or safety.canonical_hash(original)!=entry['details']['templateSha256']):
        raise ValueError('Termination stack provenance changed')
    members=aws(plan,'cloudformation','list-stack-resources','--stack-name',sid)['StackResourceSummaries']
    if not any(row.get('PhysicalResourceId')==identity and row['ResourceType']=='AWS::EC2::Instance'and
               row['LogicalResourceId']==owned['details'].get('logicalId')for row in members):
        raise ValueError('Termination target is no longer the recorded stack member')
    instance_tags={row['Key']:row['Value']for row in instance.get('Tags',[])}
    if instance_tags.get(names.RUN_TAG)!=plan['runId']:raise ValueError('Termination ownership tag changed')
    children={row['Ebs']['VolumeId']for row in instance.get('BlockDeviceMappings',[])if'Ebs'in row}
    children.update(row['NetworkInterfaceId']for row in instance.get('NetworkInterfaces',[]))
    if children&protected(plan):raise ValueError('Unexpected protected attachment on the owned instance')
    return instance['State']['Name']

def delete(plan):
    # Evidence failures cannot block independently ownership-authorized cleanup.
    sid=authorize(plan)
    if sid is None:print('Stack already absent; still run final per-ID verification');return
    safety.append(JOURNAL,plan,'INTENT','delete-exact-stack',sid,{'evidenceReceipt':str(ROOT/'evidence-retained.json')})
    aws(plan,'cloudformation','delete-stack','--stack-name',sid)
    safety.append(JOURNAL,plan,'STATUS','AWS::CloudFormation::Stack',sid,{'state':'DELETE_REQUESTED_NOT_YET_CLEAN'})
    print('Deletion requested for exact owned stack. Completion/per-ID verification still REQUIRED.')

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--state',type=Path,required=True);parser.add_argument('operation',choices=('launch','recover','sync','authorize-cleanup','delete'))
    options=parser.parse_args();configure(options.state);plan=json.loads(PLAN.read_text());rows(plan)
    if options.operation=='launch':launch(plan)
    elif options.operation=='recover':print(reconcile(plan))
    elif options.operation=='sync':print(json.dumps(sync(plan),indent=2))
    elif options.operation=='authorize-cleanup':print(authorize(plan))
    elif options.operation=='delete':delete(plan)

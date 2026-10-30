"""Verify exact created IDs after stack deletion. No deletion or account-wide sweep."""
import hashlib,json
from pathlib import Path
import builder_campaign as campaign
import builder_journal as safety
import builder_namespace as names
import builder_s3
import builder_audit
import builder_cost
import datetime
ROOT=None

def configure(root):
    global ROOT
    ROOT=Path(root).resolve()
    campaign.configure(ROOT)

def evidence(plan):
    receipt=json.loads((ROOT/'evidence-retained.json').read_text())
    expected={key:plan[key]for key in ('account','region','runId','payloadSource')}
    expected['stackArn']=campaign.stack_entry(plan)['id']
    if any(receipt.get(k)!=v for k,v in expected.items()):raise ValueError('Evidence campaign binding differs')
    if receipt.get('scope')not in ('game-results','failure-only')or receipt.get('verdict')not in ('pass','fail','incomplete'):raise ValueError('Evidence scope/verdict missing')
    if receipt['scope']=='failure-only'and receipt['verdict']=='pass':raise ValueError('Failure-only evidence cannot qualify the game')
    if receipt.get('retained')is not True or not receipt.get('files'):raise ValueError('No verified retained evidence')
    for item in receipt['files']:
        path=Path(item['path']).resolve()
        if not path.is_relative_to(ROOT.resolve()):raise ValueError('Evidence path is outside owned QA outputs')
        if hashlib.sha256(path.read_bytes()).hexdigest()!=item['sha256']:raise ValueError('Retained evidence hash differs')
    return receipt

def protected_comparison(baseline,actual):
    changes=[];unavailable=[];compared=0
    for before in baseline:
        after=actual.get(before['id'])
        if after is None:
            retired=before.get('state')=='terminated'
            record={'id':before['id'],'baselineState':before.get('state'),'observation':'not returned by DescribeInstances','baselineAlreadyTerminated':retired}
            unavailable.append(record)
            if not retired:changes.append({**record,'note':'Unexpected unavailable baseline; cause unproved, never reverted'})
            continue
        compared+=1
        selected={'state':after['State']['Name'],'type':after.get('InstanceType'),'vpc':after.get('VpcId'),'subnet':after.get('SubnetId'),
                  'groups':after.get('SecurityGroups',[]),'volumes':after.get('BlockDeviceMappings',[]),'interfaces':[x['NetworkInterfaceId']for x in after.get('NetworkInterfaces',[])]}
        fields=('state','type','vpc','subnet','groups','volumes','interfaces')
        differences={key:{'before':before[key],'after':selected[key]}for key in fields if key in before and before[key]!=selected[key]}
        if differences:changes.append({'id':before['id'],'differences':differences,'note':'Observed concurrent difference; never reverted'})
    return changes,unavailable,compared

def final(plan,stage_errors=None):
    campaign.actor(plan);rows=safety.read_journal(campaign.JOURNAL,plan)
    try:retained=evidence(plan);evidence_error=None
    except Exception as error:evidence_error=str(error);retained={}
    output=[];left=[]
    for row in rows:
        if row['event']!='CREATED':continue
        kind,identity=row['type'],row['id'];data=None;state='unverified'
        if kind=='AWS::CloudFormation::Stack':
            data=campaign.aws(plan,'cloudformation','describe-stacks','--stack-name',identity,missing=('does not exist',));state='absent'if data is None else data['Stacks'][0]['StackStatus']
        elif kind=='AWS::EC2::Instance':
            data=campaign.aws(plan,'ec2','describe-instances','--instance-ids',identity,missing=('InvalidInstanceID.NotFound',))
            state='absent'if data is None else data['Reservations'][0]['Instances'][0]['State']['Name']
        elif kind=='implicit:EBS':
            data=campaign.aws(plan,'ec2','describe-volumes','--volume-ids',identity,missing=('InvalidVolume.NotFound',));state='absent'if data is None else 'PRESENT'
        elif kind=='implicit:ENI':
            data=campaign.aws(plan,'ec2','describe-network-interfaces','--network-interface-ids',identity,missing=('InvalidNetworkInterfaceID.NotFound',));state='absent'if data is None else 'PRESENT'
        elif kind=='AWS::EC2::SecurityGroup':
            data=campaign.aws(plan,'ec2','describe-security-groups','--group-ids',identity,missing=('InvalidGroup.NotFound',));state='absent'if data is None else 'PRESENT'
        elif kind=='AWS::IAM::Role':
            data=campaign.aws(plan,'iam','get-role','--role-name',identity,missing=('NoSuchEntity',));state='absent'if data is None else 'PRESENT'
        elif kind=='AWS::IAM::InstanceProfile':
            data=campaign.aws(plan,'iam','get-instance-profile','--instance-profile-name',identity,missing=('NoSuchEntity',));state='absent'if data is None else 'PRESENT'
        elif kind=='AWS::Scheduler::Schedule':
            logical=row['details']['logicalId']
            if logical not in ('LinuxExpiry','WindowsExpiry'):raise ValueError('Unknown owned schedule')
            name=names.resource_name(plan['runId'],'linux-expiry'if logical=='LinuxExpiry'else'windows-expiry')
            data=campaign.aws(plan,'scheduler','get-schedule','--name',name,'--group-name',names.resource_name(plan['runId'],'expiry-group'),missing=('ResourceNotFoundException',));state='absent'if data is None else'PRESENT'
        elif kind=='AWS::Scheduler::ScheduleGroup':
            data=campaign.aws(plan,'scheduler','get-schedule-group','--name',identity,missing=('ResourceNotFoundException',));state='absent'if data is None else'PRESENT'
        elif kind in ('AWS::S3::Bucket','AWS::S3::BucketPolicy','S3::Object'):
            state='absent'if builder_s3.verify_absent(lambda *args:campaign.aws(plan,*args),plan['runId'])else'PRESENT'
        elif kind=='SSM::Command':
            output.append({'type':kind,'id':identity,'finalState':'non-resource history; AWS-managed expiration'})
            continue
        else:raise ValueError('Unknown created resource type cannot be marked clean: '+kind)
        result={'type':kind,'id':identity,'finalState':state};output.append(result)
        gone=state=='absent'or kind=='AWS::EC2::Instance'and state=='terminated'or kind=='AWS::CloudFormation::Stack'and state=='DELETE_COMPLETE'
        if not gone:left.append(result)
    gaps={}
    for row in rows:
        if row['event']=='STATUS'and 'inventoryGap'in row['details']:gaps[row['id']]=row['details']['inventoryGap']
    for identity,unresolved in gaps.items():
        if unresolved:left.append({'type':'inventory-gap','id':identity,'finalState':'UNRESOLVED_CHILD_INVENTORY'})
    baseline=json.loads((ROOT/'protected-instances.json').read_text());ids=[x['id']for x in baseline]
    live=campaign.aws(plan,'ec2','describe-instances','--instance-ids',*ids)['Reservations']
    actual={x['InstanceId']:x for r in live for x in r['Instances']}
    changes,unavailable,compared=protected_comparison(baseline,actual)
    audit=builder_audit.audit(lambda *args:campaign.aws(plan,*args),require_scoped_role=False)
    left.extend(audit['leftovers'])
    for error in audit['errors']:left.append({'type':error['type'],'id':'audit','finalState':'UNVERIFIED'})
    receipt={'account':plan['account'],'region':plan['region'],'runId':plan['runId'],'payloadSource':plan['payloadSource'],
             'typeAudit':audit['checked'],'nonResourceRecords':audit['nonResourceRecords'],'standingIdentity':audit['expectedStandingIdentity'],'cleanupStageErrors':stage_errors or [],'retainedEvidenceValidated':evidence_error is None,'evidenceComplete':evidence_error is None and retained.get('scope')=='game-results','testVerdict':retained.get('verdict','incomplete'),'testEvidenceScope':retained.get('scope','missing'),'evidenceError':evidence_error,'allCreatedResourcesGone':not left,'resources':output,'leftovers':left,'protectedObservedChanges':changes,
             'protectedRequestedInstances':len(baseline),'protectedComparedInstances':compared,'protectedUnavailable':unavailable,'protectedComparisonComplete':not unavailable,
             'protectedFieldsChecked':['state','instanceType','vpc','subnet','securityGroups','blockDeviceMappings','networkInterfaceIds'],'protectedNotModifiedByController':'All mutation operations constrained to this exact new stack; no protected ID was used as a deletion target.'}
    observed=datetime.datetime.now(datetime.timezone.utc).isoformat()
    receipt['observedAtUtc']=observed
    byte_count=sum(Path(item['path']).stat().st_size for item in retained.get('files',[])if Path(item['path']).is_file())
    receipt['cost']=builder_cost.calculate(plan,rows,output,observed,byte_count)
    (ROOT/'cleanup-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
    if left:raise RuntimeError('Owned resources remain; cleanup is NOT complete')
    safety.append(campaign.JOURNAL,plan,'CLEANUP_COMPLETE','campaign',plan['runId'],{'receiptSha256':hashlib.sha256((ROOT/'cleanup-receipt.json').read_bytes()).hexdigest()})
    return receipt

if __name__=='__main__':
    import argparse
    parser=argparse.ArgumentParser();parser.add_argument('--state',type=Path,required=True)
    options=parser.parse_args();configure(options.state)
    print(json.dumps(final(json.loads((ROOT/'plan.json').read_text())),indent=2))

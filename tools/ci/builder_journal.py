"""Deletion authorization only. No prefix/tag sweep can authorize a deletion."""
import datetime, hashlib, json, os, uuid
from pathlib import Path
import builder_namespace as names
import re

EVENTS={'INTENT','CREATED','REFERENCED','STATUS','EVIDENCE','DELETED','CLEANUP_COMPLETE'}

def canonical_hash(value):
    if isinstance(value,str): value=json.loads(value)
    return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':')).encode()).hexdigest()

def read_journal(path,plan):
    rows=[]
    for line in Path(path).read_text().splitlines():
        row=json.loads(line)
        required={'schemaVersion','eventId','timestampUtc','event','account','region','runId','type','id','parent','dependencies','details'}
        if not required<=row.keys() or row['schemaVersion']!=1 or row['event'] not in EVENTS:raise ValueError('Malformed journal cannot authorize cleanup')
        uuid.UUID(row['eventId']);datetime.datetime.fromisoformat(row['timestampUtc'])
        if any(row[k]!=plan[k]for k in ('account','region','runId')):raise ValueError('Journal identity mismatch')
        rows.append(row)
    return rows

def append(path,plan,event,kind,identity,details=None,parent=None,dependencies=()):
    if event not in EVENTS:raise ValueError('Unknown event')
    row={'schemaVersion':1,'eventId':str(uuid.uuid4()),'timestampUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
         'event':event,'account':plan['account'],'region':plan['region'],'runId':plan['runId'],'type':kind,'id':identity,
         'parent':parent,'dependencies':list(dependencies),'details':details or {}}
    fd=os.open(path,os.O_WRONLY|os.O_APPEND|os.O_CREAT,0o600)
    with os.fdopen(fd,'a')as file:file.write(json.dumps(row)+'\n');file.flush();os.fsync(file.fileno())
    return row

def effective_created(rows):
    owned={}
    for row in rows:
        if row['event']=='CREATED':
            if (row['type'],row['id']) in owned:raise ValueError('Duplicate CREATED identity')
            owned[(row['type'],row['id'])]={**row,'details':dict(row['details'])}
        elif row['event']=='STATUS' and row['details'].get('resourceMetadataConfirmation'):
            prior=owned.get((row['type'],row['id']));details=row['details']
            if not prior or row['parent']!=prior['parent'] or details.get('cfnStackId')!=prior['details'].get('cfnStackId'):raise ValueError('Unproven CFN metadata confirmation')
            old=prior['details'].get('logicalId');new=details.get('logicalId')
            if not new or old and old!=new:raise ValueError('Immutable logical resource changed')
            prior['details']['logicalId']=new
        elif row['event']=='STATUS' and row['details'].get('identityConfirmation'):
            prior=owned.get((row['type'],row['id']));details=row['details']
            if not prior or prior['type']!=row['type'] or details.get('cfnStackId')!=prior['details'].get('cfnStackId') or not details.get('ownershipVerified'):
                raise ValueError('Unproven identity confirmation')
            old=prior['details'].get('uniqueId');new=details.get('uniqueId')
            if not new or old and old!=new:raise ValueError('Immutable IAM identity changed')
            prior['details'].update(details)
    return owned


def authorize_stack_deletion(plan,rows,actor,stack,template,parameters,members,protected_ids,live_unique_ids):
    if not re.fullmatch(names.RUN_PATTERN,plan.get('runId','')):raise ValueError('Invalid builder run namespace')
    if actor['Account']!=plan['account']:raise ValueError('Wrong AWS account')
    created=[r for r in rows if r['event']=='CREATED' and r['type']=='AWS::CloudFormation::Stack']
    if len(created)!=1:raise ValueError('Exact CREATED stack required')
    entry=created[0];arn=entry['id'];parts=arn.split(':',5)
    if len(parts)!=6 or parts[2]!='cloudformation' or parts[3]!=plan['region'] or parts[4]!=plan['account']:raise ValueError('Stack ARN account/region mismatch')
    if not parts[5].startswith('stack/'+plan['runId']+'/'):raise ValueError('Stack name differs from exact builder run')
    if stack['StackId']!=arn or stack['Tags'].get(names.RUN_TAG)!=plan['runId']:raise ValueError('Stack ownership mismatch')
    if canonical_hash(template)!=entry['details']['templateSha256'] or parameters!=entry['details']['parameters']:raise ValueError('Stack template/parameters changed')
    owned={key:value for key,value in effective_created(rows).items() if value['type']!='AWS::CloudFormation::Stack'}
    for physical in members:
        if physical[1] in protected_ids:raise ValueError('Existing referenced resource is PROTECTED')
        if physical not in owned:raise ValueError('Unjournaled physical member; synchronize creation records before cleanup')
        if owned[physical].get('parent')!=arn:raise ValueError('Physical member belongs to another created parent')
    for identity,unique in live_unique_ids.items():
        if identity not in owned or owned[identity]['details'].get('uniqueId')!=unique:raise ValueError('Replaced or unowned IAM identity')
    return arn

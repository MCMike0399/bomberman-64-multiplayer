"""Exact-instance dispatch; SSM is control plane, never the graphical desktop."""
import base64
import datetime
import json
import re
import uuid

import builder_journal as journal
import builder_namespace as names


DOCUMENTS={'Linux':'AWS-RunShellScript','Windows':'AWS-RunPowerShellScript'}


def task(plan,instance,target,payload_sha256):
    if target not in ('windows-x64','linux-x64'):raise ValueError('Unsupported native target')
    if not re.fullmatch(r'i-[0-9a-f]{8,17}',instance):raise ValueError('Invalid exact instance ID')
    if not re.fullmatch(r'[0-9a-f]{40}',plan['payloadSource']):raise ValueError('Full source SHA required')
    if not re.fullmatch(r'[0-9a-f]{64}',payload_sha256):raise ValueError('Frozen manifest digest required')
    if not re.fullmatch(names.RUN_PATTERN,plan['runId']):raise ValueError('Invalid run namespace')
    if not re.fullmatch(r'[1-9][0-9]*',str(plan.get('verificationRunId',''))):raise ValueError('Exact trusted Verification run required')
    expiry=datetime.datetime.fromisoformat(plan['fixedCampaignExpiryUtc'])
    if expiry.tzinfo is None:raise ValueError('UTC-aware fixed expiry required')
    return {'schemaVersion':1,'runId':plan['runId'],'account':names.ACCOUNT,'region':names.REGION,
            'instanceId':instance,'target':target,'source':plan['payloadSource'],
            'verificationRunId':str(plan['verificationRunId']),
            'payloadManifestSha256':payload_sha256,'expiresAtUtc':expiry.isoformat(),
            'executionContext':'aws-ssm-interactive-desktop'}


def commands(osname,descriptor):
    """Only a fixed, preinstalled dispatcher receives a data-only descriptor.

    The dispatcher must start the Windows interactive user's scheduled task;
    executing the game in this SSM LocalSystem session cannot qualify graphics.
    """
    encoded=base64.b64encode(json.dumps(descriptor,sort_keys=True).encode()).decode()
    if osname=='Linux':return ['/opt/sancus-builder/bin/dispatch '+encoded]
    if osname=='Windows':return [r'& C:\sancus-builder\bin\dispatch.ps1 -Descriptor '+encoded]
    raise ValueError('Unknown operating system')


def verify_instance(plan,aws,instance,osname):
    response=aws('ec2','describe-instances','--instance-ids',instance)
    values=[item for row in response.get('Reservations',[])for item in row.get('Instances',[])]
    if len(values)!=1:raise ValueError('Exact instance inventory missing or ambiguous')
    item=values[0];tags={row['Key']:row['Value']for row in item.get('Tags',[])}
    expected=next(row for row in plan['instances']if row['os']==osname)
    if (item['InstanceId']!=instance or tags.get(names.RUN_TAG)!=plan['runId']or
            item['ImageId']!=expected['ami']or item['InstanceType']!='g4dn.xlarge'or
            item['State']['Name']!='running'):
        raise ValueError('Native dispatch ownership, AMI, type or running state differs')
    if aws('sts','get-caller-identity')['Account']!=plan['account']or plan['account']!=names.ACCOUNT:
        raise ValueError('Wrong dispatch account')
    return item


def dispatch(plan,path,aws,instance,osname,manifest_sha256):
    verify_instance(plan,aws,instance,osname)
    rows=journal.read_journal(path,plan)if path.exists()else[]
    if any(row['event']=='INTENT'and row['type']=='SSM::Command'and row['parent']==instance for row in rows):
        raise ValueError('Existing command intent: reconcile it, never blindly resend')
    descriptor=task(plan,instance,osname.lower()+'-x64',manifest_sha256)
    nonce=str(uuid.uuid4());comment='sancus-builder:'+nonce
    intent=journal.append(path,plan,'INTENT','SSM::Command',nonce,
                          {'comment':comment,'descriptor':descriptor,'document':DOCUMENTS[osname]},instance)
    # A lost response leaves a durable unresolved intent. SendCommand has no
    # idempotency token; automatic resend could execute the game twice.
    response=aws('ssm','send-command','--instance-ids',instance,'--document-name',DOCUMENTS[osname],
                 '--comment',comment,'--timeout-seconds','60','--parameters',
                 json.dumps({'commands':commands(osname,descriptor),'executionTimeout':['9000']}))
    command=response['Command']
    if command.get('InstanceIds')!=[instance]or command.get('Comment')!=comment:
        raise ValueError('SSM response identity differs; retain unresolved intent')
    journal.append(path,plan,'CREATED','SSM::Command',command['CommandId'],
                   {'intentId':intent['id'],'comment':comment,'nonResourceRecord':True},instance)
    return command['CommandId']


def reconcile(plan,path,aws,instance):
    rows=journal.read_journal(path,plan)
    intents=[row for row in rows if row['event']=='INTENT'and row['type']=='SSM::Command'and row['parent']==instance]
    if len(intents)!=1:raise ValueError('One exact command intent required')
    intent=intents[0]
    existing=[row for row in rows if row['event']=='CREATED'and row['type']=='SSM::Command'and row['parent']==instance]
    if existing:return existing[0]['id']
    verify_instance(plan,aws,instance,'Windows'if intent['details']['descriptor']['target']=='windows-x64'else'Linux')
    response=aws('ssm','list-commands','--instance-id',instance)
    matches=[item for item in response.get('Commands',[])if item.get('Comment')==intent['details']['comment']]
    if response.get('NextToken'):raise ValueError('Command reconciliation inventory incomplete; no resend authorized')
    if not matches:return None  # Not observed is not proof it was never accepted.
    if len(matches)!=1 or matches[0].get('InstanceIds')!=[instance]or matches[0]['DocumentName']!=intent['details']['document']:
        raise ValueError('Ambiguous or different command identity')
    command=matches[0]
    journal.append(path,plan,'CREATED','SSM::Command',command['CommandId'],
                   {'intentId':intent['id'],'comment':intent['details']['comment'],'nonResourceRecord':True,'reconciled':True},instance)
    return command['CommandId']

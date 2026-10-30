"""FREE permissions smoke: the real campaign stack minus its two instances, plus dry-run launches.

The smoke stack is derived from ``builder_template.build`` by removing exactly the
two ``AWS::EC2::Instance`` resources. The expiry role and both schedules keep
their real names, tags and boundary but target nonexistent dummy instance IDs,
and the schedules are DISABLED. Creation uses the paid path's exact intent and
CreateStack call in ``builder_campaign``; cleanup is the paid path's exact-ID
cleanup and type audit. ``RunInstances`` is only ever called with ``--dry-run``
and the exact launch properties of both real instances: ``DryRunOperation``
means authorized, ``UnauthorizedOperation`` (or anything else) fails the smoke.
No compute is allocated, no paid hold is consulted and no paid attempt is recorded.
"""
import argparse
import base64
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time

import builder_approval
import builder_arming as arming
import builder_bootstrap
import builder_campaign as campaign
import builder_identity
import builder_journal as journal
import builder_namespace as names
import builder_preflight
import builder_s3
import builder_template
import builder_transfer as transfer

ROOT=Path(__file__).resolve().parents[2]
REPOSITORY='MCMike0399/bomberman-64-multiplayer'
SYSTEMS=('Linux','Windows')
REMOVED=tuple(osname+'Instance'for osname in SYSTEMS)
# Syntactically valid, never-issued instance IDs. Even an enabled schedule could
# only call TerminateInstances on these, and the expiry role is limited to them
# plus the run tag.
DUMMY_INSTANCES={'Linux':'i-00000000000000000','Windows':'i-00000000000000001'}
ROOT_GB={'Linux':75,'Windows':80}
# All smoke AWS work must finish this long after the job starts so the shared
# always-cleanup still fits inside the 30-minute smoke job timeout.
WORK_SECONDS_AFTER_JOB_START=14*60
STACK_WAIT_SECONDS=600
PROFILE_PROPAGATION_SECONDS=90
LAUNCH_PROPERTIES={'ImageId','InstanceType','IamInstanceProfile','InstanceInitiatedShutdownBehavior',
                   'PropagateTagsToVolumeOnCreation','MetadataOptions','NetworkInterfaces',
                   'BlockDeviceMappings','UserData','Tags'}


def instances():
    """Identical to the paid preparation plan and disclosed approval instances."""
    return [{'os':osname,'type':'g4dn.xlarge','ami':ami,'rootGb':ROOT_GB[osname]}
            for osname,ami in zip(SYSTEMS,builder_identity.AMIS)]


def placeholder_userdata(plan):
    """The real bootstrap scripts with zero digests: equal shape and size, no payload."""
    zero='0'*64
    return builder_bootstrap.userdata(plan,zero,zero,{'url':'https://awscli.amazonaws.com/AWSCLIV2.msi','sha256':zero})


def instance_arn(identity):
    return f'arn:aws:ec2:{names.REGION}:{names.ACCOUNT}:instance/{identity}'


def references(value,logical_ids):
    """Every Ref/GetAtt/Sub/DependsOn mention of the given logical IDs."""
    found=[]
    def walk(item):
        if isinstance(item,dict):
            for key,child in item.items():
                if key=='Ref'and child in logical_ids:found.append(child)
                elif key=='Fn::GetAtt':
                    target=child[0]if isinstance(child,list)else str(child).split('.',1)[0]
                    if target in logical_ids:found.append(target)
                elif key=='Fn::Sub':
                    text=child[0]if isinstance(child,list)else child
                    found.extend(name for name in re.findall(r'\$\{([^}.!]+)',text)if name in logical_ids)
                elif key=='DependsOn':
                    found.extend(name for name in([child]if isinstance(child,str)else child)if name in logical_ids)
                walk(child)
        elif isinstance(item,list):
            for child in item:walk(child)
    walk(value)
    return found


def derive(real):
    """Return (smoke template, removed instance resources) from the exact real template."""
    template=json.loads(json.dumps(real))
    resources=template['Resources']
    if sorted(key for key,row in resources.items()if row['Type']=='AWS::EC2::Instance')!=sorted(REMOVED):
        raise ValueError('Real campaign must contain exactly the reviewed Linux and Windows instances')
    removed={key:resources.pop(key)for key in REMOVED}
    def rewrite(item):
        if isinstance(item,dict):
            if set(item)=={'Fn::Sub'}and isinstance(item['Fn::Sub'],str):
                text=item['Fn::Sub']
                for osname in SYSTEMS:text=text.replace('${'+osname+'Instance}',DUMMY_INSTANCES[osname])
                return text if '${'not in text else{'Fn::Sub':text}
            return {key:rewrite(child)for key,child in item.items()}
        if isinstance(item,list):return [rewrite(child)for child in item]
        return item
    template['Resources']={key:rewrite(row)for key,row in resources.items()}
    schedules=[row for row in template['Resources'].values()if row['Type']=='AWS::Scheduler::Schedule']
    if len(schedules)!=2:raise ValueError('Both real expiry schedules are required')
    for row in schedules:row['Properties']['State']='DISABLED'
    remaining=references(template,set(REMOVED))
    if remaining:raise ValueError('Smoke template still references removed instances: '+','.join(sorted(set(remaining))))
    template['Description']='FREE permissions smoke of one owned builder campaign; no instances, disabled dummy-target schedules'
    return names.validate_aws_names(template),removed


def dry_run_arguments(resource,physical,stack_tags):
    """Exact RunInstances request equivalent to one real CFN instance resource, with --dry-run."""
    properties=resource['Properties']
    if resource['Type']!='AWS::EC2::Instance'or set(properties)!=LAUNCH_PROPERTIES:
        raise ValueError('Instance launch properties changed; update the permissions smoke first')
    def value(item):
        if isinstance(item,dict):
            if set(item)!={'Ref'}or item['Ref']not in physical:raise ValueError('Unresolved instance reference')
            return physical[item['Ref']]
        return item
    tags=[{'Key':row['Key'],'Value':value(row['Value'])}for row in properties['Tags']]
    tags+=[row for row in stack_tags if row['Key']not in {item['Key']for item in tags}]
    if len(properties['NetworkInterfaces'])!=1:raise ValueError('One reviewed network interface required')
    interface=properties['NetworkInterfaces'][0]
    network=[{'DeviceIndex':int(interface['DeviceIndex']),'SubnetId':value(interface['SubnetId']),
              'AssociatePublicIpAddress':interface['AssociatePublicIpAddress'],'DeleteOnTermination':interface['DeleteOnTermination'],
              'Groups':[value(group)for group in interface['GroupSet']]}]
    kinds=['instance','volume']if properties['PropagateTagsToVolumeOnCreation']is True else['instance']
    return ['--dry-run','--count','1','--image-id',properties['ImageId'],'--instance-type',properties['InstanceType'],
            '--iam-instance-profile',json.dumps({'Name':value(properties['IamInstanceProfile'])}),
            '--instance-initiated-shutdown-behavior',properties['InstanceInitiatedShutdownBehavior'],
            '--metadata-options',json.dumps(properties['MetadataOptions']),
            '--network-interfaces',json.dumps(network),
            '--block-device-mappings',json.dumps(properties['BlockDeviceMappings']),
            # The CLI base64-encodes --user-data itself; CFN takes it pre-encoded.
            '--user-data',base64.b64decode(properties['UserData']).decode(),
            '--tag-specifications',json.dumps([{'ResourceType':kind,'Tags':tags}for kind in kinds])]


def error_code(text):
    match=re.search(r'An error occurred \(([^)]+)\)',str(text))
    return match[1]if match else None


def classify(text):
    code=error_code(text)
    return 'authorized'if code=='DryRunOperation'else'unauthorized'if code=='UnauthorizedOperation'else'error'


def redact(text):
    return re.sub(r'(Encoded authorization failure message:)\s*\S+',r'\1 <redacted>',str(text)).strip()[:2000]


def dry_run(plan,osname,arguments,call=None,sleep=time.sleep,clock=time.time,deadline=None):
    """One authorization-only launch. A real success is impossible and fails loudly."""
    call=call or campaign.aws
    if '--dry-run'not in arguments:raise ValueError('Permissions smoke requires --dry-run')
    deadline=clock()+PROFILE_PROPAGATION_SECONDS if deadline is None else deadline
    attempts=0
    while True:
        attempts+=1
        try:response=call(plan,'ec2','run-instances',*arguments)
        except (RuntimeError,subprocess.TimeoutExpired)as error:text=str(error)
        else:
            return {'os':osname,'result':'error','passed':False,'attempts':attempts,
                    'errorCode':None,'message':'RunInstances returned success despite --dry-run','response':response}
        code=error_code(text)
        # A just-created instance profile is eventually consistent for EC2.
        if code=='InvalidParameterValue'and'iamInstanceProfile'in text and clock()<deadline:
            sleep(5);continue
        result=classify(text)
        return {'os':osname,'result':result,'passed':result=='authorized','attempts':attempts,
                'errorCode':code,'message':redact(text)}


def verify_context(source):
    if (os.environ.get('GITHUB_ACTIONS')!='true'or os.environ.get('GITHUB_REF')!='refs/heads/main'or
            os.environ.get('GITHUB_REPOSITORY')!=REPOSITORY):
        raise ValueError('Hosted permissions smoke requires the main workflow')
    if subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip():
        raise ValueError('Permissions smoke requires a clean source checkout')
    current=json.loads(subprocess.check_output(['gh','api',f'repos/{REPOSITORY}/git/ref/heads/main'],text=True,timeout=90))
    if current['object']['sha']!=source:raise ValueError('Permissions smoke checkout differs from current main')


def write(path,value):
    temporary=Path(str(path)+'.pending');temporary.write_text(json.dumps(value,indent=2,default=str)+'\n');temporary.replace(path)


def prepare(state,hosted=True):
    """Read-only preparation and the durable creation intent; never creates resources."""
    import builder_prepare
    state=Path(state).resolve();state.mkdir(parents=True,exist_ok=True)
    if (state/'plan.json').exists():raise FileExistsError('Never replace an earlier smoke plan')
    clock=json.loads((state/'job-clock.json').read_text())
    if clock['jobTimeoutSeconds']!=1800:raise ValueError('Permissions smoke requires its 30-minute job envelope')
    source=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    if hosted:verify_context(source)
    spending=builder_approval.ledger()
    plan={'schemaVersion':1,'account':names.ACCOUNT,'region':names.REGION,'profile':'sancus','payloadSource':source,
          'runId':names.new_run_id(datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d'),source),
          'permissionsSmoke':True,'verificationRunId':None,'fixedCampaignExpiryUtc':clock['fixedCampaignExpiryUtc'],
          'timing':{key:clock[key]for key in ('jobStartedAtUtc','jobTimeoutSeconds','jobMarginSeconds','cleanupBudgetSeconds')},
          **{key:clock[key]for key in ('controllerRunId','controllerRunAttempt','controllerJobId')},
          'instances':instances(),'estimatedCampaignUsd':0,'priorEstimateUsd':spending['closedEstimateUsd'],
          'budgetUsd':spending['budgetUsd'],
          'references':{'vpc':os.environ.get('SANCUS_BUILDER_VPC_ID',''),'subnet':os.environ.get('SANCUS_BUILDER_SUBNET_ID','')}}
    for key in ('vpc','subnet'):
        if not re.fullmatch(key+r'-[0-9a-f]+',plan['references'][key]):raise ValueError('Approved existing network reference is missing: '+key)
    campaign.configure(state);arming.configure(state)
    aws=lambda *args:campaign.aws({**plan,'dryRun':True},*args)
    observation=builder_preflight.inspect(plan,aws)
    write(state/'smoke-preflight.json',observation)
    # GPU vCPU quota does not affect authorization and a dry run consumes none.
    blocking=[row for row in observation['errors']if row['check']!='quota']
    if blocking:raise ValueError('Resolve prior leftovers/network/image inputs before the permissions smoke')
    protected_path=state/'protected-instances.json'
    protected_path.write_text(json.dumps(builder_prepare.protected(aws),indent=2)+'\n')
    plan['references']['protectedInstancesSha256']=hashlib.sha256(protected_path.read_bytes()).hexdigest()
    real=builder_template.build(plan,placeholder_userdata(plan))
    smoke,_=derive(real)
    (state/'campaign-template.json').write_text(json.dumps(real,indent=2)+'\n')
    (state/'template.json').write_text(json.dumps(smoke,indent=2)+'\n')
    (state/'plan.json').write_text(json.dumps(plan,indent=2)+'\n')
    files=[{'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
           for path in (protected_path,state/'campaign-template.json')]
    receipt={**{key:plan[key]for key in ('account','region','runId','payloadSource','fixedCampaignExpiryUtc')},
             'preparationComplete':True,'permissionsSmoke':True,'planSha256':journal.canonical_hash(plan),
             'templateSha256':journal.canonical_hash(smoke),'files':files}
    write(state/'prepared.json',receipt)
    # Same exact-intent path as a paid launch: budget, expiry, bucket-name and
    # prepared-byte checks, then the durable INTENT row with its request token.
    campaign.prepare_intent(plan)
    return plan


def owned_physical(plan):
    logical={row['details'].get('logicalId'):row['id']for row in journal.effective_created(campaign.rows(plan)).values()}
    for key in ('QaSecurityGroup','AgentProfile','ExpiryRole'):
        if not logical.get(key):raise ValueError('Smoke stack member was not journaled: '+key)
    return logical


def verify_schedules(plan,api=None):
    api=api or(lambda *args:campaign.aws(plan,*args))
    run=plan['runId'];group=names.resource_name(run,'expiry-group');observed={}
    role=api('iam','get-role','--role-name',names.resource_name(run,'expiry'))['Role']
    policy=api('iam','get-role-policy','--role-name',role['RoleName'],'--policy-name',names.resource_name(run,'termination'))['PolicyDocument']
    if isinstance(policy,str):policy=json.loads(policy)
    resources=policy['Statement'][0]['Resource']
    if resources!=[instance_arn(DUMMY_INSTANCES[osname])for osname in SYSTEMS]:
        raise ValueError('Expiry role may target something other than the dummy instances')
    for osname in SYSTEMS:
        schedule=api('scheduler','get-schedule','--group-name',group,'--name',names.resource_name(run,osname.lower()+'-expiry'))
        target=schedule['Target']
        if (schedule['State']!='DISABLED'or json.loads(target['Input'])!={'InstanceIds':[DUMMY_INSTANCES[osname]]}or
                target['Arn']!='arn:aws:scheduler:::aws-sdk:ec2:terminateInstances'or target['RoleArn']!=role['Arn']):
            raise ValueError('Smoke schedule is enabled or targets a non-dummy instance')
        observed[osname]={'state':schedule['State'],'target':DUMMY_INSTANCES[osname],
                          'actionAfterCompletion':schedule.get('ActionAfterCompletion'),
                          'expression':schedule['ScheduleExpression']}
    return observed


def run(state,now=time.time):
    state=Path(state).resolve();campaign.configure(state);arming.configure(state)
    plan=json.loads(campaign.PLAN.read_text())
    if plan.get('permissionsSmoke')is not True:raise ValueError('Not a permissions-smoke plan')
    started=datetime.datetime.fromisoformat(plan['timing']['jobStartedAtUtc']).timestamp()
    deadline=min(now()+STACK_WAIT_SECONDS,started+WORK_SECONDS_AFTER_JOB_START)
    result={'schemaVersion':1,'mode':'permissions-smoke','runId':plan['runId'],'source':plan['payloadSource'],
            'account':plan['account'],'region':plan['region'],'instancesAllocated':0,'hardwareQualified':False,
            'paidAttempt':False,'checks':{},'dryRuns':[],'passed':False,
            'scope':'Creates and deletes only zero-cost campaign resources; RunInstances is authorization-only (--dry-run).'}
    output=state/'permissions-smoke.json'
    def check(name,operation):
        try:result['checks'][name]={'passed':True,'observed':operation()}
        except Exception as error:result['checks'][name]={'passed':False,'error':redact(error)}
        write(output,result)
        return result['checks'][name]['passed']
    try:
        # The same CreateStack request, tags, capabilities and request token as a paid launch.
        if check('create-stack',lambda:campaign.submit_intent(plan))and check('stack-create-complete',lambda:campaign.wait_stack(
                plan,campaign.stack_entry(plan)['id'],'create',deadline,observe=lambda:campaign.sync(plan,best_effort=True))):
            if check('journal-every-member',lambda:len(campaign.sync(plan))):
                check('schedule-auto-delete-update',lambda:(arming.set_schedule_auto_delete(plan),'ActionAfterCompletion=DELETE requested')[1])
                check('disabled-dummy-schedules',lambda:verify_schedules(plan))
                def stage():
                    path=state/'smoke-object.bin';path.write_bytes(b'sancus-builder permissions smoke\n')
                    item={'path':str(path),'objectKey':'payload/input-000.bin','size':path.stat().st_size,
                          'sha256':transfer.sha256(path)}
                    transfer.upload_inputs(builder_s3.Objects(lambda *args:campaign.aws(plan,*args),plan['runId']),plan,campaign.JOURNAL,[item])
                    return item['objectKey']
                check('staging-put-object',stage)
                real=json.loads((state/'campaign-template.json').read_text())
                physical={**owned_physical(plan),'RunId':plan['runId'],'SubnetId':plan['references']['subnet']}
                for osname in SYSTEMS:
                    def launch(osname=osname):
                        record=dry_run(plan,osname,dry_run_arguments(real['Resources'][osname+'Instance'],physical,campaign.stack_tags(plan)),
                                       deadline=min(now()+PROFILE_PROPAGATION_SECONDS,started+WORK_SECONDS_AFTER_JOB_START))
                        result['dryRuns'].append(record)
                        if not record['passed']:raise RuntimeError(osname+' RunInstances dry run: '+record['result']+' '+str(record['errorCode']))
                        return record['errorCode']
                    check('dry-run-'+osname.lower(),launch)
    finally:
        result['passed']=bool(result['checks'])and all(row['passed']for row in result['checks'].values())and \
            len(result['dryRuns'])==2 and all(row['passed']for row in result['dryRuns'])
        result['cleanupRequired']='Always-cleanup deletes the exact stack and staging bucket, then audits every type.'
        write(output,result)
    return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--state',type=Path,required=True)
    parser.add_argument('operation',choices=('prepare','run'));args=parser.parse_args()
    if args.operation=='prepare':
        plan=prepare(args.state);print('Prepared FREE permissions smoke',plan['runId'],'; no resources created.')
    else:
        outcome=run(args.state);print(json.dumps({key:outcome[key]for key in ('passed','checks')},indent=2))
        if not outcome['passed']:raise SystemExit('Permissions smoke failed; exact-ID cleanup still runs.')

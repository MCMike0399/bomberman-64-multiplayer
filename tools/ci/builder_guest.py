"""Fixed guest workload. Bootstrap supplies cached, immutable inputs and a desktop."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import threading
import datetime

import builder_namespace as names
import builder_payload
import builder_s3
import native_execution

ROLES={'source','artifact','godot','pillow','dotnet','python','git','pwsh','driver','audio'}
EVIDENCE_SUFFIXES={'.json','.jsonl','.gz','.png','.bin','.log','.txt','.csv','.xml','.md','.dmp','.res'}


def digest(path):
    with Path(path).open('rb')as stream:return hashlib.file_digest(stream,'sha256').hexdigest()


def validate_inputs(manifest,descriptor):
    for key in ('runId','source','account','region'):
        if manifest.get(key)!=descriptor.get(key):raise ValueError('Guest input manifest identity differs')
    if manifest.get('schemaVersion')!=1:raise ValueError('Unknown input schema')
    rows=manifest.get('files')
    if not isinstance(rows,list)or not rows or len(rows)>100:raise ValueError('Invalid guest input count')
    selected=[];keys=set();files=set()
    for row in rows:
        if row.get('target')not in ('all','windows-x64','linux-x64')or row.get('role')not in ROLES:
            raise ValueError('Unknown payload target or role')
        if not re.fullmatch(r'payload/input-[0-9]{3}\.bin',row.get('objectKey',''))or row['objectKey']in keys:
            raise ValueError('Invalid or repeated payload object')
        keys.add(row['objectKey'])
        if (not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_. -]{0,180}',row.get('fileName',''))or
                '..'in row['fileName']or not re.fullmatch(r'[0-9a-f]{64}',row.get('sha256',''))or
                type(row.get('size'))is not int or not 0<=row['size']<=5*1024**3):
            raise ValueError('Invalid frozen filename, digest or size')
        if row['target']not in ('all',descriptor['target']):continue
        if row['fileName'].casefold()in files:raise ValueError('Ambiguous guest filename')
        files.add(row['fileName'].casefold());selected.append(row)
    if sum(row['role']=='source'for row in selected)!=1:raise ValueError('One exact public source snapshot required')
    if not any(row['role']=='artifact'for row in selected):raise ValueError('No actual package artifacts')
    return selected


def prepare_source(cache,work,manifest,descriptor):
    rows=validate_inputs(manifest,descriptor);cache=Path(cache);work=Path(work)
    for row in rows:
        path=cache/row['fileName']
        if path.is_symlink()or path.stat().st_size!=row['size']or digest(path)!=row['sha256']:
            raise ValueError('Cached immutable input differs')
    source=next(row for row in rows if row['role']=='source')
    builder_payload.unpack_source(cache/source['fileName'],work,descriptor['source'])
    for row in rows:
        if row['role']not in ('artifact','godot'):continue
        destination=work/'artifacts'
        if row['role']=='godot':destination/='engine-archives'
        destination.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(cache/row['fileName'],destination/row['fileName'])
    return rows


# Exact owned CLI binaries; never a PATH lookup (Linux installs the pinned v2 bundle).
AWS_CLI={'nt':r'C:\Program Files\Amazon\AWSCLIV2\aws.exe'}.get(os.name,'/opt/sancus-builder/aws-cli/bin/aws')


def object_client(run):
    def aws(service,operation,*arguments):
        command=[AWS_CLI,'--profile','sancus','--region',names.REGION,'--cli-connect-timeout','8',
                 '--cli-read-timeout','60',service,operation,*arguments,'--output','json']
        result=subprocess.run(command,capture_output=True,text=True,timeout=300)
        if result.returncode:
            match=re.search(r'An error occurred \(([^)]+)\)',result.stderr)
            raise builder_s3.ApiError(match[1]if match else'UnverifiedError','Owned guest object operation failed: '+operation)
        return json.loads(result.stdout)if result.stdout.strip()else{}
    return builder_s3.Objects(aws,run)


def publish(client,root,descriptor,input_names,state,extra_files=()):
    """Raw files and an explicit manifest, never a collector-exit-only PASS."""
    if state not in ('core-retained','finished','failed'):raise ValueError('Unknown evidence checkpoint')
    root=Path(root);files=[];snapshot=root/('checkpoint-'+state)
    snapshot.mkdir(exist_ok=False)
    candidates=[(path,path.relative_to(root/'artifacts').as_posix())for path in sorted((root/'artifacts').rglob('*'))
                if path.is_file()and not path.is_symlink()and path.suffix.lower()in EVIDENCE_SUFFIXES
                and path.relative_to(root/'artifacts').as_posix()not in input_names
                and not path.is_relative_to(root/'artifacts/engine-archives')
                and not path.is_relative_to(root/'artifacts/gpu-engine')]
    candidates.extend((Path(path),'guest/'+Path(path).name)for path in extra_files if Path(path).is_file())
    secrets=[os.environ.get(key,'')for key in builder_payload.TOKEN_KEYS]
    for path,relative in candidates:
        # Do not copy caches, environment files or an accidental nested Git repo.
        if any(part.startswith('.')for part in Path(relative).parts):continue
        data=path.read_bytes();builder_payload.reject_credentials(data,secrets)
        frozen=snapshot/relative;frozen.parent.mkdir(parents=True,exist_ok=True);frozen.write_bytes(data)
        # Later progress writes cannot replace a core checkpoint while the host
        # is downloading it. Every checkpoint owns a distinct object prefix.
        relative=state+'/'+relative
        key='results/'+descriptor['target']+'/'+state+f'/object-{len(files):06d}.bin'
        client.call('put-object','--key',key,'--body',str(frozen))
        files.append({'path':relative,'objectKey':key,'size':frozen.stat().st_size,'sha256':digest(frozen)})
    if not files:raise ValueError('Guest produced no retainable evidence')
    manifest={'schemaVersion':1,**{key:descriptor[key]for key in ('runId','source','account','region','target','instanceId')},
              'state':state,'files':files,'gameVerdict':'not-inferred','executionContext':'aws-ssm-interactive-desktop'}
    path=root/'result-manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n')
    client.call('put-object','--key','results/'+descriptor['target']+'/manifest.json','--body',str(path))
    return manifest


# The controller acknowledges only after durably retaining the guest's core evidence
# (campaign 2 attempt 3: ~1 h for 4.9 GB). Wait up to this long, never past expiry-20 min.
CORE_ACK_WAIT_SECONDS=5400
# Without s3:ListBucket, S3 answers a missing key with 403 AccessDenied rather than 404.
# Until the deadline that means "not yet"; each sample is recorded (run 41178169234).
CORE_ACK_PENDING_CODES=builder_s3.GUEST_PENDING


def await_core_retention(client,root,descriptor,manifest_sha,now=time.monotonic,samples=None):
    remaining=datetime.datetime.fromisoformat(descriptor['expiresAtUtc']).timestamp()-time.time()-20*60
    deadline=now()+min(CORE_ACK_WAIT_SECONDS,max(0,remaining));wait=threading.Event();path=Path(root)/'core-retained-ack.json'
    samples=[]if samples is None else samples
    while now()<deadline:
        if builder_s3.fetch_if_present(client,'payload/core-retained-'+descriptor['target']+'.json',path,
                                       CORE_ACK_PENDING_CODES,samples,now):
            ack=json.loads(path.read_text())
            expected={key:descriptor[key]for key in ('runId','source','target')}
            expected['coreManifestSha256']=manifest_sha
            if any(ack.get(key)!=value for key,value in expected.items())or ack.get('retained')is not True:
                raise ValueError('Core retention acknowledgement differs from committed evidence')
            return ack
        wait.wait(min(5,max(0,deadline-now())))
    raise TimeoutError('Core evidence was not acknowledged; no long tours started; pending samples='+json.dumps(samples))


def run(args):
    descriptor=json.loads(args.task.read_text());manifest=json.loads(args.manifest.read_text())
    if digest(args.manifest)!=descriptor['payloadManifestSha256']:raise ValueError('Guest manifest digest differs')
    identity=native_execution.instance_identity()
    if any(identity.get(a)!=descriptor.get(b)for a,b in (
            ('accountId','account'),('region','region'),('instanceId','instanceId'))):
        raise ValueError('Bootstrap task does not name this exact EC2 instance')
    os.environ['SANCUS_BUILDER_TASK']=str(args.task.resolve())
    os.environ['BLAST_GPU_RUN_ID']=descriptor['runId']  # Process-local, never a repository variable.
    inputs=prepare_source(args.cache,args.work,manifest,descriptor)
    execution=native_execution.context(descriptor['source'],descriptor['target'],args.work)
    client=object_client(descriptor['runId']);root=args.work.resolve();output=root/'artifacts/guest-execution.json'
    report={'schemaVersion':1,'source':descriptor['source'],'executionContext':execution,'steps':[],
            'complete':False,'errors':[],'gameVerdict':'not-inferred'}
    input_names={row['fileName']for row in inputs if row['role']=='artifact'}
    expiry=descriptor['expiresAtUtc'];target=descriptor['target'];godot=None
    def step(name,command,limit):
        import datetime
        remaining=datetime.datetime.fromisoformat(expiry).timestamp()-time.time()
        if remaining<20*60+limit:raise TimeoutError('Guest step lacks fixed cleanup headroom: '+name)
        entry={'name':name,'command':command};report['steps'].append(entry)
        log=root/'artifacts'/('guest-'+name+'.log');start=time.monotonic()
        with log.open('w')as stream:
            result=subprocess.run(command,cwd=root,stdout=stream,stderr=subprocess.STDOUT,timeout=limit)
        entry.update(exitCode=result.returncode,seconds=round(time.monotonic()-start,3))
        output.write_text(json.dumps(report,indent=2)+'\n')
        if result.returncode:raise RuntimeError('Guest step failed: '+name)
    try:
        if target=='windows-x64':
            # This script only prepares the disposable user's display/audio.
            step('desktop',['pwsh','-NoProfile','-File','tools/packaging/windows-test-desktop.ps1',
                            '-OwnedSsm','-VerifyOnly','-ExpectedRevision',descriptor['source']],90)
            command=['pwsh','-NoProfile','-Command',
                "$ErrorActionPreference='Stop'; $p=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent(); "
                "@{sessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId; user=$env:USERNAME; "
                "administrator=$p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator); "
                "driverModel=((& nvidia-smi --query-gpu=driver_model.current --format=csv,noheader)-join ' ')} | ConvertTo-Json"]
            session=json.loads(subprocess.check_output(command,text=True,timeout=30))
            path=root/'artifacts/windows-gpu-desktop';path.mkdir(exist_ok=True)
            (path/'session.json').write_text(json.dumps(session,indent=2)+'\n')
            if session['sessionId']==0 or session['administrator']or session['user']!='BlastQa'or'WDDM'not in session['driverModel']:
                raise RuntimeError('Windows worker is not the standard interactive user on WDDM')
        install=[sys.executable,'tools/ci/install_godot.py','--destination','artifacts/gpu-engine',
                 '--archives','artifacts/engine-archives']
        step('engine',install,90)
        if target=='windows-x64':godot=next((root/'artifacts/gpu-engine').rglob('*_console.exe'))
        else:
            godot=next(path for path in (root/'artifacts/gpu-engine').rglob('Godot*')if path.is_file()and path.name.endswith('.x86_64'))
        def collector(phase):
            command=[sys.executable,'tools/ci/gpu_campaign.py','--target',target,'--expected-revision',descriptor['source'],
                     '--phase',phase,'--skip-bisection','--expiry',expiry,'--godot',str(godot)]
            if target=='linux-x64':command=[sys.executable,'tools/packaging/linux-desktop.py','--',*command]
            return command
        step('core',collector('core'),2400)
        output.write_text(json.dumps(report,indent=2)+'\n')
        publish(client,root,descriptor,input_names,'core-retained')
        report['coreAckPendingSamples']=[]
        report['coreRetentionAcknowledgement']=await_core_retention(client,root,descriptor,digest(root/'result-manifest.json'),samples=report['coreAckPendingSamples'])
        if target=='windows-x64':
            step('installed-launcher',[sys.executable,'tools/ci/startup_guard.py','--expected-revision',descriptor['source'],
                                      '--output','artifacts/gpu-installed-startup-guard'],600)
        # Linux also runs the controller-popups x10 and tour-09 x6 diagnostic repeats (about 9 minutes).
        step('remaining',collector('remaining'),3300 if target=='linux-x64' else 2400)
        step('preliminary-qualification',[sys.executable,'tools/ci/gpu_release_gates.py','--target',target,
              '--revision',descriptor['source'],'--verification-run',descriptor['verificationRunId']],120)
        report['complete']=True
    except (Exception,KeyboardInterrupt)as error:report['errors'].append(str(error))
    finally:
        output.write_text(json.dumps(report,indent=2)+'\n')
        publish(client,root,descriptor,input_names,'finished'if report['complete']else'failed')
    return 0 if report['complete']else 1


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for key in ('task','manifest','cache','work'):parser.add_argument('--'+key,type=Path,required=True)
    raise SystemExit(run(parser.parse_args()))

"""Fixed guest workload. Bootstrap supplies cached, immutable inputs and a desktop."""
import argparse
import concurrent.futures
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
        if row.get('target')not in ('all',*names.TARGETS)or row.get('role')not in ROLES:
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


# Exact owned CLI binary; never a PATH lookup. The guest is Windows only since 2026-11-13.
AWS_CLI=r'C:\Program Files\Amazon\AWSCLIV2\aws.exe'


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
    # One terminal checkpoint per lane since 2026-11-17 (two parallel lanes, no core ack).
    if state not in ('finished','failed'):raise ValueError('Unknown evidence checkpoint')
    slot=descriptor['slot']
    if names.SLOTS.get(slot,{}).get('lane')!=descriptor.get('lane'):raise ValueError('Unknown lane slot')
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
        # Every lane slot and checkpoint owns a distinct object prefix.
        key='results/'+slot+'/'+state+f'/object-{len(files):06d}.bin'
        files.append({'path':state+'/'+relative,'objectKey':key,'size':frozen.stat().st_size,'sha256':digest(frozen),'frozen':frozen})
    if not files:raise ValueError('Guest produced no retainable evidence')
    # One CLI call per object took ~1.4 s (656 files in ~15 min, run 44199687411); upload
    # the frozen snapshot concurrently, then publish the manifest only after every object.
    with concurrent.futures.ThreadPoolExecutor(max_workers=PUBLISH_WORKERS)as pool:
        for future in [pool.submit(client.call,'put-object','--key',row['objectKey'],'--body',str(row['frozen']))for row in files]:
            future.result()
    for row in files:del row['frozen']
    manifest={'schemaVersion':1,**{key:descriptor[key]for key in ('runId','source','account','region','target','slot','lane','instanceId')},
              'state':state,'files':files,'gameVerdict':'not-inferred','executionContext':'aws-ssm-interactive-desktop'}
    path=root/'result-manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n')
    client.call('put-object','--key','results/'+slot+'/manifest.json','--body',str(path))
    return manifest


# Two parallel Windows lanes (user decision 2026-11-17). Timing against the fixed
# expiry E (every guest step still refuses to start unless E-now >= 20 min + limit):
# - core lane: desktop, engine, then the core collector (<= 2,400 s; typically ~23 min),
#   then ONE terminal `finished` checkpoint. No acknowledgement wait.
# - remaining lane: desktop, engine, installed-launcher (<= 600 s), then the remaining
#   collector at once: replay (<= 300 s) plus the Windows complete graphical suite
#   (gpu_campaign.WINDOWS_GRAPHICAL_SUITE_BUDGET_SECONDS = 4,800 s; Linux measured
#   ~4,000 s in run 44199687411) plus 120 s of package inspection. The step is admitted
#   only if it can still reach the gate: E-now >= 20 min + HANDOFF_PUBLISH_RESERVE_SECONDS
#   + REMAINING_STEP_SECONDS, i.e. it must start by E-132 min. It then
#   waits for the controller's core handoff, never later than E-20 min minus
#   HANDOFF_PUBLISH_RESERVE_SECONDS (gate <= 120 s, its own `finished` publish and the
#   controller's retention), runs the unchanged gate and publishes `finished`.
PUBLISH_WORKERS=8
CORE_STEP_SECONDS=2400
REMAINING_STEP_SECONDS=300+4800+120
GATE_STEP_SECONDS=120
HANDOFF_WAIT_SECONDS=5400
HANDOFF_PUBLISH_RESERVE_SECONDS=1500
# Without s3:ListBucket, S3 answers a missing key with 403 AccessDenied rather than 404.
# Until the deadline that means "not yet"; each sample is recorded (run 41178169234).
HANDOFF_PENDING_CODES=builder_s3.GUEST_PENDING


def place_core_handoff(client,root,descriptor,receipt_path):
    """Verify the controller-written handoff and place its bytes at their original paths."""
    receipt_path=Path(receipt_path);raw=receipt_path.read_bytes();receipt=json.loads(raw)
    expected={'schemaVersion':1,**{key:descriptor[key]for key in ('runId','source','account','region','target')},
              'coreSlot':names.CORE_SLOT,'remainingSlot':descriptor['slot'],'remainingInstanceId':descriptor['instanceId']}
    if (any(receipt.get(key)!=value for key,value in expected.items())or descriptor['slot']!=names.REMAINING_SLOT or
            not re.fullmatch(names.INSTANCE_PATTERN,str(receipt.get('coreInstanceId')))or
            receipt['coreInstanceId']==descriptor['instanceId']or
            not re.fullmatch(r'[0-9a-f]{64}',str(receipt.get('coreManifestSha256')))):
        raise ValueError('Core handoff names another campaign, lane or instance')
    rows=receipt.get('files')
    if not isinstance(rows,list)or [row.get('path')for row in rows]!=list(names.HANDOFF_FILES):
        raise ValueError('Core handoff does not carry exactly the gate inputs')
    artifacts=Path(root)/'artifacts';staged=[]
    for index,row in enumerate(rows):
        if (row.get('objectKey')!=names.handoff_object(index)or not re.fullmatch(names.HANDOFF_OBJECT_PATTERN,row['objectKey'])or
                not re.fullmatch(r'[0-9a-f]{64}',str(row.get('sha256')))or type(row.get('size'))is not int or not 0<=row['size']<=256*1024**2):
            raise ValueError('Invalid core handoff object')
        temporary=Path(root)/('core-handoff-%03d.bin'%index)
        client.call('get-object','--key',row['objectKey'],str(temporary))
        if temporary.stat().st_size!=row['size']or digest(temporary)!=row['sha256']:
            raise ValueError('Core handoff bytes differ from the controller-retained core lane hash: '+row['path'])
        staged.append((temporary,artifacts/row['path']))
    for temporary,destination in staged:
        # Never replace an existing file: this lane does not run the core collector.
        destination.parent.mkdir(parents=True,exist_ok=True)
        with temporary.open('rb')as source,destination.open('xb')as stream:shutil.copyfileobj(source,stream)
    return {'receiptSha256':hashlib.sha256(raw).hexdigest(),'coreInstanceId':receipt['coreInstanceId'],
            'coreManifestSha256':receipt['coreManifestSha256'],'files':rows}


def await_core_handoff(client,root,descriptor,now=time.monotonic,samples=None,clock=time.time):
    remaining=(datetime.datetime.fromisoformat(descriptor['expiresAtUtc']).timestamp()-clock()
               -20*60-HANDOFF_PUBLISH_RESERVE_SECONDS)
    deadline=now()+min(HANDOFF_WAIT_SECONDS,max(0,remaining));wait=threading.Event();path=Path(root)/'core-handoff.json'
    samples=[]if samples is None else samples
    while now()<deadline:
        if builder_s3.fetch_if_present(client,names.HANDOFF_RECEIPT_KEY,path,HANDOFF_PENDING_CODES,samples,now):
            return place_core_handoff(client,root,descriptor,path)
        wait.wait(min(5,max(0,deadline-now())))
    raise TimeoutError('Core handoff did not arrive inside the fixed window; no gate ran; pending samples='+json.dumps(samples))


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
    slot=descriptor.get('slot');lane=descriptor.get('lane')
    report={'schemaVersion':1,'source':descriptor['source'],'slot':slot,'lane':lane,'executionContext':execution,'steps':[],
            'complete':False,'errors':[],'gameVerdict':'not-inferred'}
    input_names={row['fileName']for row in inputs if row['role']=='artifact'}
    expiry=descriptor['expiresAtUtc'];target=descriptor['target'];godot=None
    def step(name,command,limit,reserve=0):
        # reserve: time a later step of this lane still needs after this one (gate and publish).
        remaining=datetime.datetime.fromisoformat(expiry).timestamp()-time.time()
        if remaining<20*60+reserve+limit:raise TimeoutError('Guest step lacks fixed cleanup headroom: '+name)
        entry={'name':name,'command':command};report['steps'].append(entry)
        log=root/'artifacts'/('guest-'+name+'.log');start=time.monotonic()
        with log.open('w')as stream:
            result=subprocess.run(command,cwd=root,stdout=stream,stderr=subprocess.STDOUT,timeout=limit)
        entry.update(exitCode=result.returncode,seconds=round(time.monotonic()-start,3))
        output.write_text(json.dumps(report,indent=2)+'\n')
        if result.returncode:raise RuntimeError('Guest step failed: '+name)
    try:
        if target not in names.TARGETS:raise ValueError('Unsupported builder target: '+str(target))
        if slot not in names.SLOTS or names.SLOTS[slot]['lane']!=lane or names.SLOTS[slot]['target']!=target:
            raise ValueError('Unsupported builder lane slot: '+str(slot))
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
        godot=next((root/'artifacts/gpu-engine').rglob('*_console.exe'))
        def collector(phase):
            return [sys.executable,'tools/ci/gpu_campaign.py','--target',target,'--expected-revision',descriptor['source'],
                    '--phase',phase,'--skip-bisection','--expiry',expiry,'--godot',str(godot)]
        if lane=='core':
            step('core',collector('core'),CORE_STEP_SECONDS)
        else:
            step('installed-launcher',[sys.executable,'tools/ci/startup_guard.py','--expected-revision',descriptor['source'],
                                      '--output','artifacts/gpu-installed-startup-guard'],600)
            # Starts at once, in parallel with the core lane; never waits for core evidence.
            step('remaining',collector('remaining'),REMAINING_STEP_SECONDS,reserve=HANDOFF_PUBLISH_RESERVE_SECONDS)
            report['coreHandoffPendingSamples']=[]
            report['coreHandoff']=await_core_handoff(client,root,descriptor,samples=report['coreHandoffPendingSamples'])
            output.write_text(json.dumps(report,indent=2)+'\n')
            # The unchanged gate reads the handed-off core files at their original paths.
            step('preliminary-qualification',[sys.executable,'tools/ci/gpu_release_gates.py','--target',target,
                  '--revision',descriptor['source'],'--verification-run',descriptor['verificationRunId']],GATE_STEP_SECONDS)
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

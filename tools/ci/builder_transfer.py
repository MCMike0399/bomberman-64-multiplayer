"""Hash-bound per-run staging; result filenames and manifests are untrusted."""
import hashlib
import concurrent.futures
import json
import time
import os
from pathlib import Path, PurePosixPath
import re
import tempfile

import builder_journal as journal
import builder_namespace as names
import builder_payload


def sha256(path):
    value=hashlib.sha256()
    with Path(path).open('rb')as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):value.update(block)
    return value.hexdigest()


def upload_inputs(client,plan,journal_path,inputs):
    if client.run!=plan['runId']:raise ValueError('Staging run differs')
    secrets=[os.environ.get(key,'')for key in builder_payload.TOKEN_KEYS]
    for item in inputs:
        path=Path(item['path']);key=item['objectKey']
        if not re.fullmatch(r'payload/input-[0-9]{3}\.bin',key):raise ValueError('Unexpected payload object key')
        if path.stat().st_size!=item['size']or sha256(path)!=item['sha256']:
            raise ValueError('Frozen payload changed before upload')
        # Preparation scans decoded archives; this second scan catches any raw
        # credential added between preparation and dispatch without printing it.
        builder_payload.reject_credentials(path.read_bytes(),secrets)
        journal.append(journal_path,plan,'INTENT','S3::Object',key,{'sha256':item['sha256'],'size':item['size']},client.bucket)
        client.call('put-object','--key',key,'--body',str(path),'--metadata',json.dumps({'sha256':item['sha256']}))
        journal.append(journal_path,plan,'CREATED','S3::Object',key,{'sha256':item['sha256'],'size':item['size']},client.bucket)


def publish_bootstrap(client,plan,journal_path,manifest,helper,aws_cli):
    """The helper is the last readiness signal: all inputs/manifest precede it."""
    if client.run!=plan['runId']:raise ValueError('Staging run differs')
    for key,item in [('payload/bootstrap-awscli.msi',aws_cli),('payload/manifest.json',manifest),('payload/bootstrap.zip',helper)]:
        path=Path(item['path'])
        if sha256(path)!=item['sha256']:raise ValueError('Frozen bootstrap bytes changed')
        journal.append(journal_path,plan,'INTENT','S3::Object',key,{'sha256':item['sha256']},client.bucket)
        client.call('put-object','--key',key,'--body',str(path))
        journal.append(journal_path,plan,'CREATED','S3::Object',key,{'sha256':item['sha256']},client.bucket)


HANDOFF_SCOPE='Controller-retained core-lane bytes for the remaining lane gate; not a game verdict.'


def publish_core_handoff(client,plan,core,remaining,destination):
    """Hand the retained core-lane gate inputs to the remaining lane (2026-11-17).

    Only the controller writes `payload/*`; guests cannot. Every file is the exact
    hash-verified local copy of the core lane's retained `finished` checkpoint, and the
    receipt names the core instance and raw core manifest digest. The receipt object
    is written last, so its presence means every listed object already exists.
    """
    retention=core.get('retention',{})
    if (client.run!=plan['runId']or retention.get('runId')!=plan['runId']or
            retention.get('source')!=plan['payloadSource']or retention.get('slot')!=names.CORE_SLOT or
            retention.get('target')!=names.SLOTS[names.CORE_SLOT]['target']or
            retention.get('instanceId')!=core.get('instanceId')or retention.get('retained')is not True or
            core.get('state')!='finished'or not re.fullmatch(r'[0-9a-f]{64}',retention.get('rawManifestSha256',''))or
            not re.fullmatch(names.INSTANCE_PATTERN,str(remaining.get('instanceId')))or
            remaining['instanceId']==core['instanceId']):
        raise ValueError('Core lane retention is incomplete or belongs to different bytes')
    files={item['path']:item for item in retention.get('files',[])}
    destination=Path(destination);rows=[];selected=[]
    # Verify every retained gate input before publishing any object.
    for index,relative in enumerate(names.HANDOFF_FILES):
        item=files.get('finished/'+relative)
        if item is None:raise ValueError('Core lane did not retain a gate input: '+relative)
        if sha256(item['localPath'])!=item['sha256']:raise ValueError('Retained core bytes changed before the handoff')
        selected.append(item)
        rows.append({'path':relative,'objectKey':names.handoff_object(index),'size':Path(item['localPath']).stat().st_size,'sha256':item['sha256']})
    for item,row in zip(selected,rows):
        client.call('put-object','--key',row['objectKey'],'--body',str(item['localPath']))
    receipt={'schemaVersion':1,**{key:plan[key]for key in ('runId','account','region')},'source':plan['payloadSource'],
             'target':names.SLOTS[names.CORE_SLOT]['target'],'coreSlot':names.CORE_SLOT,'coreInstanceId':core['instanceId'],
             'coreManifestSha256':retention['rawManifestSha256'],'remainingSlot':names.REMAINING_SLOT,
             'remainingInstanceId':remaining['instanceId'],'files':rows,'scope':HANDOFF_SCOPE}
    destination.write_text(json.dumps(receipt,indent=2)+'\n')
    client.call('put-object','--key',names.HANDOFF_RECEIPT_KEY,'--body',str(destination))
    return receipt


def lane_slot(slot):
    if slot not in names.SLOTS:raise ValueError('Unknown result lane slot')
    return names.SLOTS[slot]


def validate_results(manifest,plan,slot,max_bytes=8*1024**3):
    spec=lane_slot(slot)
    if (manifest.get('schemaVersion')!=1 or manifest.get('runId')!=plan['runId']or
            manifest.get('account')!=plan['account']or manifest.get('region')!=plan['region']or
            manifest.get('source')!=plan['payloadSource']or manifest.get('target')!=spec['target']or
            manifest.get('slot')!=slot or manifest.get('lane')!=spec['lane']):
        raise ValueError('Result manifest campaign/source/target/slot differs')
    rows=manifest.get('files')
    if not isinstance(rows,list)or not rows or len(rows)>20000:raise ValueError('Invalid result inventory')
    keys=set();paths=set();total=0
    state=manifest.get('state')
    # One terminal checkpoint per lane since 2026-11-17; no intermediate core-retained state.
    if state not in ('finished','failed'):raise ValueError('Unknown retained checkpoint')
    for item in rows:
        path=item.get('path','');key=item.get('objectKey','');size=item.get('size')
        parts=PurePosixPath(path)
        if (not path or '\\'in path or parts.is_absolute()or any(value in ('','.','..')for value in path.split('/'))or
                ':'in path or not re.fullmatch(r'[A-Za-z0-9_./ -]+',path)):
            raise ValueError('Unsafe result path')
        if (not re.fullmatch('results/'+re.escape(slot)+'/'+state+r'/object-[0-9]{6}\.bin',key)or
                not path.startswith(state+'/')or key in keys or path.casefold()in paths):
            raise ValueError('Unexpected or duplicate result object')
        if type(size)is not int or size<0 or not re.fullmatch(r'[0-9a-f]{64}',item.get('sha256','')):
            raise ValueError('Invalid result size/digest')
        keys.add(key);paths.add(path.casefold());total+=size
    if total>max_bytes:raise ValueError('Result bundle exceeds retained-evidence limit')
    return rows


def verify_lanes(plan,slots,handoff,handoff_sha256,read):
    """Two-lane binding shared by the controller and the finalizer (2026-11-17).

    `slots` maps every lane slot to its controller row; `read(slot, relative)` returns
    the (bytes, sha256) of one hash-verified retained `finished/<relative>` file. Both
    lanes must be finished on distinct exact instances, the core lane complete with
    its core step and collection complete, the remaining lane complete with its
    installed-launcher, remaining and gate steps, and the gate's core inputs must be
    byte-identical to the core lane's retained files through the controller handoff.
    """
    import gpu_release_gates
    if set(slots)!=set(names.SLOTS):raise ValueError('Every reviewed lane slot is required')
    identities=[]
    for slot,row in slots.items():
        retention=row.get('retention')or{}
        if row.get('state')!='finished':raise ValueError('Every lane must finish its workload: '+slot)
        if (retention.get('slot')!=slot or retention.get('instanceId')!=row.get('instanceId')or
                retention.get('target')!=names.SLOTS[slot]['target']or retention.get('runId')!=plan['runId']or
                retention.get('source')!=plan['payloadSource']):
            raise ValueError('Lane retention belongs to another slot, instance or campaign: '+slot)
        identities.append(row['instanceId'])
    if len(set(identities))!=len(identities):raise ValueError('Lanes must run on distinct exact instances')
    def document(slot,relative):
        data,digest=read(slot,relative)
        return json.loads(data),digest
    def execution(slot,required):
        report,_=document(slot,'guest-execution.json')
        steps={row.get('name'):row for row in report.get('steps',[])}
        if (report.get('complete')is not True or report.get('errors')!=[]or report.get('slot')!=slot or
                report.get('lane')!=names.SLOTS[slot]['lane']or report.get('source')!=plan['payloadSource']or
                any(steps.get(name,{}).get('exitCode')!=0 for name in required)or
                any(row.get('exitCode')!=0 for row in steps.values())):
            raise ValueError('Lane guest execution is incomplete or a required step failed: '+slot)
        return report
    core,remaining=names.CORE_SLOT,names.REMAINING_SLOT
    execution(core,('desktop','engine','core'))
    target=names.SLOTS[core]['target']
    result,_=document(core,'gpu-campaign-'+target+'-core/campaign-result.json')
    if (result.get('collectionComplete')is not True or result.get('phase')!='core'or
            result.get('source')!=plan['payloadSource']or result.get('target')!=target):
        raise ValueError('Core lane collection is incomplete')
    report=execution(remaining,('desktop','engine','installed-launcher','remaining','preliminary-qualification'))
    expected={'schemaVersion':1,'runId':plan['runId'],'account':plan['account'],'region':plan['region'],
              'source':plan['payloadSource'],'target':target,'coreSlot':core,'coreInstanceId':slots[core]['instanceId'],
              'coreManifestSha256':slots[core]['retention'].get('rawManifestSha256'),'remainingSlot':remaining,
              'remainingInstanceId':slots[remaining]['instanceId']}
    if any(handoff.get(key)!=value for key,value in expected.items())or not re.fullmatch(r'[0-9a-f]{64}',str(expected['coreManifestSha256'])):
        raise ValueError('Core handoff does not name this core instance and manifest')
    if [row.get('path')for row in handoff.get('files',[])]!=list(names.HANDOFF_FILES):
        raise ValueError('Core handoff does not carry exactly the gate inputs')
    for row in handoff['files']:
        _,core_digest=read(core,row['path']);_,lane_digest=read(remaining,row['path'])
        if not row.get('sha256')==core_digest==lane_digest:
            raise ValueError('Gate core inputs are not byte-identical to the retained core lane: '+row['path'])
    guest=report.get('coreHandoff')or{}
    if (guest.get('receiptSha256')!=handoff_sha256 or guest.get('coreInstanceId')!=expected['coreInstanceId']or
            guest.get('coreManifestSha256')!=expected['coreManifestSha256']):
        raise ValueError('Remaining lane did not gate against this exact core handoff')
    receipt,_=document(remaining,'gpu-release-'+target+'/'+gpu_release_gates.receipt_name(target))
    high,_=document(core,names.HANDOFF_FILES[1])
    if receipt.get('primaryHigh')!=high:raise ValueError('Remaining lane receipt does not carry the core lane High evidence')
    return remaining


# Campaign 2 attempt 3 retained 1,611 files (5.3 GB) one CLI process at a time in ~1 h.
RETENTION_WORKERS=12


def retain_results(client,plan,slot,manifest,destination,workers=RETENTION_WORKERS,now=time.monotonic):
    """Receipt proves retention, never that the game assertions passed."""
    if client.run!=plan['runId']:raise ValueError('Staging run differs')
    rows=validate_results(manifest,plan,slot)
    destination=Path(destination).resolve()
    # A new directory prevents symlink/path injection through a previous run.
    destination.mkdir(parents=True,exist_ok=False)
    for item in rows:(destination/item['path']).parent.mkdir(parents=True,exist_ok=True)
    def fetch(item):
        path=destination/item['path']
        with tempfile.NamedTemporaryFile(dir=path.parent,delete=False)as stream:temporary=Path(stream.name)
        try:
            client.call('get-object','--key',item['objectKey'],str(temporary))
            if temporary.stat().st_size!=item['size']or sha256(temporary)!=item['sha256']:
                raise ValueError('Downloaded game evidence digest/size differs')
            temporary.replace(path)
        finally:
            temporary.unlink(missing_ok=True)
        return {**item,'localPath':str(path)}
    started=now();count=max(1,min(workers,len(rows)))
    pool=concurrent.futures.ThreadPoolExecutor(max_workers=count)
    try:
        futures=[pool.submit(fetch,item)for item in rows]
        # Fail closed: the first error (or digest mismatch) fails the whole retention.
        for future in concurrent.futures.as_completed(futures):future.result()
        retained=[future.result()for future in futures]  # manifest order
    finally:
        pool.shutdown(wait=True,cancel_futures=True)
    seconds=max(now()-started,1e-6);total=sum(item['size']for item in rows)
    timing={'workers':count,'files':len(rows),'bytes':total,'seconds':round(seconds,3),
            'throughputMBps':round(total/seconds/1e6,3)}
    receipt={'schemaVersion':1,'runId':plan['runId'],'source':plan['payloadSource'],
             'account':plan['account'],'region':plan['region'],'target':manifest['target'],
             'slot':slot,'lane':manifest['lane'],'instanceId':manifest.get('instanceId'),
             'retained':True,'scope':'game-results-retention','gameVerdict':'not-inferred','retentionTiming':timing,'files':retained}
    (destination/'retention.json').write_text(json.dumps(receipt,indent=2)+'\n')
    return receipt

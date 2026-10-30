"""Hash-bound per-run staging; result filenames and manifests are untrusted."""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import tempfile

import builder_journal as journal
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


def acknowledge_core(client,plan,target,core_manifest_sha,retention,destination):
    """Caller invokes after durable upload/DiveMac retention, before long tours."""
    if (client.run!=plan['runId']or retention.get('runId')!=plan['runId']or
            retention.get('source')!=plan['payloadSource']or retention.get('target')!=target or
            retention.get('retained')is not True or not retention.get('files')or
            not re.fullmatch(r'[0-9a-f]{64}',core_manifest_sha)):
        raise ValueError('Core retention is incomplete or belongs to different bytes')
    for item in retention['files']:
        if sha256(item['localPath'])!=item['sha256']:raise ValueError('Retained core bytes changed before acknowledgement')
    ack={'runId':plan['runId'],'source':plan['payloadSource'],'target':target,
         'coreManifestSha256':core_manifest_sha,'retained':True}
    destination=Path(destination);destination.write_text(json.dumps(ack,indent=2)+'\n')
    client.call('put-object','--key','payload/core-retained-'+target+'.json','--body',str(destination))
    return ack


def validate_results(manifest,plan,target,max_bytes=8*1024**3):
    if target not in ('windows-x64','linux-x64'):raise ValueError('Unknown result target')
    if (manifest.get('schemaVersion')!=1 or manifest.get('runId')!=plan['runId']or
            manifest.get('account')!=plan['account']or manifest.get('region')!=plan['region']or
            manifest.get('source')!=plan['payloadSource']or manifest.get('target')!=target):
        raise ValueError('Result manifest campaign/source/target differs')
    rows=manifest.get('files')
    if not isinstance(rows,list)or not rows or len(rows)>20000:raise ValueError('Invalid result inventory')
    keys=set();paths=set();total=0
    state=manifest.get('state')
    if state not in ('core-retained','finished','failed'):raise ValueError('Unknown retained checkpoint')
    for item in rows:
        path=item.get('path','');key=item.get('objectKey','');size=item.get('size')
        parts=PurePosixPath(path)
        if (not path or '\\'in path or parts.is_absolute()or any(value in ('','.','..')for value in path.split('/'))or
                ':'in path or not re.fullmatch(r'[A-Za-z0-9_./ -]+',path)):
            raise ValueError('Unsafe result path')
        if (not re.fullmatch('results/'+target+'/'+state+r'/object-[0-9]{6}\.bin',key)or
                not path.startswith(state+'/')or key in keys or path.casefold()in paths):
            raise ValueError('Unexpected or duplicate result object')
        if type(size)is not int or size<0 or not re.fullmatch(r'[0-9a-f]{64}',item.get('sha256','')):
            raise ValueError('Invalid result size/digest')
        keys.add(key);paths.add(path.casefold());total+=size
    if total>max_bytes:raise ValueError('Result bundle exceeds retained-evidence limit')
    return rows


def retain_results(client,plan,target,manifest,destination):
    """Receipt proves retention, never that the game assertions passed."""
    if client.run!=plan['runId']:raise ValueError('Staging run differs')
    rows=validate_results(manifest,plan,target)
    destination=Path(destination).resolve()
    # A new directory prevents symlink/path injection through a previous run.
    destination.mkdir(parents=True,exist_ok=False)
    retained=[]
    for item in rows:
        path=destination/item['path'];path.parent.mkdir(parents=True,exist_ok=True)
        with tempfile.NamedTemporaryFile(dir=path.parent,delete=False)as stream:temporary=Path(stream.name)
        try:
            client.call('get-object','--key',item['objectKey'],str(temporary))
            if temporary.stat().st_size!=item['size']or sha256(temporary)!=item['sha256']:
                raise ValueError('Downloaded game evidence digest/size differs')
            temporary.replace(path)
        finally:
            temporary.unlink(missing_ok=True)
        retained.append({**item,'localPath':str(path)})
    receipt={'schemaVersion':1,'runId':plan['runId'],'source':plan['payloadSource'],
             'account':plan['account'],'region':plan['region'],'target':target,
             'retained':True,'scope':'game-results-retention','gameVerdict':'not-inferred','files':retained}
    (destination/'retention.json').write_text(json.dumps(receipt,indent=2)+'\n')
    return receipt

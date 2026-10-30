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


def validate_results(manifest,plan,target,max_bytes=8*1024**3):
    if target not in ('windows-x64','linux-x64'):raise ValueError('Unknown result target')
    if (manifest.get('schemaVersion')!=1 or manifest.get('runId')!=plan['runId']or
            manifest.get('source')!=plan['payloadSource']or manifest.get('target')!=target):
        raise ValueError('Result manifest campaign/source/target differs')
    rows=manifest.get('files')
    if not isinstance(rows,list)or not rows or len(rows)>20000:raise ValueError('Invalid result inventory')
    keys=set();paths=set();total=0
    for item in rows:
        path=item.get('path','');key=item.get('objectKey','');size=item.get('size')
        parts=PurePosixPath(path)
        if (not path or '\\'in path or parts.is_absolute()or any(value in ('','.','..')for value in path.split('/'))or
                ':'in path or not re.fullmatch(r'[A-Za-z0-9_./ -]+',path)):
            raise ValueError('Unsafe result path')
        if key!='results/'+target+'/'+path or key in keys or path.casefold()in paths:
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

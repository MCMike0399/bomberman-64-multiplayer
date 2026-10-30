"""Prepare immutable public-source/runtime payloads before any billable allocation."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import zipfile

TOKEN_KEYS=('GH_TOKEN','GITHUB_TOKEN','ACTIONS_RUNTIME_TOKEN','ACTIONS_ID_TOKEN_REQUEST_TOKEN',
            'KERNEL_DEPLOY_KEY','KERNEL_READ_TOKEN','BLAST_UPDATE_SIGNING_KEY',
            'AWS_ACCESS_KEY_ID','AWS_SECRET_ACCESS_KEY','AWS_SESSION_TOKEN')


def reject_credentials(data,secrets):
    if any(value and len(value)>=16 and value.encode()in data for value in secrets):
        raise ValueError('Credential detected in staging payload; no upload authorized')


def source_bundle(repository,revision,output,secrets=None):
    secrets=[os.environ.get(key,'')for key in TOKEN_KEYS]if secrets is None else secrets
    repository=Path(repository).resolve();output=Path(output).resolve()
    def git(*args):return subprocess.check_output(['git',*args],cwd=repository)
    if git('rev-parse','HEAD').decode().strip()!=revision:raise ValueError('Source checkout differs from frozen revision')
    if subprocess.run(['git','diff','--quiet','HEAD'],cwd=repository).returncode:raise ValueError('Source payload requires a clean tracked tree')
    objects=[revision,*git('rev-list','--objects','--no-object-names',revision+'^{tree}').decode().splitlines()]
    objects=list(dict.fromkeys(objects))
    # Inspect decoded Git objects, not compressed pack bytes. The pack includes
    # only this commit/tree, no Git config, credentials, ignored files or memory.
    for oid in objects:
        reject_credentials(git('cat-file','-p',oid),secrets)
    output.parent.mkdir(parents=True,exist_ok=True)
    if output.exists():raise FileExistsError('Do not overwrite a frozen source payload')
    pack=output.with_suffix('.pack')
    try:
        with pack.open('wb')as stream:
            subprocess.run(['git','pack-objects','--stdout'],cwd=repository,
                           input=('\n'.join(objects)+'\n').encode(),stdout=stream,check=True)
        with zipfile.ZipFile(output,'w',zipfile.ZIP_STORED)as archive:
            archive.write(pack,'source.pack');archive.writestr('revision.txt',revision+'\n')
    finally:
        if pack.exists():pack.unlink()
    return {'path':str(output),'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'source':revision,
            'scope':'Exact shallow public Git snapshot; no parent history or local Git configuration'}


def unpack_source(bundle,destination,revision):
    destination=Path(destination);destination.mkdir(parents=True,exist_ok=False)
    with zipfile.ZipFile(bundle)as archive:
        if set(archive.namelist())!={'source.pack','revision.txt'}or archive.read('revision.txt').decode().strip()!=revision:
            raise ValueError('Source snapshot identity/contents differ')
        subprocess.run(['git','init','--quiet',str(destination)],check=True)
        subprocess.run(['git','config','core.autocrlf','false'],cwd=destination,check=True)
        subprocess.run(['git','index-pack','--stdin'],cwd=destination,input=archive.read('source.pack'),check=True,capture_output=True)
    (destination/'.git/shallow').write_text(revision+'\n')
    subprocess.run(['git','update-ref','refs/heads/main',revision],cwd=destination,check=True)
    subprocess.run(['git','symbolic-ref','HEAD','refs/heads/main'],cwd=destination,check=True)
    subprocess.run(['git','reset','--hard','--quiet',revision],cwd=destination,check=True)
    actual=subprocess.check_output(['git','rev-parse','HEAD'],cwd=destination,text=True).strip()
    if actual!=revision:raise ValueError('Unpacked source commit differs')


def prepare_files(inputs,output):
    """Opaque AWS object keys avoid copying product names into AWS names."""
    output=Path(output);output.mkdir(parents=True,exist_ok=False)
    secrets=[os.environ.get(key,'')for key in TOKEN_KEYS];rows=[]
    for index,item in enumerate(inputs):
        path=Path(item['path']);data=path.read_bytes();reject_credentials(data,secrets)
        digest=hashlib.sha256(data).hexdigest()
        if digest!=item['sha256']:raise ValueError('Frozen input digest changed before staging')
        if zipfile.is_zipfile(path):
            with zipfile.ZipFile(path)as archive:
                for entry in archive.infolist():
                    if not entry.is_dir():reject_credentials(archive.read(entry),secrets)
        rows.append({**item,'objectKey':f'payload/input-{index:03d}.bin','size':len(data)})
    (output/'input-manifest.json').write_text(json.dumps({'files':rows,'credentialsRejected':True},indent=2)+'\n')
    return rows


def public_manifest(plan,rows):
    """The guest gets required byte identities, never operator-local paths."""
    manifest={'schemaVersion':1,'runId':plan['runId'],'source':plan['payloadSource'],
              'account':plan['account'],'region':plan['region'],
              'files':[{key:row[key]for key in ('role','target','fileName','objectKey','size','sha256')}for row in rows]}
    import builder_guest
    for target in ('windows-x64','linux-x64'):
        builder_guest.validate_inputs(manifest,{**manifest,'target':target})
    return manifest

"""Explicit disposable execution provenance; AWS guests never impersonate Actions."""
import datetime
import json
import os
from pathlib import Path
import subprocess
import urllib.request

import builder_namespace as names


def instance_identity():
    # IMDS must stay link-local, bypassing any inherited HTTP proxy. This reads
    # identity only; no credentials or tokens are returned in the receipt.
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
    base='http://169.254.169.254/latest/'
    request=urllib.request.Request(base+'api/token',method='PUT',headers={'X-aws-ec2-metadata-token-ttl-seconds':'60'})
    with opener.open(request,timeout=3)as response:token=response.read().decode()
    request=urllib.request.Request(base+'dynamic/instance-identity/document',headers={'X-aws-ec2-metadata-token':token})
    with opener.open(request,timeout=3)as response:return json.load(response)


def context(revision,target,root):
    if os.environ.get('SANCUS_BUILDER_TASK'):
        task=json.loads(Path(os.environ['SANCUS_BUILDER_TASK']).read_text())
        import re
        if (task.get('schemaVersion')!=1 or task.get('executionContext')!='aws-ssm-interactive-desktop'or
                task.get('account')!=names.ACCOUNT or task.get('region')!=names.REGION or
                task.get('source')!=revision or task.get('target')!=target or
                not re.fullmatch(names.RUN_PATTERN,task.get('runId',''))):
            raise RuntimeError('SSM task source, target or campaign differs')
        expiry=datetime.datetime.fromisoformat(task['expiresAtUtc'])
        if expiry.tzinfo is None or datetime.datetime.now(datetime.timezone.utc)>=expiry:
            raise RuntimeError('SSM fixed campaign window expired')
        identity=instance_identity()
        if (identity.get('accountId')!=task['account']or identity.get('region')!=task['region']or
                identity.get('instanceId')!=task.get('instanceId')):
            raise RuntimeError('Actual IMDS identity differs from the exact SSM task')
        actual=subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()
        if actual!=revision:raise RuntimeError('Guest source snapshot differs from frozen source')
        return {'kind':'aws-ssm-interactive-desktop','runId':task['runId'],'instanceId':identity['instanceId'],
                'account':identity['accountId'],'region':identity['region'],'imageId':identity.get('imageId'),
                'source':actual,'expiresAtUtc':task['expiresAtUtc']}
    if os.environ.get('GITHUB_ACTIONS')=='true':return {'kind':'github-actions'}
    raise RuntimeError('Use an explicit disposable native CI or owned SSM execution context')

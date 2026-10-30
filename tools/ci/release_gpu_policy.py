"""User policy: minor/major bumps need real GPUs; patches use free gates and warnings."""
import json
import os
import re
from pathlib import Path
import render_fingerprint

RECEIPT='Blast-Circuit-graphics-policy.json'


def version(value):
    if not re.fullmatch(r'v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)',value):
        raise ValueError('Expected numeric release version')
    return tuple(map(int,value.removeprefix('v').split('.')))


def decide(previous,candidate,previous_fingerprint=None,current_fingerprint=None,override=None):
    current=version(candidate);prior=version(previous)if previous else None
    if prior and current<=prior:raise ValueError('Candidate must be newer than the latest published release')
    kind='major'if prior is None or current[:2]!=prior[:2]else'patch'
    if override is not None:
        if override.get('version')!=candidate or override.get('kind')not in ('major','patch'):
            raise ValueError('Release-kind override must name this exact version and an explicit kind')
        kind=override['kind']
    warning=None
    if kind=='patch'and previous_fingerprint!=current_fingerprint:
        warning='Rendering inputs changed or the previous fingerprint is unavailable. Consider a minor bump for real-GPU qualification; patch promotion is not blocked.'
    return {'previousVersion':previous,'candidateVersion':candidate,'kind':kind,'requiresGpu':kind=='major','warning':warning,
            'previousFingerprint':previous_fingerprint,'renderFingerprint':current_fingerprint,'override':override}


def read(directory):
    path=directory/RECEIPT
    if not path.exists():return None  # Historical releases predate this policy.
    data=json.loads(path.read_text())
    metadata=json.loads((directory/'build-metadata.json').read_text())
    candidate=metadata.get('release',{}).get('version',metadata.get('version'))
    if not re.fullmatch(r'[0-9a-f]{64}',metadata.get('renderFingerprint','')):
        raise RuntimeError('Graphics policy requires the archived rendering fingerprint')
    expected=decide(data.get('previousVersion'),candidate,data.get('previousFingerprint'),
                    metadata.get('renderFingerprint'),data.get('override'))
    if data.get('schemaVersion')!=1 or data.get('source')!=metadata.get('revision')or any(data.get(key)!=value for key,value in expected.items()):
        raise RuntimeError('Graphics policy differs from its signed release identity or version-derived kind')
    if data['requiresGpu']and(not str(data.get('gpuRunId','')).isdigit()or not str(data.get('verificationRunId','')).isdigit()):
        raise RuntimeError('Major graphics policy lacks bound workflow run identities')
    return data


def asset_names(target,policy):
    import desktop_packages,gpu_release_gates
    values=list(desktop_packages.asset_names(target,include_graphics=policy is None or policy['requiresGpu']))
    if policy and policy['requiresGpu']:values.append(gpu_release_gates.receipt_name(target))
    return values


def bind(directory,repository,version,revision,run_id,gh,*,fetch=False,gpu_run_id=None):
    """Derive from live publication state, then validate before signing or draining."""
    import desktop_packages,gpu_release_gates
    data=derive(repository,version,gh)
    metadata=json.loads((directory/'build-metadata.json').read_text())
    if metadata.get('revision')!=revision or metadata.get('renderFingerprint')!=data['renderFingerprint']:
        raise RuntimeError('Graphics fingerprint differs from the exact Verification artifact')
    gpu_run=gpu_run_id or os.environ.get('BLAST_GPU_QUALIFICATION_RUN_ID','')
    saved=read(directory)
    if not gpu_run and saved:gpu_run=saved.get('gpuRunId','')
    if data['requiresGpu']:
        if fetch:gpu_release_gates.download(repository,gpu_run,revision,directory,gh)
        gpu_release_gates.inspect_all(directory,revision,fingerprint=data['renderFingerprint'])
        if not str(gpu_run).isdigit():raise RuntimeError('Major qualification needs its trusted GPU workflow run ID')
    desktop_packages.inspect(directory,revision,metadata['release'],include_graphics=data['requiresGpu'])
    data.update(schemaVersion=1,source=revision,verificationRunId=str(run_id),gpuRunId=str(gpu_run)if data['requiresGpu']else None)
    if saved:
        if saved!=data:raise RuntimeError('Graphics policy changed after preparation; refusing to rewrite a signed asset')
    else:(directory/RECEIPT).write_text(json.dumps(data,indent=2)+'\n')
    return read(directory)


def derive(repository,candidate,gh):
    pages=json.loads(gh('api',f'repos/{repository}/releases?per_page=100','--paginate','--slurp'))
    releases=[row for page in pages for row in page if not row.get('draft')and not row.get('prerelease')and re.fullmatch(r'v[0-9]+\.[0-9]+\.[0-9]+',row.get('tag_name',''))]
    previous=max(releases,key=lambda row:version(row['tag_name']))if releases else None
    baseline=None
    if previous:
        metadata=json.loads(gh('release','download',previous['tag_name'],'--repo',repository,'--pattern','build-metadata.json','--output','-'))
        baseline=metadata.get('renderFingerprint')
    raw=os.environ.get('BLAST_RELEASE_KIND_OVERRIDE');override=json.loads(raw)if raw else None
    policy=decide(previous['tag_name'].removeprefix('v')if previous else None,candidate,baseline,render_fingerprint.calculate()['sha256'],override)
    text=f"Release graphics policy: {policy['kind']} ({policy['previousVersion']} → {candidate}); real GPU required: {policy['requiresGpu']}"
    print(text)
    if policy['warning']:print('WARNING: '+policy['warning'])
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a')as summary:
            summary.write(text+'\n\n'+(policy['warning']or'')+'\n')
    return policy

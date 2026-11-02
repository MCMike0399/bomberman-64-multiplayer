"""GPU promotion evidence. Functional/software receipts cannot satisfy this boundary."""
import hashlib
import json
from pathlib import Path
import argparse
import shutil
import tempfile
import re
import os
import builder_namespace

SOFTWARE=('warp','basic render','llvmpipe','lavapipe','swiftshader','paravirtual','software')
CLEANUP_RECEIPT='Sancus-builder-cleanup.json'
CLEANUP_TYPES={'CFN','EC2','EBS','ENI','SG','EIP','IAM','Scheduler','Logs','S3','SSM','AMI','Snapshots','KeyPairs'}


def receipt_name(target):return 'Blast-Circuit-'+target+'-gpu.json'
def artifact_name(target):return 'release-gate-gpu-'+target


def physical(device):
    return (bool(device.get('adapter'))and device.get('deviceType')in ('DiscreteGpu','IntegratedGpu')and
            not any(word in device['adapter'].casefold()for word in SOFTWARE))


def inspect_receipt(data,target,revision,archive,download,server,fingerprint,*,require_cleanup=True):
    expected={'schemaVersion':1,'target':target,'source':revision,'archiveSha256':archive,
              'downloadSha256':download,'serverSha256':server,'renderFingerprint':fingerprint}
    if any(data.get(key)!=value for key,value in expected.items()):
        raise RuntimeError('GPU receipt source/archive/download/server/fingerprint mismatch')
    if require_cleanup and(data.get('cleanupAudited')is not True or not re.fullmatch(r'[0-9a-f]{64}',data.get('cleanupSha256',''))):
        raise RuntimeError('GPU qualification requires its completed ownership-safe cleanup audit')
    if data.get('passed')is not True or data.get('primaryDriver')!='vulkan' or data.get('effectiveSamples')!=2:
        raise RuntimeError('GPU original High qualification is incomplete')
    # Receipts made before diagnostic repeats were gated (e.g. run 41304300722) carry no verdict for them.
    if not isinstance(data.get('diagnosticRepeats'),dict)or data['diagnosticRepeats'].get('failures')!=[]:
        raise RuntimeError('GPU receipt lacks a clean diagnostic-repeat verdict')
    devices=data.get('devices',[])
    if not devices or not all(physical(row)for row in devices):
        raise RuntimeError('GPU receipt requires initialized physical hardware, never software/paravirtual')
    high=data.get('primaryHigh',{})
    if high.get('passed')is not True or high.get('revision')!=revision or high.get('archiveSha256')!=archive or high.get('downloadSha256')!=download:
        raise RuntimeError('GPU High evidence is missing or mismatched')
    runs=high.get('runs',[])
    if len(runs)!=2 or any(row.get('exitCode')!=0 or row.get('runtimeErrors')or row.get('validationErrors')or
                           row.get('meshInputsValidated')is not True or not physical(row.get('device',{}))or
                           row.get('device',{}).get('driver')!='vulkan'for row in runs):
        raise RuntimeError('GPU High fresh-process execution failed')
    comparisons={row['name']:row for row in high.get('comparisons',[])}
    expected_names={f'run-{i}/{name}'for i in (0,1)for name in ('same-draw','fresh-target','control-dof','control-mirror')}|{'fresh-process'}
    if set(comparisons)!=expected_names or any((row.get('passed')is not True if '/control-'in name else row.get('exitCode')!=0)for name,row in comparisons.items()):
        raise RuntimeError('GPU High repeats/effect controls did not pass')
    return data


def inspect(directory,target,revision,archive,download,server,fingerprint):
    path=directory/receipt_name(target)
    if not path.is_file():raise RuntimeError('Missing mandatory GPU qualification receipt: '+target)
    data=inspect_receipt(json.loads(path.read_text()),target,revision,archive,download,server,fingerprint)
    cleanup_path=directory/CLEANUP_RECEIPT
    if not cleanup_path.is_file()or hashlib.sha256(cleanup_path.read_bytes()).hexdigest()!=data['cleanupSha256']:
        raise RuntimeError('GPU cleanup receipt is missing or differs from its bound hash')
    cleanup=json.loads(cleanup_path.read_text())
    checks=cleanup.get('checked',{})
    if (cleanup.get('schemaVersion')!=1 or cleanup.get('source')!=revision or cleanup.get('passed')is not True or
            cleanup.get('account')!=builder_namespace.ACCOUNT or cleanup.get('region')!=builder_namespace.REGION or
            not re.fullmatch(builder_namespace.RUN_PATTERN,cleanup.get('runId',''))or
            cleanup.get('runId')!=data.get('campaignRunId')or
            cleanup.get('leftovers')!=[]or cleanup.get('errors')!=[]or
            any(cleanup.get(key)is not True for key in ('protectedInventoryVerified','ephemeralRunnersRemoved','gpuVariablesDisarmed'))or
            not CLEANUP_TYPES<=set(checks)or any(checks[key].get('state')!='absent'for key in CLEANUP_TYPES)):
        raise RuntimeError('GPU cleanup is incomplete, mismatched, or retains owned resources')
    graphical=directory/('Blast-Circuit-'+target+'-graphical.json')
    if not graphical.is_file()or hashlib.sha256(graphical.read_bytes()).hexdigest()!=data.get('graphicalSha256'):
        raise RuntimeError('GPU graphical tour receipt differs from qualified evidence')
    return data


def inspect_run(run,repository,revision):
    if (run.get('head_repository',{}).get('full_name')!=repository or run.get('head_sha')!=revision or
            run.get('path')!='.github/workflows/gpu-qualification.yml'or run.get('head_branch')!='main'or
            run.get('event')not in ('workflow_run','workflow_dispatch')or run.get('status')!='completed'or run.get('conclusion')!='success'):
        raise RuntimeError('GPU qualification must use this exact trusted main SHA and successful owned workflow')
    return revision


def inspect_all(directory,revision,*,fingerprint=None,targets=None):
    import desktop_packages,download_formats,render_fingerprint
    fingerprint=fingerprint or render_fingerprint.calculate()['sha256']
    for target in (desktop_packages.TARGETS if targets is None else targets):
        item=desktop_packages.names(target)
        inspect(directory,target,revision,desktop_packages.digest(directory/item['archive']),
                desktop_packages.digest(directory/download_formats.FILES[target]),
                desktop_packages.digest(directory/'Blast-Circuit-server-dotnet10.zip'),fingerprint)


def inspect_graphical(graph,target,revision,release,archive,download,server):
    import desktop_packages,desktop_validation
    desktop_validation.inspect_receipt(graph,desktop_packages.TARGETS[target],revision,release)
    expected={'passed':True,'suite':'graphical-regression','target':target,'revision':revision,'release':release,
              'archiveSha256':archive,'downloadSha256':download,'serverSha256':server}
    if any(graph.get(key)!=value for key,value in expected.items()):
        raise RuntimeError('GPU graphical wrapper differs from the exact source/download/server identity')
    if any(not physical({'adapter':case.get('adapter'),'deviceType':case.get('adapterType')})for case in graph.get('cases',[])):
        raise RuntimeError('Graphical tour did not run on physical hardware')
    return graph


def diagnostic_repeat_failures(report,target):
    """Failed, crashed, missing or incomplete diagnostic repeats in the remaining phase's campaign-result.

    Run 41304300722 finished its phase with tour-09 repeat 4 killed (exit 127, no summary); the repeat step
    failed but the guest and this receipt still said finished/passed. Linux must carry every configured case."""
    import gpu_campaign
    expected={case:count for case,count,_ in gpu_campaign.LINUX_REPEATS}if target=='linux-x64'else{}
    repeats=report.get('diagnosticRepeats')or{}
    failures=[case+': repeat receipt missing'for case in expected if case not in repeats]
    for case,row in repeats.items():
        runs=row.get('runs')or[];need=expected.get(case,row.get('requested'))
        passed=sum(run.get('passed')is True for run in runs);crashed=sum(bool(run.get('crashed'))for run in runs)
        if row.get('error')or row.get('requested')!=need or len(runs)!=need or row.get('allPassed')is not True or passed!=need or crashed:
            failures.append(f"{case}: {passed}/{need} passed, {crashed} crashed, {len(runs)} ran"+(' ('+row['error']+')'if row.get('error')else''))
    return failures


def block_failed_repeats(directory,target,data,graphical,output):
    """Record a failed receipt (passed false, with reasons) and stop qualification if any diagnostic repeat failed."""
    remaining=directory/('gpu-campaign-'+target+'-remaining')/'campaign-result.json'
    report=json.loads(remaining.read_text())if remaining.is_file()else{}
    failures=diagnostic_repeat_failures(report,target)
    data['diagnosticRepeats']={'failures':failures,'cases':{case:{key:row.get(key)for key in('requested','completed','passed','crashed','failed','allPassed')}
                                                            for case,row in(report.get('diagnosticRepeats')or{}).items()}}
    if failures:
        data['passed']=False
        shutil.copy2(graphical,output/graphical.name)
        (output/receipt_name(target)).write_text(json.dumps(data,indent=2)+'\n')
        raise RuntimeError('Diagnostic repeats failed; GPU qualification is not passed: '+'; '.join(failures))


def stage(directory,target,revision,verification_run):
    import desktop_packages,download_formats,render_fingerprint,release_identity,desktop_validation
    core=directory/('gpu-campaign-'+target+'-core')
    report=json.loads((core/'campaign-result.json').read_text())
    high=json.loads((core/'high-vulkan/visual-parity-native.json').read_text())
    fingerprint=render_fingerprint.calculate()
    graphical=directory/('Blast-Circuit-'+target+'-graphical.json')
    graph=json.loads(graphical.read_text())
    item=desktop_packages.names(target)
    inspect_graphical(graph,target,revision,release_identity.check_exports(),desktop_packages.digest(directory/item['archive']),
                      desktop_packages.digest(directory/download_formats.FILES[target]),desktop_packages.digest(directory/'Blast-Circuit-server-dotnet10.zip'))
    if not report.get('capabilityPassed')or report.get('source')!=revision or report.get('target')!=target:
        raise RuntimeError('Original 2x resource capability evidence is missing')
    output=directory/('gpu-release-'+target);output.mkdir(exist_ok=False)
    data={'schemaVersion':1,'target':target,'source':revision,'verificationRunId':str(verification_run),
          'archiveSha256':desktop_packages.digest(directory/item['archive']),
          'downloadSha256':desktop_packages.digest(directory/download_formats.FILES[target]),
          'serverSha256':desktop_packages.digest(directory/'Blast-Circuit-server-dotnet10.zip'),
          'renderFingerprint':fingerprint['sha256'],'fingerprintManifest':fingerprint,'passed':True,
          'primaryDriver':'vulkan','effectiveSamples':2,'devices':[row['device']for row in high['runs']],
          'primaryHigh':high,'graphicalSha256':desktop_packages.digest(graphical),
          'scope':'Exact-source physical-GPU graphics evidence; visual user approval is separate'}
    data.update(cleanupAudited=False,campaignRunId=os.environ.get('BLAST_GPU_RUN_ID',''))
    data['scope']='Preliminary exact-source game graphics evidence; promotion rejects it until the controller binds successful cleanup.'
    block_failed_repeats(directory,target,data,graphical,output)
    inspect_receipt(data,target,revision,data['archiveSha256'],data['downloadSha256'],data['serverSha256'],fingerprint['sha256'],require_cleanup=False)
    shutil.copy2(graphical,output/graphical.name)
    (output/receipt_name(target)).write_text(json.dumps(data,indent=2)+'\n')
    return output


def download(repository,run_id,revision,directory,gh):
    if not run_id or not str(run_id).isdigit():raise RuntimeError('Promotion requires an explicit GPU qualification run ID')
    run=json.loads(gh('api',f'repos/{repository}/actions/runs/{run_id}'));inspect_run(run,repository,revision)
    for target in ('windows-x64','linux-x64'):
        with tempfile.TemporaryDirectory(prefix='blast-gpu-artifact-') as temporary:
            extracted=Path(temporary)
            gh('run','download',str(run_id),'--repo',repository,'--name',artifact_name(target),'--dir',str(extracted))
            for source in sorted(extracted.rglob('*')):
                if source.is_symlink():raise RuntimeError('GPU artifact contains a symbolic link')
                if source.is_dir():continue
                destination=directory/source.relative_to(extracted)
                if destination.exists():
                    if not destination.is_file() or destination.read_bytes()!=source.read_bytes():
                        raise RuntimeError('conflicting GPU artifact file: '+str(source.relative_to(extracted)))
                else:
                    destination.parent.mkdir(parents=True,exist_ok=True)
                    shutil.copy2(source,destination)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--directory',type=Path,default=Path('artifacts'))
    parser.add_argument('--target',choices=('windows-x64','linux-x64'),required=True)
    parser.add_argument('--revision',required=True);parser.add_argument('--verification-run',required=True)
    args=parser.parse_args();print(stage(args.directory.resolve(),args.target,args.revision,args.verification_run))

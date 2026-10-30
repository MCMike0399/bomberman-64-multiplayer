#!/usr/bin/env python3
"""Bounded GPU collection in two stages; raw technical verdicts remain separate."""
import argparse,datetime,hashlib,json,os,platform,re,subprocess,sys,time
from pathlib import Path
import crossplay_runtime,download_formats
import visual_parity

ROOT=Path(__file__).resolve().parents[2]

OTHER_STAGES=('sunken-temple','rock-garden','top-rules','blizzard-battle','greedy-trap','circuit-garden')

def collect_stage_captures(directory,output,target,revision,provenance,expiry,driver='vulkan',expected_adapter='T4',processes=2):
    """Retain the other six original-High scenes; core owns Up & Down's effect controls."""
    output.mkdir(exist_ok=False)
    if processes not in (1,2):raise ValueError('Stage collection supports one or two explicitly recorded processes')
    report={'source':revision,'driver':driver,'expectedAdapter':expected_adapter,'processesPerStage':processes,'freshProcessCoverage':processes==2,'scope':'Original High all-stage images and strict repeats; cross-GPU/effect review remains separate',
            'collectionComplete':False,'repeatsExact':False,'runs':[],'comparisons':[],'errors':[]}
    executable=None
    try:
        if expiry-time.time()<23*60:raise RuntimeError('Insufficient fixed-lifetime stage-capture headroom')
        executable=crossplay_runtime.install(directory,target,output)
        for stage in OTHER_STAGES:
            for index in range(processes):
                if expiry-time.time()<23*60:raise RuntimeError('Stage collection partial at fixed cleanup boundary')
                case=output/stage/f'run-{index}';case.mkdir(parents=True)
                command=[str(executable),*visual_parity.renderer_arguments(target,driver),'--windowed','--max-fps','30','--fixed-fps','30','--disable-vsync','--',
                         '--display-windowed','--playtest=visual-parity','--playtest-output='+str(case),'--parity-stage='+stage,
                         '--parity-provenance='+str(provenance)]
                row={'stage':stage,'index':index,'command':command,'launchPath':'ordinary-launcher-default'if target=='windows-x64'and driver=='vulkan'else'explicit-api','passed':False};report['runs'].append(row)
                with (case/'runtime.log').open('w')as log:
                    result=subprocess.run(command,cwd=case,stdout=log,stderr=subprocess.STDOUT,timeout=180)
                summary=json.loads((case/'summary.json').read_text());text=(case/'runtime.log').read_text(errors='replace')
                errors=crossplay_runtime.validate_summary(summary,case,'visual-parity',False)
                device=json.loads((case/'visual-parity-observations.json').read_text())
                runtime_errors=[s for s in text.splitlines()if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|Unhandled exception|FATAL:)',s)]
                row.update(exitCode=result.returncode,assertions=summary.get('assertions'),device=device,validationErrors=errors,
                           runtimeErrors=runtime_errors,runtimeWarnings=[s for s in text.splitlines()if 'WARNING:'in s])
                row['passed']=not(result.returncode or errors or runtime_errors)and summary['release']['revision']==revision and device['driver']==driver and visual_parity.hardware_adapter_matches(device,expected_adapter)
                if not row['passed']:raise RuntimeError('Stage capture execution/device failed: '+stage)
                for name,right in [('same-draw','parity-base-a-1.png'),('fresh-target','parity-base-b-0.png')]:
                    _,code=visual_parity.pair(case/'parity-base-a-0.png',case/right,case/name)
                    report['comparisons'].append({'stage':stage,'index':index,'name':name,'exitCode':code})
            if processes==2:
                _,code=visual_parity.pair(output/stage/'run-0/parity-base-a-0.png',output/stage/'run-1/parity-base-a-0.png',output/stage/'fresh-process')
                report['comparisons'].append({'stage':stage,'name':'fresh-process','exitCode':code})
        report['collectionComplete']=True
        report['repeatsExact']=all(row['exitCode']==0 for row in report['comparisons'])
    except (OSError,ValueError,RuntimeError,KeyError,TypeError,subprocess.SubprocessError)as error:report['errors'].append(str(error))
    finally:
        if target=='windows-x64'and executable is not None and(executable.parent/'unins000.exe').is_file():
            try:subprocess.run([str(executable.parent/'unins000.exe'),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART'],check=True,timeout=120)
            except subprocess.SubprocessError as error:report['collectionComplete']=False;report['errors'].append('Owned uninstall failed: '+str(error))
        (output/'stage-captures.json').write_text(json.dumps(report,indent=2)+'\n')
    return report

def run(args):
    target=args.target;output=ROOT/'artifacts'/('gpu-campaign-'+target+'-'+args.phase);output.mkdir(exist_ok=False)
    expiry=datetime.datetime.fromisoformat(args.expiry.replace('Z','+00:00')).timestamp()
    report={'scope':'Authorized diagnostic collection, not release approval','passed':False,'collectionComplete':False,
            'source':args.expected_revision,'target':target,'phase':args.phase,'os':platform.platform(),
            'runnerSource':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
            'expiryUtc':args.expiry,'steps':[],'errors':[]}
    def step(name,command,limit,diagnostic=False):
        if expiry-time.time()<20*60+limit:raise RuntimeError('Insufficient fixed-lifetime headroom for '+name)
        record={'name':name,'command':command,'completed':False};report['steps'].append(record);start=time.monotonic()
        with (output/(name+'.log')).open('w')as log:
            result=subprocess.run(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,timeout=limit)
        record.update(exitCode=result.returncode,seconds=round(time.monotonic()-start,3),completed=True)
        if result.returncode and not diagnostic:raise RuntimeError('Failed execution phase '+name)
        return result.returncode
    try:
        data=crossplay_runtime.metadata(ROOT/'artifacts',target)
        if data['revision']!=args.expected_revision:raise ValueError('Wrong frozen source')
        report['download']=download_formats.inspect(ROOT/'artifacts',target,data['revision'],data['release'])
        if args.phase=='core':
            report['capabilityPassed']=False
            drivers=['vulkan','d3d12']if target=='windows-x64'else['vulkan']
            for driver in drivers:
                depth=output/('depth-'+driver)
                step('small-resource-'+driver,[sys.executable,'tools/qa/depth_probe.py','--godot',str(args.godot),'--driver',driver,'--output',str(depth)],180)
                result=json.loads((depth/'depth-probe.json').read_text());original=next(c for c in result['cases']if c['name']=='msaa-2x')
                device=original['metadata']['device'];report.setdefault('devices',[]).append(original['metadata'])
                if 't4'not in device.lower()or any(x in device.lower()for x in ('warp','basic render','llvmpipe','lavapipe','swiftshader')):
                    raise ValueError('Capability failed: selected hardware T4 is absent')
                for plane in original['planeCenters']:
                    if any(abs(plane[key]-plane['expectedReverseOrthoDepth'])>1e-6 for key in ('rawSample0','rawSample1','resolved')):
                        raise ValueError('Capability failed: original2x known-plane depth is invalid')
                clears=next(c for c in result['cases']if c['name']=='msaa-2x-clear-attachments')['customClears']
                for item in clears.values():
                    for key in ('minimumRaw0Raw1Resolved','maximumRaw0Raw1Resolved'):
                        if item['bufferSummary'][key][:2]!=[.75,.75]:raise ValueError('Capability failed: original2x sample clear is invalid')
            report['capabilityPassed']=True
            if target=='windows-x64':
                health=[]
                for driver in drivers:
                    destination=output/('health-'+driver)
                    command=[sys.executable,'tools/ci/gpu_health.py','--expected-revision',args.expected_revision,'--driver',driver,'--output',str(destination)]
                    if driver=='vulkan':command+=['--default-driver']
                    step('game-health-'+driver,command,240)
                    health.append(json.loads((destination/'gpu-health.json').read_text()))
                # D3D12 reports 'NVIDIA Tesla T4'; Vulkan reports 'Tesla T4'.
                # Both game receipts are bound to this one-GPU instance and exact executable.
                if any('t4' not in row['health']['adapter'].lower()for row in health):raise ValueError('Windows API control did not use the selected T4')
                report['actualGameHealth']=health
            technical=[]
            for driver in drivers:
                destination=output/('high-'+driver)
                command=[sys.executable,'tools/ci/visual_parity.py','--target',target,'--expected-revision',args.expected_revision,'--expected-adapter','T4','--output',str(destination)]
                if target=='windows-x64'and driver=='d3d12':command+=['--diagnostic-windows-driver',driver]
                step('original-high-'+driver,command,900,diagnostic=True)
                high=json.loads((destination/'visual-parity-native.json').read_text())
                if len(high['runs'])!=2 or any(r['exitCode']or r['validationErrors']or r['runtimeErrors']for r in high['runs']):
                    raise RuntimeError('Original High capture execution failed; raw repeat verdict is separate')
                technical.append(high['passed'])
            stages=collect_stage_captures(ROOT/'artifacts',output/'all-stages',target,args.expected_revision,
                                          output/'high-vulkan/provenance.json',expiry)
            report['allStages']=stages
            if not stages['collectionComplete']:raise RuntimeError('All-stage captures are incomplete; retain partial evidence')
            technical.append(stages['repeatsExact'])
            report['collectionComplete']=True;report['passed']=all(technical)
            report['technicalVerdict']='pass'if report['passed']else'failed strict repeat/control; complete raw captures retained'
            files=[{'path':str(p.relative_to(output)),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}for p in sorted(output.rglob('*'))if p.is_file()and p.suffix in('.png','.json','.bin','.gz','.log')]
            (output/'core-capture-files.json').write_text(json.dumps({'source':args.expected_revision,'files':files},indent=2)+'\n')
        else:
            if target=='windows-x64' and not args.skip_bisection:
                for control in ('ssil','ssao','glow','shadows','msaa'):
                    step('bisect-'+control,[sys.executable,'tools/ci/frozen_effect_control.py','--target',target,'--expected-revision',args.expected_revision,'--driver','d3d12','--control',control,'--output',str(output/('bisect-'+control))],600,diagnostic=True)
            if target=='windows-x64' and args.skip_bisection:
                report['bisectionNotRunReason']='User-directed: Vulkan repeats exactly; use remaining time for tours, no product API change'
            step('actual-package-replay',[sys.executable,'tools/ci/authority_replay.py','run','--target',target,'--expected-revision',args.expected_revision,'--output',str(output/'replay')],300,diagnostic=True)
            if target=='linux-x64':step('saved-startups',[sys.executable,'tools/packaging/startup-desktop.py','--target',target],600,diagnostic=True)
            # Tours are last. A headroom failure is explicit partial coverage, never a pass.
            step('complete-native-graphical',[sys.executable,'tools/packaging/verify-desktop.py','--target',target,'--graphical','--disposable-desktop',*(['--default-renderer']if target=='windows-x64'else[])],1800,diagnostic=True)
            report['collectionComplete']=True;report['passed']=all(r['exitCode']==0 for r in report['steps'])
    except (OSError,ValueError,KeyError,RuntimeError,subprocess.SubprocessError)as error:report['errors'].append(str(error))
    finally:(output/'campaign-result.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({'target':target,'phase':args.phase,'collectionComplete':report['collectionComplete'],'technicalPassed':report['passed'],'errors':report['errors']}))
    return 0 if report['collectionComplete']else 1

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--target',choices=('windows-x64','linux-x64'),required=True)
    parser.add_argument('--expected-revision',required=True);parser.add_argument('--expiry',required=True);parser.add_argument('--godot',type=Path,required=True)
    parser.add_argument('--phase',choices=('core','remaining'),required=True)
    parser.add_argument('--skip-bisection',action='store_true')
    raise SystemExit(run(parser.parse_args()))

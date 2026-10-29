#!/usr/bin/env python3
"""One authorized hardware campaign; collection success never substitutes for phase verdicts."""
import argparse,datetime,hashlib,json,os,platform,subprocess,sys,time
from pathlib import Path
import desktop_packages as desktop
import crossplay_runtime,download_formats

ROOT=Path(__file__).resolve().parents[2]

def run(args):
    target=args.target;output=ROOT/'artifacts'/('gpu-campaign-'+target);output.mkdir(exist_ok=False)
    expiry=datetime.datetime.fromisoformat(args.expiry.replace('Z','+00:00')).timestamp()
    report={'mode':'authorized-one-time-gpu-diagnostic','passed':False,'source':args.expected_revision,'target':target,
            'os':platform.platform(),'runnerSource':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
            'expiryUtc':args.expiry,'phases':[],'errors':[]}
    def step(name,command,limit):
        if expiry-time.time()<20*60+limit:raise RuntimeError('Insufficient fixed-lifetime headroom for '+name+'; test incomplete, not skipped/pass')
        record={'name':name,'command':command,'passed':False};report['phases'].append(record);start=time.monotonic()
        with (output/(name+'.log')).open('w')as log:
            process=subprocess.run(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,timeout=limit)
        record.update(exitCode=process.returncode,seconds=round(time.monotonic()-start,3));record['passed']=process.returncode==0
        if process.returncode:raise RuntimeError('Failed phase '+name)
    try:
        data=crossplay_runtime.metadata(ROOT/'artifacts',target)
        if data['revision']!=args.expected_revision:raise ValueError('Wrong frozen source')
        download=download_formats.inspect(ROOT/'artifacts',target,data['revision'],data['release'])
        report['download']=download
        depth=output/'depth'
        step('small-resource-control',[sys.executable,'tools/qa/depth_probe.py','--godot',str(args.godot),'--driver',crossplay_runtime.SYSTEMS[target][2],'--output',str(depth)],180)
        result=json.loads((depth/'depth-probe.json').read_text());original=next(c for c in result['cases']if c['name']=='msaa-2x')
        device=original['metadata']['device'];report['device']=original['metadata']
        if 't4'not in device.lower()or any(x in device.lower()for x in ('warp','basic render','llvmpipe','lavapipe','swiftshader')):raise ValueError('Resource control did not initialize selected T4 graphics hardware')
        for plane in original['planeCenters']:
            if any(abs(plane[key]-plane['expectedReverseOrthoDepth'])>1e-6 for key in ('rawSample0','rawSample1','resolved')):raise ValueError('Original2x known-plane depth is invalid')
        clears=next(c for c in result['cases']if c['name']=='msaa-2x-clear-attachments')['customClears']
        for item in clears.values():
            for key in ('minimumRaw0Raw1Resolved','maximumRaw0Raw1Resolved'):
                if item['bufferSummary'][key][:2]!=[.75,.75]:raise ValueError('Original2x attachment sample clear is invalid')
        step('original-full-high',[sys.executable,'tools/ci/visual_parity.py','--target',target,'--expected-revision',args.expected_revision,'--expected-adapter','T4','--output',str(output/'high')],900)
        step('actual-package-replay',[sys.executable,'tools/ci/authority_replay.py','run','--target',target,'--expected-revision',args.expected_revision,'--output',str(output/'replay')],300)
        step('saved-startups',[sys.executable,'tools/packaging/startup-desktop.py','--target',target],600)
        step('unpack-owned-update-fixture',[sys.executable,'tools/packaging/package-update-fixture.py','--target',target,'--unpack'],120)
        if target=='linux-x64':step('fixture-native-download',[sys.executable,'tools/packaging/package-download.py','--target',target,'--directory','artifacts/update-fixture'],180)
        step('update-and-rollback',[sys.executable,'tools/packaging/verify-desktop-update.py','--target',target],900)
        step('complete-native-headless',[sys.executable,'tools/packaging/verify-desktop.py','--target',target,'--disposable-desktop'],600)
        step('complete-native-graphical',[sys.executable,'tools/packaging/verify-desktop.py','--target',target,'--graphical','--disposable-desktop'],1800)
        report['passed']=True
    except (OSError,ValueError,KeyError,RuntimeError,subprocess.SubprocessError)as error:report['errors'].append(str(error))
    finally:(output/'campaign-result.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({'target':target,'passed':report['passed'],'errors':report['errors']}));return 0 if report['passed']else 1

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--target',choices=('windows-x64','linux-x64'),required=True)
    parser.add_argument('--expected-revision',required=True);parser.add_argument('--expiry',required=True);parser.add_argument('--godot',type=Path,required=True)
    raise SystemExit(run(parser.parse_args()))

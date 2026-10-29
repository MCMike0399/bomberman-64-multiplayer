"""Actual installed Windows first-frame crash/recovery proof; never GPU qualification."""
import argparse,json,os,re,subprocess,sys,time
from pathlib import Path
import crossplay,crossplay_runtime,desktop_packages as desktop
from desktop_rendering import report_matches


def run(directory,output,revision):
    if os.name!='nt' or os.environ.get('GITHUB_ACTIONS')!='true':raise RuntimeError('Use the owned native Windows CI profile')
    output.mkdir(parents=True,exist_ok=False);data=crossplay_runtime.metadata(directory,'windows-x64')
    if data['revision']!=revision:raise ValueError('Wrong packaged source')
    report={'schemaVersion':1,'scope':'Actual installer startup crash/recovery; functional only','graphicsQualified':False,'source':revision,'passed':False,'runs':[],'errors':[]}
    executable=None
    try:
        executable=crossplay_runtime.install(directory,'windows-x64',output)
        report['archiveSha256']=desktop.digest(directory/'Blast-Circuit-windows-x64.zip');report['executableSha256']=desktop.digest(executable)
        state=output/'owned-launcher-state';prefs=output/'preferences.cfg';prefs.write_text('[player]\nautomaticUpdates=false\n[video]\ngraphicsPreset="low"\nfullscreen=false\n')
        for index in range(3):
            case=output/f'run-{index}';command=[str(executable),'--windowed','--','--smoke-menu','--smoke-seconds=4','--display-windowed','--smoke-output='+str(case),'--smoke-preferences='+str(prefs),'--launcher-state-dir='+str(state),'--qa-vulkan-first-frame-crash']
            process=crossplay.launch(command,case,cwd=case)
            try:code=process.wait(timeout=180)
            except subprocess.TimeoutExpired:crossplay.stop_launcher(case);raise
            finally:crossplay.stop_child(process)
            (case/'launcher.log').rename(case/'runtime.log');text=(case/'runtime.log').read_text(errors='replace');errors=[s for s in text.splitlines()if re.search(r'(^|\s)(ERROR:|SCRIPT ERROR:|FATAL:|Unhandled exception)',s)]
            row={'index':index,'command':command,'exitCode':code,'runtimeErrors':errors,'runtimeWarnings':[s for s in text.splitlines()if 'WARNING:'in s]}
            if index==0:
                row['expectedCrashObserved']='BLAST_QA_FIRST_FRAME_CRASH'in text
                row['pendingMarkers']=[json.loads(p.read_text())for p in state.glob('boot-*.json')]
                (case/'pending-launch-markers.json').write_text(json.dumps(row['pendingMarkers'],indent=2)+'\n')
                if code==0 or not row['expectedCrashObserved']or not list(state.glob('boot-*.json')):raise RuntimeError('Actual first-frame crash/marker was not demonstrated')
            else:
                renderer=json.loads((case/'renderer-selection.json').read_text());shutdown=json.loads((case/'shutdown.json').read_text());row.update(renderer=renderer,shutdown=shutdown)
                if code or errors or not report_matches('windows-x64',renderer,'driver') or renderer.get('driver')!='d3d12' or renderer.get('fallbackReason')!='previous-vulkan-startup-incomplete' or renderer.get('sourceRevision')!=revision or renderer.get('method')!='forward_plus' or not shutdown.get('savedStateFlushed')or not shutdown.get('drain',{}).get('completed')or not(case/'menu.png').is_file()or 'GODOT_SMOKE_OK'not in text:
                    raise RuntimeError('Recovered/remembered actual DirectX startup failed')
            report['runs'].append(row)
        report['passed']=True
    except (OSError,ValueError,RuntimeError,KeyError,subprocess.SubprocessError)as e:report['errors'].append(str(e))
    finally:
        if executable is not None and(executable.parent/'unins000.exe').exists():
            result=subprocess.run([str(executable.parent/'unins000.exe'),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART'],timeout=120)
            if result.returncode:report['passed']=False;report['errors'].append('Owned uninstall failed')
        (output/'startup-guard.json').write_text(json.dumps(report,indent=2)+'\n')
        (directory/'Blast-Circuit-windows-x64-startup-guard.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({'passed':report['passed'],'errors':report['errors']}));return 0 if report['passed']else 1

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--directory',type=Path,default=Path('artifacts'));parser.add_argument('--output',type=Path,required=True);parser.add_argument('--expected-revision',required=True)
    args=parser.parse_args();raise SystemExit(run(args.directory.resolve(),args.output.resolve(),args.expected_revision))

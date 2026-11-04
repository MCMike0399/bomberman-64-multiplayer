"""Fixed nonqualifying comparison of two frozen packages; no product rebuild or retries."""
import hashlib,importlib.util,json,os,platform,subprocess,sys,tempfile,zipfile
from pathlib import Path
import crossplay,desktop_packages as desktop,download_formats as downloads
from promote_release import gh,inspect_server
ROOT=Path(__file__).resolve().parents[2]
REPO='MCMike0399/bomberman-64-multiplayer'
PAYLOADS={'accepted':(41747779337,'818dc63edccc791aa01a03a12583783fb844ef9c'),
          'candidate':(41788411766,'68002f2c28314d2ed91301cf37088bea1d408d1a')}
RELEASE={'version':'0.45.1','build':'57','protocolRevision':6,'admissionToken':'6/0.45.1/57'}
OUT=ROOT/'artifacts/peer-order-observation';OUT.mkdir(parents=True,exist_ok=False)
def api(path):return json.loads(gh('api',f'repos/{REPO}/{path}'))
def fetch(label,run,revision):
 record=api(f'actions/runs/{run}');assert record['head_sha']==revision and record['head_branch']=='main'
 items=api(f'actions/runs/{run}/artifacts?per_page=100')['artifacts'];directory=ROOT/'artifacts'/label;directory.mkdir()
 verified=[]
 for name in ['Blast-Circuit-windows-x64','Blast-Circuit-windows-x64-download','Blast-Circuit-server-dotnet10']:
  found=[x for x in items if x['name']==name and not x['expired']];assert len(found)==1
  item=found[0];archive=directory/(name+'.artifact.zip')
  with archive.open('wb') as stream:subprocess.run(['gh','api',f'repos/{REPO}/actions/artifacts/{item["id"]}/zip'],stdout=stream,stderr=subprocess.PIPE,check=True,timeout=900)
  actual=desktop.digest(archive);assert 'sha256:'+actual==item['digest']
  with zipfile.ZipFile(archive) as bundle:
   assert all((directory/x.filename).resolve().is_relative_to(directory.resolve()) for x in bundle.infolist())
   bundle.extractall(directory)
  verified.append({'name':name,'id':item['id'],'zipSha256':actual})
 desktop.inspect_archive(directory,'windows-x64',revision,RELEASE);downloads.inspect(directory,'windows-x64',revision,RELEASE)
 inspect_server(directory,revision,'0.45.1',release=RELEASE)
 metadata=json.loads((directory/'Blast-Circuit-windows-x64-metadata.json').read_text())
 proof={'run':run,'revision':revision,'artifacts':verified,'files':metadata['files'],'installerSha256':desktop.digest(directory/downloads.FILES['windows-x64'])}
 (OUT/(label+'-payload.json')).write_text(json.dumps(proof,indent=2)+'\n');return directory,metadata
assert platform.system()=='Windows' and os.environ.get('GITHUB_ACTIONS')=='true'
prepared={label:fetch(label,*value) for label,value in PAYLOADS.items()}
# Downloads are finished. Every later child (installer, server, game and cleanup)
# inherits the same credential-free environment, including Suite.server().
runtime_environment=crossplay.clean_environment()
os.environ.clear();os.environ.update(runtime_environment)
spec=importlib.util.spec_from_file_location('owned_qa',ROOT/'tools/qa/run.py');qa=importlib.util.module_from_spec(spec);spec.loader.exec_module(qa)
receipt={'schemaVersion':1,'nonqualifying':True,'measurementComplete':False,'connectionDeadlineSeconds':5,
         'normalCatalogTimeoutUnchanged':True,'automaticRetries':False,'controllerSource':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
         'os':platform.platform(),'imageVersion':os.environ.get('ImageVersion'),'order':['accepted','candidate']*5,'cells':[]}
for index,label in enumerate(receipt['order'],1):
 directory,metadata=prepared[label];revision=PAYLOADS[label][1]
 cell=OUT/f'{index:02d}-{label}';cell.mkdir();row={'index':index,'payload':label,'revision':revision,'scenarios':[]};receipt['cells'].append(row)
 with tempfile.TemporaryDirectory(prefix='blast-peer-order-') as temporary:
  work=Path(temporary);installed=work/'Installed game with spaces';server=work/'server'
  with zipfile.ZipFile(directory/'Blast-Circuit-server-dotnet10.zip') as z:z.extractall(server)
  subprocess.run([str(directory/downloads.FILES['windows-x64']),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/DIR='+str(installed)],check=True,timeout=180)
  try:
   assert all(desktop.digest(installed/name)==expected for name,expected in metadata['files'].items())
   row['executedInventoryVerified']=True
   suite=qa.Suite('peer-order-observation',cell,'unused',desktop='shared')
   with suite.server(dll=server/'Blast-Circuit-server-dotnet10/Bomber.Server.dll') as origin:
    # Match the original cold-process predecessor before the isolated ICE pair.
    for scenario in ['core-headless','peer-ice-order']:
     destination=cell/scenario;destination.mkdir()
     command=[sys.executable,str(ROOT/'tools/playtest/run.py'),'--executable',str(installed/'BlastCircuit.exe'),'--scenario',scenario,'--output',str(destination),'--desktop','shared','--server',origin,'--headless']
     result=subprocess.run(command,cwd=ROOT,env=dict(crossplay.clean_environment(),BLAST_QA_PEER_JOIN_ORDER='normal'),capture_output=True,text=True,timeout=1800)
     (destination/'controller.log').write_text(result.stdout+result.stderr)
     folders=[p for p in destination.iterdir() if p.is_dir() and (p/'runner-summary.json').exists()];assert len(folders)==1
     folder=folders[0];runner=json.loads((folder/'runner-summary.json').read_text());report=json.loads((folder/'summary.json').read_text())
     assert report['release']==dict(RELEASE,revision=revision)
     row['scenarios'].append({'scenario':scenario,'exitCode':result.returncode,'passed':runner['passed'],'runtimeErrors':runner.get('runtimeErrors'),'seconds':runner.get('seconds'),'assertions':report.get('assertions'),'folder':str(folder.relative_to(OUT))})
  finally:subprocess.run([str(installed/'unins000.exe'),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART'],check=True,timeout=180)
 (OUT/'measurement.json').write_text(json.dumps(receipt,indent=2)+'\n')
receipt.update(measurementComplete=True,allOriginalCasesPassed=all(s['passed'] for c in receipt['cells'] for s in c['scenarios']))
(OUT/'measurement.json').write_text(json.dumps(receipt,indent=2)+'\n')
print(json.dumps({'measurementComplete':True,'nonqualifying':True,'cells':10,'allOriginalCasesPassed':receipt['allOriginalCasesPassed']}))

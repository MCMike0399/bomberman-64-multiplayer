"""Run mocked phase wiring plus noncreating live AWS checks, with no native qualification claim."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

import builder_arming
import builder_campaign
import builder_controller
import builder_preflight

ROOT=Path(__file__).resolve().parents[2]


def run(state,output,require_prepared=False,operator=False):
    state=Path(state).resolve();output=Path(output).resolve();output.mkdir(parents=True,exist_ok=False)
    builder_campaign.configure(state);builder_arming.configure(state)
    plan=json.loads(builder_campaign.PLAN.read_text())
    def sources():return {path.name:hashlib.sha256(path.read_bytes()).hexdigest()for path in sorted((ROOT/'tools/ci').glob('*.py'))}
    source_hashes=sources()
    if require_prepared:
        builder_controller.verify_context(plan,ROOT,operator)
        builder_arming.verify_prepared(plan)
    actual=os.environ.get('BUILDER_REAL_AWS')or shutil.which('aws')
    if not actual:raise ValueError('AWS CLI must exist before rehearsal')
    with tempfile.TemporaryDirectory(prefix='builder-readonly-')as temporary:
        guard=Path(temporary)/'aws';source=Path(__file__).with_name('builder_dry_aws.py')
        # Import resolution remains pinned to our own checked source directory.
        guard.write_text('#!'+sys.executable+'\nimport runpy,sys\nsys.path.insert(0,'+repr(str(source.parent))+')\nrunpy.run_path('+repr(str(source))+',run_name="__main__")\n')
        guard.chmod(0o700)
        environment={**os.environ,'PATH':str(guard.parent)+os.pathsep+os.environ.get('PATH',''),
                     'BUILDER_REAL_AWS':str(Path(actual).resolve()),'BUILDER_DRY_RUN':'true'}
        log=output/'mocked-phase-wiring.log'
        with log.open('w')as stream:
            tests=subprocess.run([sys.executable,'-m','unittest','discover','-s','tools/ci','-p','test_builder_*.py'],
                                 cwd=ROOT,env=environment,stdout=stream,stderr=subprocess.STDOUT,timeout=180)
        # This subprocess inherits the CLI fence, so even an accidental new
        # mutating call inside preflight fails before reaching AWS.
        command=[sys.executable,'tools/ci/builder_preflight.py','--state',str(state),'--output',str(output/'live-preflight.json')]
        live=subprocess.run(command,cwd=ROOT,env=environment,capture_output=True,text=True,timeout=300)
        (output/'live-preflight.log').write_text(live.stdout+live.stderr)
    preflight=json.loads((output/'live-preflight.json').read_text())if(output/'live-preflight.json').exists()else{}
    import builder_namespace as names
    role_verified=preflight.get('caller','').startswith(f'arn:aws:sts::{names.ACCOUNT}:assumed-role/{names.CI_ROLE}/')
    unchanged=source_hashes==sources()
    report={'schemaVersion':1,'passed':tests.returncode==0 and live.returncode==0 and preflight.get('passed')is True and unchanged and(operator or role_verified),
            'source':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
            'requestedPayloadSource':plan['payloadSource'],'runId':plan['runId'],'account':plan['account'],'region':plan['region'],
            'createdResources':[],'deletedResources':[],'mutatingCliOperationsAllowed':False,
            'preparedInputsValidated':require_prepared,'mockedPhaseExitCode':tests.returncode,'liveReadExitCode':live.returncode,
            'caller':preflight.get('caller'),'hardwareQualified':False,'timestampUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
            'sourceTreeDirty':bool(subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip()),
            'sourceFilesUnchangedDuringRehearsal':unchanged,'sourceFiles':source_hashes,
            'actualScopedRoleVerified':role_verified,'operatorMode':operator,
            'scope':'Mocked coordinator/cleanup/game-contract wiring plus live read-only identity/network/quota/image/discovery checks; no actual guest, game or GPU proof.'}
    (output/'rehearsal.json').write_text(json.dumps(report,indent=2)+'\n')
    return report


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--state',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True);parser.add_argument('--require-prepared',action='store_true')
    parser.add_argument('--operator',action='store_true');args=parser.parse_args()
    result=run(args.state,args.output,args.require_prepared,args.operator)
    print(json.dumps(result));raise SystemExit(0 if result['passed']else 1)

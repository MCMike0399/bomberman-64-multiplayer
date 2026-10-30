"""Hosted phase coordinator. Each phase is resumable evidence, never a new allocation retry."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import threading
import time

import builder_arming as arming
import builder_campaign as campaign
import builder_cleanup as cleanup
import builder_journal as journal
import builder_namespace as names
import builder_preflight as preflight
import builder_s3
import builder_ssm
import builder_supervisor as supervisor
import builder_transfer as transfer

TARGETS={'linux-x64':'Linux','windows-x64':'Windows'}


def write(path,value):
    temporary=path.with_suffix(path.suffix+'.pending')
    temporary.write_text(json.dumps(value,indent=2)+'\n');temporary.replace(path)


def verify_context(plan,root,operator=False):
    if not operator and (os.environ.get('GITHUB_ACTIONS')!='true'or os.environ.get('GITHUB_REF')!='refs/heads/main'or
                         os.environ.get('GITHUB_REPOSITORY')!='MCMike0399/bomberman-64-multiplayer'):
        raise ValueError('Hosted allocation requires the approved main workflow')
    actual=subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()
    if actual!=plan['payloadSource']:raise ValueError('Controller checkout differs from frozen source')
    if subprocess.check_output(['git','status','--porcelain'],cwd=root,text=True).strip():
        raise ValueError('Actual hosted/operator qualification requires a clean source checkout')
    def gh(path):return json.loads(subprocess.check_output(['gh','api','repos/MCMike0399/bomberman-64-multiplayer/'+path],text=True,timeout=90))
    current=gh('git/ref/heads/main')['object']['sha']
    run=gh('actions/runs/'+str(plan['verificationRunId']))
    if (current!=actual or run['head_sha']!=actual or run['head_branch']!='main'or run['path']!='.github/workflows/ci.yml'or
            run['event']not in ('push','workflow_dispatch')or run['status']!='completed'or run['conclusion']!='success'or
            run['head_repository']['full_name']!='MCMike0399/bomberman-64-multiplayer'):
        raise ValueError('Only successful current-main Verification can supply final GPU bytes')
    return {'source':actual,'verificationRunId':str(run['id']),'mode':'operator'if operator else'hosted'}


class Controller:
    def __init__(self,state,operator=False):
        self.root=Path(state).resolve();campaign.configure(self.root);cleanup.configure(self.root);arming.configure(self.root)
        self.plan=json.loads(campaign.PLAN.read_text());self.operator=operator
        self.expiry=datetime.datetime.fromisoformat(self.plan['fixedCampaignExpiryUtc']).timestamp()
        self.stop=self.expiry-20*60
        self.api=lambda *args:campaign.aws(self.plan,*args)
        self.client=builder_s3.Objects(self.api,self.plan['runId'])
        path=self.root/'controller.json'
        self.data=json.loads(path.read_text())if path.exists()else{'schemaVersion':1,'runId':self.plan['runId'],
            'source':self.plan['payloadSource'],'targets':{},'errors':[]}
        if self.data['runId']!=self.plan['runId']or self.data['source']!=self.plan['payloadSource']:
            raise ValueError('Controller state belongs to another campaign')

    def save(self):write(self.root/'controller.json',self.data)

    def intent(self):
        verify_context(self.plan,Path(__file__).resolve().parents[2],self.operator)
        arming.verify_prepared(self.plan)
        result=preflight.inspect(self.plan,self.api);write(self.root/'preflight.json',result)
        if not result['passed']:raise ValueError('Noncreating preflight failed; no allocation allowed')
        self.check_actor()
        row=campaign.prepare_intent(self.plan)
        self.data['intentSha256']=journal.canonical_hash(row);self.data['phase']='intent-prepared';self.save()
        return row

    def check_actor(self):
        identity=self.api('sts','get-caller-identity')
        if identity.get('Account')!=names.ACCOUNT or not self.operator and not identity.get('Arn','').startswith(
                f'arn:aws:sts::{names.ACCOUNT}:assumed-role/{names.CI_ROLE}/'):
            raise ValueError('Actual hosted caller is not the scoped builder role')
        return identity

    def publish_intent(self,artifact_id,artifact_digest):
        import re
        if not str(artifact_id).isdigit()or int(artifact_id)<=0 or not re.fullmatch(r'[0-9a-f]{64}',artifact_digest):
            raise ValueError('Actual successful durable state upload outputs required')
        intents=[row for row in campaign.rows(self.plan)if row['event']=='INTENT'and row['type']=='AWS::CloudFormation::Stack']
        if len(intents)!=1:raise ValueError('One exact intent required')
        write(self.root/'intent-published.json',{'intentSha256':journal.canonical_hash(intents[0]),
              'controllerRunId':os.environ.get('GITHUB_RUN_ID'),'artifactId':str(artifact_id),'artifactDigest':artifact_digest})

    def launch(self):
        verify_context(self.plan,Path(__file__).resolve().parents[2],self.operator)
        self.check_actor()
        intents=[row for row in campaign.rows(self.plan)if row['event']=='INTENT'and row['type']=='AWS::CloudFormation::Stack']
        if len(intents)!=1:raise ValueError('Persist a creation intent before launching')
        if not self.operator:
            published=json.loads((self.root/'intent-published.json').read_text())
            if published['intentSha256']!=journal.canonical_hash(intents[0])or published['controllerRunId']!=os.environ.get('GITHUB_RUN_ID'):
                raise ValueError('Durable creation intent differs from the approved hosted run')
        sid=campaign.submit_intent(self.plan)
        campaign.wait_stack(self.plan,sid,'create',min(time.time()+900,self.stop),
                            observe=lambda:campaign.sync(self.plan,best_effort=True))
        campaign.sync(self.plan);arming.set_schedule_auto_delete(self.plan);ready=arming.verify_live(self.plan)
        staging=self.plan['staging']
        transfer.upload_inputs(self.client,self.plan,campaign.JOURNAL,staging['inputs'])
        transfer.publish_bootstrap(self.client,self.plan,campaign.JOURNAL,staging['manifest'],staging['bootstrap'],staging['awsCli'])
        logical={row['details'].get('logicalId'):row['id']for row in journal.effective_created(campaign.rows(self.plan)).values()}
        self.data['targets']={target:{'instanceId':logical[osname+'Instance'],'state':'booting'}for target,osname in TARGETS.items()}
        self.data.update(phase='bootstrapping',bootstrapDeadline=min(time.time()+600,self.stop));self.save()
        return ready

    def read_object(self,key,path):
        path.parent.mkdir(parents=True,exist_ok=True)
        try:self.client.call('get-object','--key',key,str(path))
        except subprocess.TimeoutExpired:return None
        except builder_s3.ApiError as error:
            if error.code in ('NoSuchKey','404','NotFound','SlowDown','RequestTimeout','UnverifiedError'):return None
            raise
        if path.stat().st_size>16*1024*1024:raise ValueError('Control receipt exceeds its size limit')
        return json.loads(path.read_text())

    def owned_target(self,target):
        sid=campaign.authorize(self.plan)
        row=self.data['targets'][target]
        owned=journal.effective_created(campaign.rows(self.plan)).get(('AWS::EC2::Instance',row['instanceId']))
        if not sid or not owned or owned['parent']!=sid or owned['details'].get('logicalId')!=TARGETS[target]+'Instance':
            raise ValueError('Target is not the exact journaled stack member')
        return row['instanceId']

    def stop_target(self,target):
        identity=self.owned_target(target)
        row=self.data['targets'][target]
        builder_ssm.verify_instance(self.plan,self.api,row['instanceId'],TARGETS[target])
        self.api('ec2','terminate-instances','--instance-ids',identity)
        row['terminationRequested']=True;self.save()

    def observe_bootstrap(self,target):
        row=self.data['targets'][target]
        receipt=self.read_object('results/'+target+'/bootstrap-ready.json',self.root/'observations'/target/'bootstrap-ready.json')
        if receipt is None:return False
        expected={'runId':self.plan['runId'],'source':self.plan['payloadSource'],'target':target,'instanceId':row['instanceId'],
                  'account':self.plan['account'],'region':self.plan['region'],
                  'payloadManifestSha256':self.plan['staging']['manifest']['sha256'],'ready':True,'hardwareQualified':False}
        expected['user']='BlastQa'if target=='windows-x64'else'ubuntu'
        if any(receipt.get(key)!=value for key,value in expected.items()):raise ValueError('Bootstrap receipt identity differs')
        if target=='windows-x64'and(type(receipt.get('sessionId'))is not int or receipt['sessionId']<=0):
            raise ValueError('Windows bootstrap did not observe an interactive session')
        agents=self.api('ssm','describe-instance-information','--filters',json.dumps([{'Key':'InstanceIds','Values':[row['instanceId']]}]))['InstanceInformationList']
        if not any(agent['InstanceId']==row['instanceId']and agent['PingStatus']=='Online'for agent in agents):return False
        self.owned_target(target)
        row['commandId']=builder_ssm.dispatch(self.plan,campaign.JOURNAL,self.api,row['instanceId'],TARGETS[target],self.plan['staging']['manifest']['sha256'])
        row['state']='running-core';self.save();return True

    def retain(self,target,manifest):
        transfer.validate_results(manifest,self.plan,target)
        if manifest.get('instanceId')!=self.data['targets'][target]['instanceId']:raise ValueError('Result producer instance differs')
        state=manifest['state'];destination=self.root/'evidence'/target/state
        if destination.exists():
            prior=json.loads((destination/'retention.json').read_text())
            if prior['manifestSha256']!=journal.canonical_hash(manifest):raise ValueError('A committed checkpoint changed')
            return prior
        receipt=transfer.retain_results(self.client,self.plan,target,manifest,destination)
        receipt['manifestSha256']=journal.canonical_hash(manifest)
        receipt['rawManifestSha256']=hashlib.sha256((self.root/'observations'/target/'manifest.json').read_bytes()).hexdigest()
        write(destination/'retention.json',receipt)
        return receipt

    def check_worker_exit(self,target):
        outcome=self.read_object('results/'+target+'/worker-exit.json',self.root/'observations'/target/'worker-exit.json')
        if outcome is None:return
        expected={'runId':self.plan['runId'],'source':self.plan['payloadSource'],'target':target,
                  'account':self.plan['account'],'region':self.plan['region'],'instanceId':self.data['targets'][target]['instanceId']}
        if any(outcome.get(key)!=value for key,value in expected.items()):raise ValueError('Worker outcome producer differs')
        if 'logSha256'in outcome:
            path=self.root/'observations'/target/'worker.log'
            self.client.call('get-object','--key','results/'+target+'/worker-log.bin',str(path))
            if path.stat().st_size!=outcome['logSize']or transfer.sha256(path)!=outcome['logSha256']:
                raise ValueError('Early worker failure log differs')
        raise RuntimeError('Worker exited before its required complete receipt; exitCode='+str(outcome['exitCode']))

    def collect(self,phase):
        if phase not in ('core','final'):raise ValueError('Unknown collection phase')
        wait=threading.Event()
        while time.time()<self.stop:
            complete=True
            for target,row in self.data['targets'].items():
                if row['state']=='failed'or phase=='core'and row['state']in ('core-retained','finished')or phase=='final'and row['state']=='finished':continue
                complete=False
                try:
                    if row['state']=='booting':
                        if not self.observe_bootstrap(target)and time.time()>self.data['bootstrapDeadline']:
                            raise TimeoutError('Owned bootstrap did not become ready before its deadline')
                        continue
                    manifest=self.read_object('results/'+target+'/manifest.json',self.root/'observations'/target/'manifest.json')
                    if manifest is None:self.check_worker_exit(target);continue
                    state=manifest.get('state')
                    if state=='core-retained'and phase=='final':self.check_worker_exit(target);continue
                    retained=self.retain(target,manifest)
                    row.update(state=state,retention=retained)
                    if state=='failed':self.stop_target(target)
                    self.save()
                except (ValueError,RuntimeError,TimeoutError)as error:
                    row.update(state='failed',error=str(error));self.data['errors'].append({'target':target,'phase':phase,'error':str(error)})
                    try:self.stop_target(target)
                    except Exception as cleanup_error:self.data['errors'].append({'target':target,'error':str(cleanup_error),'scope':'termination'})
                    self.save()
            if complete:
                self.data['phase']=phase+'-collected';self.save()
                if any(row['state']=='failed'for row in self.data['targets'].values()):raise RuntimeError('A target failed; raw retained evidence is not qualification')
                if phase=='final':self.verify_games()
                return self.data
            wait.wait(min(5,max(0,self.stop-time.time())))
        raise TimeoutError('Fixed cleanup boundary reached; remaining work is explicitly incomplete')

    def verify_games(self):
        import desktop_packages,desktop_validation,gpu_release_gates,release_identity
        result={}
        for target in TARGETS:
            row=self.data['targets'].get(target,{})
            if row.get('state')!='finished':raise ValueError('Both complete native GPU workloads are required')
            files={item['path']:item for item in row['retention']['files']}
            expected=self.plan['artifacts'][target];prefix='finished/'
            def read(relative):
                item=files[prefix+relative];path=Path(item['localPath'])
                if transfer.sha256(path)!=item['sha256']:raise ValueError('Retained qualification bytes changed')
                return json.loads(path.read_text()),item['sha256']
            receipt,_=read('gpu-release-'+target+'/'+gpu_release_gates.receipt_name(target))
            gpu_release_gates.inspect_receipt(receipt,target,self.plan['payloadSource'],expected['archiveSha256'],
                expected['downloadSha256'],expected['serverSha256'],self.plan['renderFingerprint'],require_cleanup=False)
            if receipt.get('campaignRunId')!=self.plan['runId']or receipt.get('verificationRunId')!=str(self.plan['verificationRunId']):
                raise ValueError('Game receipt belongs to another campaign or trusted artifact run')
            graphical,sha=read('Blast-Circuit-'+target+'-graphical.json')
            if receipt['graphicalSha256']!=sha:raise ValueError('Retained graphical tour differs')
            gpu_release_gates.inspect_graphical(graphical,target,self.plan['payloadSource'],release_identity.check_exports(),
                expected['archiveSha256'],expected['downloadSha256'],expected['serverSha256'])
            result[target]=receipt
        return result

    def acknowledge(self):
        for target,row in self.data['targets'].items():
            if row['state']=='finished':continue
            if row['state']!='core-retained':raise ValueError('Both committed core receipts are required before tours')
            transfer.acknowledge_core(self.client,self.plan,target,row['retention']['rawManifestSha256'],row['retention'],
                                      self.root/('core-ack-'+target+'.json'))
            row['state']='running-remaining';self.save()

    def retain_evidence(self):
        files=[]
        for path in sorted(self.root.rglob('*')):
            if not path.is_file()or path.is_symlink()or path.name=='evidence-retained.json':continue
            if path.is_relative_to(self.root/'evidence')or path.is_relative_to(self.root/'observations'):
                files.append({'path':str(path),'sha256':transfer.sha256(path)})
        scope='game-results'if any(Path(item['path']).is_relative_to(self.root/'evidence')for item in files)else'failure-only'
        if scope=='failure-only':
            try:response=self.api('cloudformation','describe-stack-events','--stack-name',campaign.stack_entry(self.plan)['id'])
            except Exception as error:response={'collectionError':str(error),'scope':'Incomplete control-plane evidence; cleanup must continue'}
            path=self.root/'failure-control-plane.json';write(path,response);files.append({'path':str(path),'sha256':transfer.sha256(path)})
        verdict='incomplete'
        try:self.verify_games();verdict='pass'
        except (ValueError,KeyError,RuntimeError,FileNotFoundError):
            if self.data.get('errors')or any(row.get('state')=='failed'for row in self.data['targets'].values()):verdict='fail'
        receipt={**{key:self.plan[key]for key in ('account','region','runId','payloadSource')},
                 'stackArn':campaign.stack_entry(self.plan)['id'],'scope':scope,'verdict':verdict,'retained':True,'files':files,
                 'verdictMeaning':'Machine game/graphics receipts only; cross-GPU image review and user approval remain separate.'}
        write(self.root/'evidence-retained.json',receipt)
        return receipt

    def cleanup(self):
        result=supervisor.cleanup_owned(self.plan,time.time()+1200,self.retain_evidence)
        self.data['cleanup']=result;self.save()
        return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--state',type=Path,required=True)
    parser.add_argument('--operator',action='store_true');parser.add_argument('--artifact-id');parser.add_argument('--artifact-digest')
    parser.add_argument('phase',choices=('intent','intent-published','launch','core','acknowledge','final','cleanup'))
    args=parser.parse_args();controller=Controller(args.state,args.operator)
    try:
        if args.phase=='intent':controller.intent()
        elif args.phase=='intent-published':controller.publish_intent(args.artifact_id,args.artifact_digest)
        elif args.phase=='launch':controller.launch()
        elif args.phase in ('core','final'):controller.collect(args.phase)
        elif args.phase=='acknowledge':controller.acknowledge()
        elif args.phase=='cleanup':
            if not controller.cleanup()['cleanupComplete']:raise RuntimeError('Owned cleanup remains incomplete')
    except BaseException as error:
        controller.data['errors'].append({'phase':args.phase,'error':str(error)});controller.save()
        if args.phase=='launch':
            try:controller.cleanup()
            except BaseException as cleanup_error:
                controller.data['errors'].append({'phase':'cleanup','error':str(cleanup_error)});controller.save()
        raise

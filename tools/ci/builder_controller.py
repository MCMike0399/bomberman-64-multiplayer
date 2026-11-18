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
import uuid

import builder_arming as arming
import builder_audit
import builder_approval
import builder_campaign as campaign
import builder_cleanup as cleanup
import builder_journal as journal
import builder_namespace as names
import builder_preflight as preflight
import builder_s3
import builder_ssm
import builder_supervisor as supervisor
import builder_transfer as transfer
import builder_timing

# Windows only since 2026-11-13; Linux qualifies on the owned workstation. Since 2026-11-17
# two parallel Windows lanes (names.SLOTS): `core` publishes one terminal checkpoint; the
# controller hands its retained gate inputs to `remaining`, which gates and finishes.
TARGETS=names.TARGETS
SLOTS=names.SLOTS
PING_SAMPLE_SECONDS=30
PING_SAMPLE_LIMIT=40
CONSOLE_SAMPLE_SECONDS=60


def new_event():
    """Collection wait; tests patch this, never the global threading module (the
    evidence retention thread pool needs real threading)."""
    return threading.Event()
BOOTSTRAP_FAILED_MARKER='SANCUS-BUILDER-BOOTSTRAP-FAILED'
CONSOLE_MARKERS=('SANCUS-BUILDER-BOOTSTRAP-FAILED','SANCUS-BUILDER-BOOTSTRAP-READY','SANCUS-BUILDER-BOOTSTRAP-SYSTEM-READY')


def utc_now():return datetime.datetime.now(datetime.timezone.utc).isoformat()


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
        self.stop=self.expiry-builder_timing.CONTROLLER_STOP_SECONDS
        self.ping_sampled={};self.console_sampled={}
        self.api=lambda *args:campaign.aws(self.plan,*args)
        self.client=builder_s3.Objects(self.api,self.plan['runId'])
        path=self.root/'controller.json'
        self.data=json.loads(path.read_text())if path.exists()else{'schemaVersion':1,'runId':self.plan['runId'],
            'source':self.plan['payloadSource'],'targets':{},'errors':[]}
        if self.data['runId']!=self.plan['runId']or self.data['source']!=self.plan['payloadSource']:
            raise ValueError('Controller state belongs to another campaign')

    def save(self):write(self.root/'controller.json',self.data)

    def check_paid_approval(self):
        if self.operator:return  # Operator fallback has no hosted approval prompt.
        # GitHub supplies the repository variable to the approved job. No
        # standing administrator token or extra Variables API scope is needed.
        builder_approval.require_unheld(os.environ.get('SANCUS_BUILDER_PAID_HOLD'))
        return builder_approval.validate_prepared(self.plan.get('approvalPlan',{}),self.plan,builder_approval.ledger())

    def intent(self):
        verify_context(self.plan,Path(__file__).resolve().parents[2],self.operator)
        self.check_paid_approval()
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
        self.check_paid_approval()
        sid=campaign.submit_intent(self.plan)
        campaign.wait_stack(self.plan,sid,'create',min(time.time()+900,self.stop),
                            observe=lambda:campaign.sync(self.plan,best_effort=True))
        campaign.sync(self.plan);arming.set_schedule_auto_delete(self.plan);ready=arming.verify_live(self.plan)
        staging=self.plan['staging']
        transfer.upload_inputs(self.client,self.plan,campaign.JOURNAL,staging['inputs'])
        transfer.publish_bootstrap(self.client,self.plan,campaign.JOURNAL,staging['manifest'],staging['bootstrap'],staging['awsCli'])
        logical={row['details'].get('logicalId'):row['id']for row in journal.effective_created(campaign.rows(self.plan)).values()}
        # `targets` is keyed by lane slot; every slot qualifies the one target windows-x64.
        self.data['targets']={slot:{'instanceId':logical[names.slot_logical(slot,'Instance')],'target':spec['target'],
                                    'lane':spec['lane'],'state':'booting','pingSamples':[]}for slot,spec in SLOTS.items()}
        self.data.update(phase='bootstrapping',bootstrapStartedAtUtc=utc_now(),
                         bootstrapDeadline=min(time.time()+builder_timing.BOOTSTRAP_SECONDS,self.stop));self.save()
        return ready

    def read_object(self,key,path):
        path.parent.mkdir(parents=True,exist_ok=True)
        # Callers poll within their phase deadline; 403 is never pending here (ListBucket held).
        samples=self.data.setdefault('objectPollSamples',{}).setdefault(key,[])
        try:
            if not builder_s3.fetch_if_present(self.client,key,path,builder_s3.CONTROLLER_PENDING,samples):return None
        except subprocess.TimeoutExpired:return None
        if path.stat().st_size>16*1024*1024:raise ValueError('Control receipt exceeds its size limit')
        return json.loads(path.read_text())

    def owned_target(self,slot):
        sid=campaign.authorize(self.plan)
        row=self.data['targets'][slot]
        owned=journal.effective_created(campaign.rows(self.plan)).get(('AWS::EC2::Instance',row['instanceId']))
        if not sid or not owned or owned['parent']!=sid or owned['details'].get('logicalId')!=names.slot_logical(slot,'Instance'):
            raise ValueError('Target is not the exact journaled stack member')
        return row['instanceId']

    def stop_target(self,slot):
        identity=self.owned_target(slot)
        row=self.data['targets'][slot]
        builder_ssm.verify_instance(self.plan,self.api,row['instanceId'],slot)
        self.api('ec2','terminate-instances','--instance-ids',identity)
        row['terminationRequested']=True;self.save()

    def stop_peers(self,failed):
        """Fail fast: once one lane fails nothing can qualify, so stop every other lane.

        Uses only the exact owned-member termination above; errors are recorded and the
        always-cleanup still terminates and audits every journaled instance.
        """
        for slot,row in self.data['targets'].items():
            if slot==failed or row.get('terminationRequested'):continue
            if row['state']not in ('failed','finished'):
                row.update(state='stopped',error='Stopped after lane '+failed+' failed; nothing can qualify')
            try:self.stop_target(slot)
            except Exception as error:self.data['errors'].append({'target':slot,'error':str(error),'scope':'peer-termination'})
            self.save()

    def ping(self,target,force=False):
        """Read-only SSM agent observation, retained as a bounded diagnostic series."""
        row=self.data['targets'][target];now=time.time()
        if not force and now-self.ping_sampled.get(target,0)<PING_SAMPLE_SECONDS:return None
        self.ping_sampled[target]=now
        sample={'atUtc':utc_now()}
        try:
            agents=self.api('ssm','describe-instance-information','--filters',json.dumps([{'Key':'InstanceIds','Values':[row['instanceId']]}]))['InstanceInformationList']
            matching=[agent for agent in agents if agent.get('InstanceId')==row['instanceId']]
            sample['pingStatus']=matching[0].get('PingStatus')if matching else'NotRegistered'
        except Exception as error:
            agents=None;sample['error']=str(error)[:300]
        samples=row.setdefault('pingSamples',[]);samples.append(sample)
        row['pingSampleCount']=row.get('pingSampleCount',0)+1
        # Keep the first observation and the most recent ones; the count is exact.
        if len(samples)>PING_SAMPLE_LIMIT:del samples[1:len(samples)-PING_SAMPLE_LIMIT+1]
        self.save()
        return agents

    def console(self,target,force=False,label='latest'):
        """Read-only guest console capture: retained as evidence, markers recorded per target."""
        row=self.data['targets'][target];now=time.time()
        if not force and now-self.console_sampled.get(target,0)<CONSOLE_SAMPLE_SECONDS:return None
        self.console_sampled[target]=now
        sample={'atUtc':utc_now(),'label':label}
        try:
            text=self.api('ec2','get-console-output','--instance-id',row['instanceId'],'--latest').get('Output')or''
        except Exception as error:
            sample['error']=str(error)[:300];row.setdefault('consoleSamples',[]).append(sample);self.save();return None
        destination=self.root/'evidence'/target/'console';destination.mkdir(parents=True,exist_ok=True)
        (destination/(label+'.txt')).write_text(text)
        seen=[marker for marker in CONSOLE_MARKERS if marker in text]
        sample.update(bytes=len(text.encode()),sha256=hashlib.sha256(text.encode()).hexdigest(),markers=seen)
        row.setdefault('consoleSamples',[]).append(sample)
        row['consoleMarkersSeen']=sorted(set(row.get('consoleMarkersSeen',[]))|set(seen))
        if len(row['consoleSamples'])>PING_SAMPLE_LIMIT:del row['consoleSamples'][1:len(row['consoleSamples'])-PING_SAMPLE_LIMIT+1]
        self.save()
        return text

    def observe_bootstrap(self,target):
        row=self.data['targets'][target]
        # Both lanes share user data, so readiness is keyed by the exact instance ID.
        receipt=self.read_object(names.instance_object(row['instanceId'],'bootstrap-ready.json'),self.root/'observations'/target/'bootstrap-ready.json')
        if receipt is None:
            self.ping(target)  # Diagnostic only while waiting; errors are recorded, never fatal.
            text=self.console(target)
            if text is not None and BOOTSTRAP_FAILED_MARKER in text:
                self.console(target,force=True,label='bootstrap-failed')
                raise RuntimeError('Guest bootstrap reported failure on its console')
            return False
        expected={'runId':self.plan['runId'],'source':self.plan['payloadSource'],'target':SLOTS[target]['target'],'instanceId':row['instanceId'],
                  'account':self.plan['account'],'region':self.plan['region'],
                  'payloadManifestSha256':self.plan['staging']['manifest']['sha256'],'ready':True,'hardwareQualified':False}
        expected['user']='BlastQa'
        if any(receipt.get(key)!=value for key,value in expected.items()):raise ValueError('Bootstrap receipt identity differs')
        if type(receipt.get('sessionId'))is not int or receipt['sessionId']<=0:
            raise ValueError('Windows bootstrap did not observe an interactive session')
        if 'readyAtUtc'not in row:row['readyAtUtc']=utc_now();self.save()
        agents=self.ping(target,force=True)
        if agents is None:return False
        if not any(agent['InstanceId']==row['instanceId']and agent['PingStatus']=='Online'for agent in agents):return False
        self.owned_target(target)
        row['commandId']=builder_ssm.dispatch(self.plan,campaign.JOURNAL,self.api,row['instanceId'],target,self.plan['staging']['manifest']['sha256'])
        row['state']='running';self.save();return True

    def retain(self,target,manifest):
        transfer.validate_results(manifest,self.plan,target)
        if manifest.get('instanceId')!=self.data['targets'][target]['instanceId']:raise ValueError('Result producer instance differs')
        state=manifest['state'];destination=self.root/'evidence'/target/state
        if destination.exists():
            prior_path=destination/'retention.json'
            if prior_path.exists():
                prior=json.loads(prior_path.read_text())
                if prior['manifestSha256']!=journal.canonical_hash(manifest):raise ValueError('A committed checkpoint changed')
                valid=all(Path(item['localPath']).is_file()and transfer.sha256(item['localPath'])==item['sha256']for item in prior['files'])
                if valid:return prior
            # Preserve interrupted/corrupt local bytes, then download the exact
            # same immutable checkpoint again. This never reruns a game case.
            destination.rename(destination.with_name(destination.name+'-partial-'+uuid.uuid4().hex[:8]))
        receipt=transfer.retain_results(self.client,self.plan,target,manifest,destination)
        receipt['manifestSha256']=journal.canonical_hash(manifest)
        receipt['rawManifestSha256']=hashlib.sha256((self.root/'observations'/target/'manifest.json').read_bytes()).hexdigest()
        write(destination/'retention.json',receipt)
        return receipt

    def check_worker_exit(self,target):
        instance=self.data['targets'][target]['instanceId']
        outcome=self.read_object(names.instance_object(instance,'worker-exit.json'),self.root/'observations'/target/'worker-exit.json')
        if outcome is None:return
        expected={'runId':self.plan['runId'],'source':self.plan['payloadSource'],'target':SLOTS[target]['target'],
                  'account':self.plan['account'],'region':self.plan['region'],'instanceId':self.data['targets'][target]['instanceId']}
        if any(outcome.get(key)!=value for key,value in expected.items()):raise ValueError('Worker outcome producer differs')
        if 'logSha256'in outcome:
            path=self.root/'observations'/target/'worker.log'
            self.client.call('get-object','--key',names.instance_object(instance,'worker-log.bin'),str(path))
            if path.stat().st_size!=outcome['logSize']or transfer.sha256(path)!=outcome['logSha256']:
                raise ValueError('Early worker failure log differs')
        raise RuntimeError('Worker exited before its required complete receipt; exitCode='+str(outcome['exitCode']))

    def phase_done(self,phase,slot,row):
        """core: the core lane finished and the remaining lane is dispatched; final: every lane finished."""
        if phase=='final'or SLOTS[slot]['lane']=='core':return row['state']=='finished'
        return row['state']not in ('booting',)

    def collect(self,phase):
        if phase not in ('core','final'):raise ValueError('Unknown collection phase')
        wait=new_event()
        while time.time()<self.stop:
            if any(row['state']in ('failed','stopped')for row in self.data['targets'].values()):
                raise RuntimeError('A lane already failed or was stopped; raw retained evidence is not qualification')
            for target,row in self.data['targets'].items():
                if row['state']in ('failed','stopped','finished'):continue
                try:
                    if row['state']=='booting':
                        if not self.observe_bootstrap(target)and time.time()>self.data['bootstrapDeadline']:
                            raise TimeoutError('Owned bootstrap did not become ready before its deadline')
                        continue
                    manifest=self.read_object('results/'+target+'/manifest.json',self.root/'observations'/target/'manifest.json')
                    if manifest is None:self.check_worker_exit(target);continue
                    try:retained=self.retain(target,manifest)
                    except (ValueError,RuntimeError)as first:
                        # Fail-fast stops the other lane too, so one transient S3 read must not
                        # cost both lanes: retry once; a deterministic mismatch fails again.
                        self.data.setdefault('retentionRetries',[]).append({'target':target,'phase':phase,'error':str(first)});self.save()
                        retained=self.retain(target,manifest)
                    row.update(state=manifest.get('state'),retention=retained)
                    if row['state']=='failed':raise RuntimeError('Lane published a failed checkpoint')
                    self.save()
                except (ValueError,RuntimeError,TimeoutError)as error:
                    row.update(state='failed',error=str(error));self.data['errors'].append({'target':target,'phase':phase,'error':str(error)})
                    try:self.console(target,force=True,label='failure')
                    except Exception as console_error:self.data['errors'].append({'target':target,'error':str(console_error),'scope':'console'})
                    try:self.stop_target(target)
                    except Exception as cleanup_error:self.data['errors'].append({'target':target,'error':str(cleanup_error),'scope':'termination'})
                    self.save()
                    # Fail fast (2026-11-17): one failed lane means nothing can qualify.
                    self.stop_peers(target)
                    self.data['phase']=phase+'-failed';self.save()
                    raise RuntimeError('Lane '+target+' failed; the other lane was stopped; raw retained evidence is not qualification')
            if all(self.phase_done(phase,target,row)for target,row in self.data['targets'].items()):
                self.data['phase']=phase+'-collected';self.save()
                if phase=='final':self.verify_games()
                return self.data
            wait.wait(min(5,max(0,self.stop-time.time())))
        raise TimeoutError('Fixed cleanup boundary reached; remaining work is explicitly incomplete')

    def retained_reader(self):
        def read(slot,relative):
            files={item['path']:item for item in self.data['targets'][slot]['retention']['files']}
            item=files.get('finished/'+relative)
            if item is None:raise ValueError('Lane did not retain '+relative+': '+slot)
            path=Path(item['localPath']);data=path.read_bytes()
            if hashlib.sha256(data).hexdigest()!=item['sha256']:raise ValueError('Retained qualification bytes changed')
            return data,item['sha256']
        return read

    def verify_games(self):
        import desktop_packages,desktop_validation,gpu_release_gates,release_identity
        for slot in SLOTS:
            if self.data['targets'].get(slot,{}).get('state')!='finished':raise ValueError('Every complete native GPU workload is required')
        path=self.root/'core-handoff.json'
        if not path.is_file():raise ValueError('The controller core handoff is required')
        handoff_sha=transfer.sha256(path)
        if self.data.get('coreHandoffSha256')!=handoff_sha:raise ValueError('Core handoff receipt changed after publication')
        read=self.retained_reader()
        lane=transfer.verify_lanes(self.plan,self.data['targets'],json.loads(path.read_text()),handoff_sha,read)
        result={}
        for target in TARGETS:
            expected=self.plan['artifacts'][target]
            data,_=read(lane,'gpu-release-'+target+'/'+gpu_release_gates.receipt_name(target));receipt=json.loads(data)
            gpu_release_gates.inspect_receipt(receipt,target,self.plan['payloadSource'],expected['archiveSha256'],
                expected['downloadSha256'],expected['serverSha256'],self.plan['renderFingerprint'],require_cleanup=False)
            if receipt.get('campaignRunId')!=self.plan['runId']or receipt.get('verificationRunId')!=str(self.plan['verificationRunId']):
                raise ValueError('Game receipt belongs to another campaign or trusted artifact run')
            data,sha=read(lane,'Blast-Circuit-'+target+'-graphical.json');graphical=json.loads(data)
            if receipt['graphicalSha256']!=sha:raise ValueError('Retained graphical tour differs')
            gpu_release_gates.inspect_graphical(graphical,target,self.plan['payloadSource'],release_identity.check_exports(),
                expected['archiveSha256'],expected['downloadSha256'],expected['serverSha256'])
            result[target]=receipt
        return result

    def acknowledge(self):
        """Publish the hash-bound core handoff to the remaining lane, then stop the idle core instance."""
        core=self.data['targets'][names.CORE_SLOT];remaining=self.data['targets'][names.REMAINING_SLOT]
        if core['state']!='finished':raise ValueError('The core lane must be finished and retained before the handoff')
        if remaining['state']in ('failed','stopped','booting'):raise ValueError('The remaining lane is not running; nothing can qualify')
        path=self.root/'core-handoff.json'
        if self.data.get('coreHandoffSha256'):
            # Resume: never publish a different receipt for this campaign.
            if not path.is_file()or self.data['coreHandoffSha256']!=transfer.sha256(path):raise ValueError('An earlier core handoff changed')
        else:
            # No recorded publication: (re)publish the deterministic receipt from the retained bytes.
            transfer.publish_core_handoff(self.client,self.plan,core,remaining,path)
            self.data['coreHandoffSha256']=transfer.sha256(path);self.data['coreHandoffPublishedAtUtc']=utc_now()
            self.save()
        # The core instance has published its only checkpoint; terminate it to save cost
        # through the exact owned-member path. Failure is recorded, cleanup still terminates it.
        if not core.get('terminationRequested'):
            try:self.stop_target(names.CORE_SLOT);core['earlyTerminationAtUtc']=utc_now()
            except Exception as error:core['earlyTerminationError']=str(error)[:300]
            self.save()

    def retain_evidence(self):
        for target,row in self.data['targets'].items():
            for item in row.get('retention',{}).get('files',[]):
                path=Path(item['localPath']).resolve()
                if not path.is_relative_to(self.root)or transfer.sha256(path)!=item['sha256']:
                    raise ValueError('Original retained game evidence changed before cleanup')
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
            if self.data.get('errors')or any(row.get('state')in ('failed','stopped')for row in self.data['targets'].values()):verdict='fail'
        receipt={**{key:self.plan[key]for key in ('account','region','runId','payloadSource')},
                 'stackArn':campaign.stack_entry(self.plan)['id'],'scope':scope,'verdict':verdict,'retained':True,'files':files,
                 'verdictMeaning':'Machine game/graphics receipts only; cross-GPU image review and user approval remain separate.'}
        write(self.root/'evidence-retained.json',receipt)
        return receipt

    def retain_available(self):
        """One bounded post-termination collection; never rerun the workload."""
        for target,row in self.data['targets'].items():
            try:
                manifest=self.read_object('results/'+target+'/manifest.json',self.root/'observations'/target/'manifest.json')
                if manifest is not None:
                    row['retention']=self.retain(target,manifest);row['state']=manifest['state'];self.save()
                else:
                    try:self.check_worker_exit(target)
                    except RuntimeError as error:self.data['errors'].append({'target':target,'scope':'worker-outcome','error':str(error)});self.save()
            except Exception as error:
                self.data['errors'].append({'target':target,'scope':'available-evidence','error':str(error)});self.save()
        return self.retain_evidence()

    def cleanup(self,budget_seconds=1200):
        # Authoritative hosted step outcome can prove that this new run never
        # entered allocation. Absence of a recovered local marker cannot.
        same_job=all(self.plan.get(key)and self.plan[key]==os.environ.get(variable)for key,variable in (
            ('controllerRunId','GITHUB_RUN_ID'),('controllerRunAttempt','GITHUB_RUN_ATTEMPT'),('controllerJobId','GITHUB_JOB')))
        rows=campaign.rows(self.plan);outcome=os.environ.get('BUILDER_LAUNCH_OUTCOME')
        never_submitted=not any(row['event']=='STATUS'and row['type']=='creation-submission'for row in rows)
        locally_proven=(never_submitted and not(self.root/'state-restored.json').exists()and outcome in ('failure','cancelled'))
        if (same_job and(outcome=='skipped'or locally_proven)and
                not any(row['event']=='CREATED'and row['type']=='AWS::CloudFormation::Stack'for row in rows)):
            audit=builder_audit.audit(self.api)
            result={'cleanupComplete':audit['passed'],'evidenceComplete':False,'allocationAttempted':False,
                    'scope':'Actual hosted allocation step was skipped; fresh namespace discovery is separate from paid-game qualification.',
                    'checked':audit['checked'],'leftovers':audit['leftovers'],'errors':audit['errors']}
            write(self.root/'cleanup-controller.json',result);self.data['cleanup']=result;self.save();return result
        result=supervisor.cleanup_owned(self.plan,time.time()+budget_seconds,self.retain_evidence)
        self.data['cleanup']=result;self.save()
        return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--state',type=Path,required=True)
    parser.add_argument('--operator',action='store_true');parser.add_argument('--artifact-id');parser.add_argument('--artifact-digest')
    parser.add_argument('phase',choices=('intent','intent-published','launch','core','acknowledge','final','terminate','retain','cleanup'))
    args=parser.parse_args();controller=Controller(args.state,args.operator)
    grant=builder_timing.grant(controller.plan,args.phase,args.operator)
    controller.data['phaseTiming']=grant;controller.save()
    try:
        if args.phase=='intent':controller.intent()
        elif args.phase=='intent-published':controller.publish_intent(args.artifact_id,args.artifact_digest)
        elif args.phase=='launch':controller.launch()
        elif args.phase in ('core','final'):controller.collect(args.phase)
        elif args.phase=='acknowledge':controller.acknowledge()
        elif args.phase=='terminate':supervisor.request_termination(controller.plan)
        elif args.phase=='retain':controller.retain_available()
        elif args.phase=='cleanup':
            if not controller.cleanup(grant['budgetSeconds'])['cleanupComplete']:raise RuntimeError('Owned cleanup remains incomplete')
    except BaseException as error:
        controller.data['errors'].append({'phase':args.phase,'error':str(error)});controller.save()
        # Hosted cleanup is a separate always step after fresh OIDC assumption,
        # including intent/core/ack/final failures. Never spend expired credentials
        # trying an implicit launch-only cleanup here.
        raise

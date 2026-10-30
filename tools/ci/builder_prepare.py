"""Prepare exact trusted-main payloads, protected references and a bounded plan before allocation."""
import argparse
import datetime
import json
import os
from pathlib import Path
import re
import subprocess

import builder_approval
import builder_assets
import builder_bootstrap
import builder_campaign as campaign
import builder_controller
import builder_identity
import builder_journal
import builder_namespace as names
import builder_payload
import builder_preflight
import builder_template
import builder_timing
import crossplay_runtime
import desktop_packages
import download_formats
import release_identity
import render_fingerprint

ROOT=Path(__file__).resolve().parents[2]


def unrecorded_intents(runs,ledger,current_run,artifacts):
    recorded={str(item['workflowRunId'])for item in ledger.get('recordedRuns',[])}
    unknown=[]
    for run in runs:
        identity=str(run['id'])
        if identity==str(current_run)or identity in recorded:continue
        if any(item['name'].startswith('builder-intent-')for item in artifacts(identity)):
            unknown.append(identity)
    return unknown


def verify_spend_history(ledger):
    def gh(path):return json.loads(subprocess.check_output(['gh','api',path,'--paginate','--slurp'],text=True,timeout=120))
    base='repos/MCMike0399/bomberman-64-multiplayer/actions/'
    runs=[run for page in gh(base+'workflows/gpu-qualification.yml/runs?per_page=100')for run in page['workflow_runs']]
    def artifacts(identity):return[item for page in gh(base+'runs/'+identity+'/artifacts?per_page=100')for item in page['artifacts']]
    unknown=unrecorded_intents(runs,ledger,os.environ.get('GITHUB_RUN_ID'),artifacts)
    if unknown:raise ValueError('Earlier allocation intents need cleanup/cost closure in the ledger before another paid run: '+','.join(unknown))


def estimate(rates):
    return builder_approval.estimate(rates)


def price_rates(aws,state):
    rates={}
    for system in ('Linux','Windows'):
        operation='RunInstances:0002'if system=='Windows'else'RunInstances'
        filters=[{'Type':'TERM_MATCH','Field':key,'Value':value}for key,value in (
            ('instanceType','g4dn.xlarge'),('regionCode',names.REGION),('operatingSystem',system),
            ('tenancy','Shared'),('preInstalledSw','NA'),('capacitystatus','Used'),('operation',operation))]
        response=aws('pricing','get-products','--service-code','AmazonEC2','--filters',json.dumps(filters))
        (state/('pricing-'+system.lower()+'.json')).write_text(json.dumps(response,indent=2)+'\n')
        values={float(dimension['pricePerUnit']['USD'])for raw in response['PriceList']for product in [json.loads(raw)]
                if product['product']['attributes'].get('operation')==operation
                for term in product.get('terms',{}).get('OnDemand',{}).values()for dimension in term['priceDimensions'].values()
                if dimension['unit']=='Hrs'}
        if len(values)!=1 or next(iter(values))<=0:raise ValueError('Unambiguous current on-demand rate required for '+system)
        rates[system]=values.pop()
    return rates


def protected(aws):
    return [{'id':item['InstanceId'],'state':item['State']['Name'],'type':item['InstanceType'],'vpc':item.get('VpcId'),
             'subnet':item.get('SubnetId'),'groups':item.get('SecurityGroups',[]),'volumes':item.get('BlockDeviceMappings',[]),
             'interfaces':[row['NetworkInterfaceId']for row in item.get('NetworkInterfaces',[])]}
            for group in aws('ec2','describe-instances')['Reservations']for item in group['Instances']]


def package_inputs(directory,source,release):
    rows=[];expected={}
    common=('Blast-Circuit-server-dotnet10.zip','Blast-Circuit-server-dotnet10.zip.sha256','server-build-metadata.json')
    for name in common:
        path=directory/name
        if not path.is_file():raise ValueError('Missing matching server input: '+name)
        rows.append({'role':'artifact','target':'all','fileName':name,'path':str(path),'sha256':desktop_packages.digest(path)})
    server=json.loads((directory/'server-build-metadata.json').read_text())
    if server.get('revision')!=source or server.get('release')!=release:raise ValueError('Server metadata differs from frozen client source')
    for target in ('windows-x64','linux-x64'):
        metadata=crossplay_runtime.metadata(directory,target)
        if metadata['revision']!=source or metadata['release']!=release:raise ValueError('Native package metadata differs')
        download_formats.inspect(directory,target,source,release)
        item=desktop_packages.names(target)
        expected[target]={'archiveSha256':desktop_packages.digest(directory/item['archive']),
                          'downloadSha256':desktop_packages.digest(directory/download_formats.FILES[target]),
                          'serverSha256':desktop_packages.digest(directory/common[0])}
        selected=[item['archive'],item['archive']+'.sha256',item['metadata'],download_formats.FILES[target],
                  download_formats.FILES[target]+'.sha256','Blast-Circuit-'+target+'-download.json']
        for name in selected:
            path=directory/name
            if not path.is_file():raise ValueError('Missing exact native input: '+name)
            rows.append({'role':'artifact','target':target,'fileName':name,'path':str(path),'sha256':desktop_packages.digest(path)})
    return rows,expected


def prepare(state,verification_run,fixed_expiry):
    if os.environ.get('MODE')=='qualify'and os.environ.get('GITHUB_RUN_ATTEMPT')!='1':
        raise ValueError('Paid reruns are refused; close the prior cleanup/cost record and use a new reviewed dispatch')
    state=Path(state).resolve();state.mkdir(parents=True,exist_ok=True)
    if (state/'plan.json').exists():raise FileExistsError('Never replace an earlier run plan or expiry')
    clock=json.loads((state/'job-clock.json').read_text());source=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    expiry=fixed_expiry or clock['fixedCampaignExpiryUtc']
    plan={'schemaVersion':1,'account':names.ACCOUNT,'region':names.REGION,'profile':'sancus','payloadSource':source,
          'runId':names.new_run_id(datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d'),source),
          'verificationRunId':str(verification_run),'fixedCampaignExpiryUtc':expiry,'minimumLaunchHeadroomSeconds':7200,
          'timing':{key:clock[key]for key in ('jobStartedAtUtc','jobTimeoutSeconds','jobMarginSeconds','cleanupBudgetSeconds')},
          **{key:clock[key]for key in ('controllerRunId','controllerRunAttempt','controllerJobId')},
          'instances':[{'os':system,'type':'g4dn.xlarge','ami':ami,'rootGb':size}for system,ami,size in (
              ('Linux',builder_identity.AMIS[0],75),('Windows',builder_identity.AMIS[1],80))],
          'references':{'vpc':os.environ.get('SANCUS_BUILDER_VPC_ID',''),'subnet':os.environ.get('SANCUS_BUILDER_SUBNET_ID','')}}
    spending=builder_approval.ledger()
    if os.environ.get('MODE')=='qualify':
        builder_approval.require_unheld(os.environ.get('SANCUS_BUILDER_PAID_HOLD'))
        plan['approvalPlan']=builder_approval.validate(json.loads(os.environ.get('BUILDER_APPROVAL_PLAN','{}')),source,verification_run,expiry,spending)
    for key,prefix in [('vpc','vpc'),('subnet','subnet')]:
        if not re.fullmatch(prefix+r'-[0-9a-f]+',plan['references'][key]):raise ValueError('Approved existing network reference is missing: '+key)
    builder_timing.envelope(plan);builder_timing.check_credentials(1800)
    campaign.configure(state)
    aws=lambda *args:campaign.aws({**plan,'dryRun':True},*args)
    builder_controller.verify_context(plan,ROOT)
    observation=builder_preflight.inspect(plan,aws)
    (state/'prepare-preflight.json').write_text(json.dumps(observation,indent=2)+'\n')
    if not observation['passed']:raise ValueError('Resolve prior leftovers/capability inputs before any new campaign')
    ledger=json.loads((ROOT/'docs/sancus-builder-spend.json').read_text())
    if os.environ.get('MODE')=='qualify':verify_spend_history(ledger)
    plan.update(budgetUsd=ledger['budgetUsd'],priorEstimateUsd=ledger['closedEstimateUsd'])
    plan['hourlyRatesUsd']=price_rates(aws,state);plan['estimatedCampaignUsd']=estimate(plan['hourlyRatesUsd'])
    if plan['estimatedCampaignUsd']>10 or plan['estimatedCampaignUsd']+plan['priorEstimateUsd']>min(50,plan['budgetUsd']):
        raise ValueError('Reviewed run/cumulative spend cap exceeded before allocation')
    if os.environ.get('MODE')=='qualify':builder_approval.validate_prepared(plan['approvalPlan'],plan,spending)
    snapshot=protected(aws);protected_path=state/'protected-instances.json'
    protected_path.write_text(json.dumps(snapshot,indent=2)+'\n');plan['references']['protectedInstancesSha256']=desktop_packages.digest(protected_path)
    plan['release']=release_identity.check_exports();plan['renderFingerprint']=render_fingerprint.calculate()['sha256']
    directory=state/'trusted-artifacts';directory.mkdir()
    for name in ('Blast-Circuit-windows-x64','Blast-Circuit-windows-x64-download','Blast-Circuit-linux-x64',
                 'Blast-Circuit-linux-x64-download','Blast-Circuit-server-dotnet10'):
        subprocess.run(['gh','run','download',str(verification_run),'--name',name,'--dir',str(directory)],check=True,timeout=600)
    inputs,plan['artifacts']=package_inputs(directory,source,plan['release'])
    tools,cli=builder_assets.freeze(state/'frozen-tools',aws);inputs.extend(tools)
    snapshot_source=builder_payload.source_bundle(ROOT,source,state/'source.zip')
    inputs.append({**snapshot_source,'role':'source','target':'all','fileName':'source.zip'})
    staged=builder_payload.prepare_files(inputs,state/'staging')
    manifest=builder_payload.public_manifest(plan,staged);manifest_path=state/'guest-inputs.json'
    manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    helper=builder_bootstrap.bundle(plan['runId'],state/'bootstrap.zip')
    manifest_info={'path':str(manifest_path),'sha256':desktop_packages.digest(manifest_path)}
    scripts=builder_bootstrap.userdata(plan,helper['sha256'],manifest_info['sha256'],cli)
    template=builder_template.build(plan,scripts);(state/'template.json').write_text(json.dumps(template,indent=2)+'\n')
    plan['staging']={'inputs':staged,'manifest':manifest_info,'bootstrap':helper,'awsCli':cli}
    (state/'plan.json').write_text(json.dumps(plan,indent=2)+'\n')
    files=[*staged,manifest_info,helper,cli,{'path':str(protected_path),'sha256':desktop_packages.digest(protected_path)}]
    receipt={**{key:plan[key]for key in ('account','region','runId','payloadSource','fixedCampaignExpiryUtc')},
             'preparationComplete':True,'planSha256':builder_journal.canonical_hash(plan),
             'templateSha256':builder_journal.canonical_hash(template),'files':files}
    (state/'prepared.json').write_text(json.dumps(receipt,indent=2)+'\n')
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'],'a')as stream:
            stream.write(f"Prepared `{source}` from Verification `{verification_run}`. Fixed expiry `{expiry}`. Estimated ${plan['estimatedCampaignUsd']:.2f}; prior ${plan['priorEstimateUsd']:.2f} / ${plan['budgetUsd']:.2f}. No instances allocated.\n")
    return plan


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--state',type=Path,required=True)
    parser.add_argument('--verification-run',required=True);parser.add_argument('--fixed-expiry',default='')
    args=parser.parse_args();prepare(args.state,args.verification_run,args.fixed_expiry)

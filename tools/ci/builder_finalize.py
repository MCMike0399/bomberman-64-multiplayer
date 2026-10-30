"""Bind successful machine evidence to actual cleanup; no AWS calls or allocation."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

import builder_namespace as names
import gpu_release_gates as gates

GROUPS={'CFN':('CFN',),'EC2':('EC2',),'EBS':('EBS',),'ENI':('ENI',),'SG':('SG',),'EIP':('EIP',),
        'IAM':('IAMRoles','IAMProfiles'),'Scheduler':('SchedulerSchedules','SchedulerGroups'),
        'Logs':('Logs',),'S3':('S3',),'SSM':('SSMParameters','SSMDocuments'),
        'AMI':('AMI',),'Snapshots':('Snapshots',),'KeyPairs':('KeyPairs',)}


def cleanup_receipt(raw,plan,variables,created_rows):
    if any(raw.get(key)!=plan[key]for key in ('account','region','runId','payloadSource')):
        raise ValueError('Cleanup campaign identity differs')
    if (not raw.get('allCreatedResourcesGone')or raw.get('leftovers')!=[]or raw.get('cleanupStageErrors')!=[]or
            raw.get('retainedEvidenceValidated')is not True or raw.get('evidenceComplete')is not True or raw.get('testVerdict')!='pass'):
        raise ValueError('Resource, evidence or machine verdict is incomplete')
    if (raw.get('protectedComparisonComplete')is not True or raw.get('protectedObservedChanges')!=[]or
            raw.get('protectedRequestedInstances',0)!=raw.get('protectedComparedInstances',-1)):
        raise ValueError('Protected inventory comparison requires review; it is not silently waived')
    if any(key.startswith('BLAST_GPU_')for key in variables):raise ValueError('Repository GPU variables are still armed or retained')
    if any(row.get('event')=='CREATED'and row.get('type')=='GitHub::Runner'for row in created_rows):
        raise ValueError('This hosted controller must not create runner registrations')
    owned=[row for row in created_rows if row.get('event')=='CREATED']
    if (sum(row['type']=='AWS::CloudFormation::Stack'for row in owned)!=1 or
            sum(row['type']=='AWS::EC2::Instance'for row in owned)!=2):
        raise ValueError('A real campaign requires its one stack and two journaled native instances')
    stack=next(row['id']for row in owned if row['type']=='AWS::CloudFormation::Stack')
    if not stack.startswith(f"arn:aws:cloudformation:{plan['region']}:{plan['account']}:stack/{plan['runId']}/"):
        raise ValueError('Exact stack ARN differs from the campaign namespace')
    final={(row['type'],row['id']):row['finalState']for row in raw.get('resources',[])}
    for row in owned:
        if row['type']=='SSM::Command':continue  # Explicitly retained AWS-managed history.
        state=final.get((row['type'],row['id']))
        if not(state=='absent'or row['type']=='AWS::EC2::Instance'and state=='terminated'or
               row['type']=='AWS::CloudFormation::Stack'and state=='DELETE_COMPLETE'):
            raise ValueError('A created physical ID is missing or not gone: '+row['id'])
    checked={}
    for kind,members in GROUPS.items():
        if any(raw.get('typeAudit',{}).get(member,{}).get('state')!='absent'for member in members):
            raise ValueError('Cleanup type is not independently absent: '+kind)
        checked[kind]={'state':'absent','sourceChecks':list(members)}
    return {'schemaVersion':1,'source':plan['payloadSource'],'account':plan['account'],'region':plan['region'],
            'runId':plan['runId'],'passed':True,'checked':checked,'leftovers':[],'errors':[],
            'protectedInventoryVerified':True,'protectedFieldsChecked':raw['protectedFieldsChecked'],
            'ephemeralRunnersRemoved':True,'runnerScope':'No runner registrations were created; coordinator uses GitHub-hosted execution.',
            'gpuVariablesDisarmed':True,'variableScope':'Fresh finalization-job repository/organization variable context; no campaign variable writes.',
            'standingIdentity':raw['standingIdentity'],'nonResourceRecords':raw['nonResourceRecords'],
            'stackArn':stack,'resources':raw['resources'],'protectedObservedChanges':raw['protectedObservedChanges'],
            'rawCleanupSha256':hashlib.sha256(json.dumps(raw,sort_keys=True,separators=(',',':')).encode()).hexdigest()}


def bind(state,variables,output):
    state=Path(state).resolve();output=Path(output).resolve()
    if output.exists():raise FileExistsError('Never overwrite a bound qualification artifact')
    plan=json.loads((state/'plan.json').read_text());raw=json.loads((state/'cleanup-receipt.json').read_text())
    import builder_journal
    rows=builder_journal.read_journal(state/'creation-manifest.jsonl',plan)
    cleanup=cleanup_receipt(raw,plan,variables,rows)
    controller=json.loads((state/'controller.json').read_text())
    output.mkdir(parents=True)
    cleanup_path=output/gates.CLEANUP_RECEIPT;cleanup_path.write_text(json.dumps(cleanup,indent=2)+'\n')
    cleanup_sha=hashlib.sha256(cleanup_path.read_bytes()).hexdigest()
    for target in ('windows-x64','linux-x64'):
        item=controller['targets'][target]
        if item['state']!='finished':raise ValueError('Target did not complete its actual workload')
        files={row['path']:row for row in item['retention']['files']}
        for relative,row in files.items():
            path=(state/'evidence'/target/'finished'/relative).resolve()
            if not path.is_relative_to(state)or hashlib.sha256(path.read_bytes()).hexdigest()!=row['sha256']:
                raise ValueError('An original retained game file changed before final binding')
        def retained(relative):
            row=files['finished/'+relative]
            # Resolve only the coordinator's fixed retained layout. Original
            # absolute paths remain evidence, never a path-selection authority.
            path=(state/'evidence'/target/'finished'/'finished'/relative).resolve()
            if not path.is_relative_to(state)or hashlib.sha256(path.read_bytes()).hexdigest()!=row['sha256']:
                raise ValueError('Retained qualification input is missing or changed')
            return path
        receipt_path=retained('gpu-release-'+target+'/'+gates.receipt_name(target))
        data=json.loads(receipt_path.read_text());expected=plan['artifacts'][target]
        gates.inspect_receipt(data,target,plan['payloadSource'],expected['archiveSha256'],expected['downloadSha256'],
                              expected['serverSha256'],plan['renderFingerprint'],require_cleanup=False)
        if data.get('campaignRunId')!=plan['runId']or data.get('verificationRunId')!=str(plan['verificationRunId']):
            raise ValueError('Native GPU receipt campaign/artifact run differs')
        graphical=retained('Blast-Circuit-'+target+'-graphical.json')
        gates.inspect_graphical(json.loads(graphical.read_text()),target,plan['payloadSource'],plan['release'],
            expected['archiveSha256'],expected['downloadSha256'],expected['serverSha256'])
        data.update(cleanupAudited=True,cleanupSha256=cleanup_sha,
                    scope='Exact-source physical GPU machine qualification and verified cleanup; visual approval remains separate.')
        (output/gates.receipt_name(target)).write_text(json.dumps(data,indent=2)+'\n')
        shutil.copy2(graphical,output/graphical.name)
        gates.inspect(output,target,plan['payloadSource'],expected['archiveSha256'],expected['downloadSha256'],expected['serverSha256'],plan['renderFingerprint'])
    return output


if __name__=='__main__':
    import os
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--state',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    if os.environ.get('GITHUB_ACTIONS')!='true':raise SystemExit('Use the fresh trusted hosted finalization job for repository variable proof')
    variables=json.loads(os.environ['BUILDER_FINAL_VARIABLES'])
    print(bind(args.state,variables,args.output))

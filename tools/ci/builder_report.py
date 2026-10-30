"""Independent end-of-run report; missing cleanup is red, never inferred absent."""
import argparse
import json
from pathlib import Path


def report(directory):
    directory=Path(directory);created={};intents=[];errors=[];cleanup=[];empty_audits=[]
    for path in sorted(directory.rglob('creation-manifest.jsonl')):
        try:
            for line in path.read_text().splitlines():
                row=json.loads(line)
                if row['event']=='CREATED':created[(row['type'],row['id'])]=row
                if row['event']=='INTENT'and row['type']=='AWS::CloudFormation::Stack':intents.append(row)
        except Exception as error:errors.append({'file':str(path),'error':str(error)})
    for path in directory.rglob('cleanup-receipt.json'):
        try:cleanup.append(json.loads(path.read_text()))
        except Exception as error:errors.append({'file':str(path),'error':str(error)})
    for path in directory.rglob('cleanup-audit-only.json'):
        try:empty_audits.append(json.loads(path.read_text()))
        except Exception as error:errors.append({'file':str(path),'error':str(error)})
    verified=any(row.get('allCreatedResourcesGone')is True and row.get('leftovers')==[]for row in cleanup)
    if not created and not intents and any(row.get('passed')is True and row.get('leftovers')==[]for row in empty_audits):verified=True
    leftovers=[]
    if not verified:
        leftovers=[{'type':kind,'id':identity,'finalState':'UNVERIFIED_AFTER_RUNNER_OR_PHASE_FAILURE'}
                   for(kind,identity),row in created.items()if kind!='SSM::Command']
        if not leftovers:
            leftovers=[{'type':'AWS::CloudFormation::Stack','id':row['id'],'finalState':'EXACT_INTENT_NOT_RECONCILED'}for row in intents]
        errors.append({'error':'No verified final cleanup receipt; recover the durable state and run exact-ID operator cleanup.'})
    return {'schemaVersion':1,'passed':verified and not errors,'resourceCleanupVerified':verified,'leftovers':leftovers,
            'errors':errors,'operatorCleanupRequired':not verified,'hardwareQualified':False,
            'ttlScope':'Schedules terminate instances only. Stack, IAM, SG/group and bucket cleanup requires the controller, next-run recovery or operator.',
            'scope':'Independent resource-outcome report; no game or visual qualification. Missing reports are not evidence of absence.'}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--artifacts',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();result=report(args.artifacts)
    args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(result,indent=2)+'\n')
    raise SystemExit(0 if result['passed']else 1)

"""Read-only AWS IAM simulations of the proposed builder identity and boundary."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

import builder_identity
import builder_namespace as names


def cases(protected_instance):
    run=names.run_id('20261030','09e454b','1234abcd')
    instance=lambda value:f'arn:aws:ec2:{names.REGION}:{names.ACCOUNT}:instance/{value}'
    role=lambda value:f'arn:aws:iam::{names.ACCOUNT}:role/{value}'
    tagged={'aws:ResourceTag/'+names.RUN_TAG:run}
    requested={'aws:RequestTag/'+names.RUN_TAG:run,'ec2:InstanceType':'g4dn.xlarge','ec2:MetadataHttpTokens':'required'}
    return [
        ('owned-termination','ec2:TerminateInstances',instance('i-00000000000000000'),tagged,True),
        ('protected-termination','ec2:TerminateInstances',instance(protected_instance),{},False),
        ('untagged-termination','ec2:TerminateInstances',instance('i-00000000000000000'),{},False),
        ('old-watchdog-tag','ec2:TerminateInstances',instance('i-00000000000000000'),{'aws:ResourceTag/sancus:job':'other'},False),
        ('old-run-tag','ec2:TerminateInstances',instance('i-00000000000000000'),{'aws:ResourceTag/'+names.RUN_TAG:'blast-gpu-old'},False),
        ('tagged-small-launch','ec2:RunInstances',instance('i-00000000000000000'),requested,True),
        ('untagged-launch','ec2:RunInstances',instance('i-00000000000000000'),{'ec2:InstanceType':'g4dn.xlarge'},False),
        ('larger-launch','ec2:RunInstances',instance('i-00000000000000000'),{**requested,'ec2:InstanceType':'g4dn.12xlarge'},False),
        ('imds-optional-launch','ec2:RunInstances',instance('i-00000000000000000'),{**requested,'ec2:MetadataHttpTokens':'optional'},False),
        ('owned-security-group','ec2:RunInstances',f'arn:aws:ec2:{names.REGION}:{names.ACCOUNT}:security-group/sg-00000000000000000',tagged,True),
        ('protected-security-group','ec2:RunInstances',f'arn:aws:ec2:{names.REGION}:{names.ACCOUNT}:security-group/sg-0b4abff510c4f1e62',{},False),
        ('pinned-image','ec2:RunInstances',f'arn:aws:ec2:{names.REGION}::image/'+builder_identity.AMIS[0],{},True),
        ('unreviewed-image','ec2:RunInstances',f'arn:aws:ec2:{names.REGION}::image/ami-00000000000000000',{},False),
        ('encrypted-volume','ec2:RunInstances',f'arn:aws:ec2:{names.REGION}:{names.ACCOUNT}:volume/vol-00000000000000000',{'ec2:Encrypted':'true'},True),
        ('unencrypted-volume','ec2:RunInstances',f'arn:aws:ec2:{names.REGION}:{names.ACCOUNT}:volume/vol-00000000000000000',{'ec2:Encrypted':'false'},False),
        ('pass-child','iam:PassRole',role(names.resource_name(run,'agent')),{'iam:PassedToService':'ec2.amazonaws.com'},True),
        ('pass-controller','iam:PassRole',role(names.CI_ROLE),{'iam:PassedToService':'ec2.amazonaws.com'},False),
        ('edit-controller','iam:PutRolePolicy',role(names.CI_ROLE),{},False),
        ('tag-existing-protected','ec2:CreateTags',instance(protected_instance),{'aws:RequestTag/'+names.RUN_TAG:run},False),
        ('create-bounded-child','iam:CreateRole',role(names.resource_name(run,'agent')),
         {'aws:RequestTag/'+names.RUN_TAG:run,'iam:PermissionsBoundary':f'arn:aws:iam::{names.ACCOUNT}:policy/{names.BOUNDARY}'},True),
        ('create-unbounded-child','iam:CreateRole',role(names.resource_name(run,'agent')),{'aws:RequestTag/'+names.RUN_TAG:run},False),
    ]


def decisions(result,action,resource):
    # AWS sometimes emits a parameterized aggregate EvalResourceName. Prefer
    # the exact resource-specific result, and never accept only an aggregate.
    rows=[row for row in result.get('EvaluationResults',[])if row.get('EvalActionName')==action]
    exact=[]
    for row in rows:
        children=row.get('ResourceSpecificResults',[])
        exact.extend(child.get('EvalResourceDecision')for child in children if child.get('EvalResourceName')==resource)
        if not children and row.get('EvalResourceName')==resource:exact.append(row.get('EvalDecision'))
    if len(exact)!=1:raise RuntimeError('IAM simulation did not uniquely evaluate the exact requested resource')
    return exact[0]


def run(output,protected_file):
    output.mkdir(parents=True,exist_ok=False)
    def aws(*arguments):
        return json.loads(subprocess.check_output(['aws','--profile','sancus','--region',names.REGION,
                                                  *arguments,'--output','json'],text=True,timeout=90))
    caller=aws('sts','get-caller-identity')
    if caller['Account']!=names.ACCOUNT:raise RuntimeError('Unexpected AWS account')
    protected=json.loads(protected_file.read_text())
    if not protected:raise RuntimeError('Protected inventory is empty')
    template=builder_identity.template();policy=builder_identity.policy()
    (output/'identity-template.json').write_text(json.dumps(template,indent=2)+'\n')
    results=[]
    for name,action,resource,context,expected in cases(protected[0]['id']):
        request={'PolicyInputList':[json.dumps(policy)],'PermissionsBoundaryPolicyInputList':[json.dumps(builder_identity.boundary())],
                 'ActionNames':[action],'ResourceArns':[resource],
                 'ContextEntries':[{'ContextKeyName':key,'ContextKeyValues':[value],'ContextKeyType':'boolean'if key=='ec2:Encrypted'else'string'}for key,value in context.items()]}
        request_path=output/(name+'-request.json');request_path.write_text(json.dumps(request,indent=2)+'\n')
        result=aws('iam','simulate-custom-policy','--cli-input-json','file://'+str(request_path.resolve()))
        (output/(name+'-result.json')).write_text(json.dumps(result,indent=2)+'\n')
        decision=decisions(result,action,resource)
        passed=(decision=='allowed')if expected else decision in ('implicitDeny','explicitDeny')
        results.append({'name':name,'action':action,'resource':resource,'decision':decision,'expectedAllowed':expected,'passed':passed})
    receipt={'account':caller['Account'],'region':names.REGION,'readOnly':True,
             'templateSha256':hashlib.sha256((output/'identity-template.json').read_bytes()).hexdigest(),
             'protectedInventorySha256':hashlib.sha256(protected_file.read_bytes()).hexdigest(),
             'cases':results,'passed':all(row['passed']for row in results),
             'scope':'Custom identity + boundary simulation only. Not live OIDC trust, environment protection, provisioning, cleanup or hardware proof.'}
    (output/'simulation.json').write_text(json.dumps(receipt,indent=2)+'\n')
    if not receipt['passed']:raise RuntimeError('Builder IAM simulation failed; inspect retained exact decisions')
    return receipt


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--protected-inventory',type=Path,required=True)
    args=parser.parse_args();print(json.dumps(run(args.output,args.protected_inventory),indent=2))

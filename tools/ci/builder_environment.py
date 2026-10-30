"""Configure and read back the user-authorized GitHub approval and OIDC boundary."""
import argparse
import json
from pathlib import Path
import subprocess

import builder_identity as identity

REVIEWER_ID=40257172
REPOSITORY_ID=1426399566
CLAIMS=['repo','context','job_workflow_ref']


def verify(environment,branches,oidc):
    if environment.get('deployment_branch_policy')!={'protected_branches':False,'custom_branch_policies':True}:
        raise RuntimeError('Builder environment must use explicit custom main-only branch protection')
    rows=branches.get('branch_policies',[])
    if len(rows)!=1 or rows[0].get('name')!='main'or rows[0].get('type')!='branch':
        raise RuntimeError('Builder environment must allow only the main branch, never tags or wildcards')
    reviewers=[row for rule in environment.get('protection_rules',[])if rule.get('type')=='required_reviewers'
               for row in rule.get('reviewers',[])]
    if len(reviewers)!=1 or reviewers[0].get('type')!='User'or reviewers[0].get('reviewer',{}).get('id')!=REVIEWER_ID:
        raise RuntimeError('The user must be the required builder environment reviewer')
    if oidc.get('use_default')is not False or oidc.get('include_claim_keys')!=CLAIMS or oidc.get('use_immutable_subject')is not True:
        raise RuntimeError('OIDC must require the immutable repository, environment and reusable-workflow claims')
    prefix='repo:MCMike0399@40257172/bomberman-64-multiplayer@1426399566'
    if oidc.get('sub_claim_prefix')!=prefix:
        raise RuntimeError('Unexpected immutable OIDC repository identity')
    return {'passed':True,'expectedSubject':identity.OIDC_SUBJECT,
            'scope':'GitHub settings readback only; a live reusable-workflow OIDC assume-role is still required.'}


def configure(output,apply=False):
    output.mkdir(parents=True,exist_ok=False)
    def api(path,method='GET',data=None,missing=False):
        command=['gh','api',path,'--method',method]
        if data is not None:command+=['--input','-']
        result=subprocess.run(command,input=json.dumps(data)if data is not None else None,
                              capture_output=True,text=True,timeout=60)
        if result.returncode:
            if missing and 'HTTP 404'in result.stderr:return None
            raise RuntimeError('GitHub approval-boundary operation failed: '+method+' '+path+' '+result.stderr.strip())
        return json.loads(result.stdout)if result.stdout.strip()else None
    base='repos/'+identity.REPOSITORY
    repository=api(base)
    if repository['id']!=REPOSITORY_ID or repository['owner']['id']!=REVIEWER_ID:
        raise RuntimeError('Unexpected repository or owner identity')
    env_path=base+'/environments/'+identity.ENVIRONMENT
    oidc_path=base+'/actions/oidc/customization/sub'
    environment=api(env_path,missing=True);oidc=api(oidc_path)
    (output/'before.json').write_text(json.dumps({'environment':environment,'oidc':oidc},indent=2)+'\n')
    if apply:
        # The inspected repository currently has no OIDC-consuming workflows.
        # Preserve immutable IDs; never silently replace an unrelated custom template.
        if oidc.get('use_default')is not True and oidc.get('include_claim_keys')!=CLAIMS:
            raise RuntimeError('An unrelated OIDC customization needs explicit reconciliation')
        if environment is None:
            api(env_path,'PUT',{'wait_timer':0,'prevent_self_review':False,
                'reviewers':[{'type':'User','id':REVIEWER_ID}],
                'deployment_branch_policy':{'protected_branches':False,'custom_branch_policies':True}})
            api(env_path+'/deployment-branch-policies','POST',{'name':'main','type':'branch'})
        # Existing environments are checked, not overwritten by this bootstrap.
        environment=api(env_path)
        branches=api(env_path+'/deployment-branch-policies')
        desired={**oidc,'use_default':False,'include_claim_keys':CLAIMS,'use_immutable_subject':True}
        verify(environment,branches,desired)
        api(oidc_path,'PUT',{'use_default':False,'include_claim_keys':CLAIMS,'use_immutable_subject':True})
    environment=api(env_path);branches=api(env_path+'/deployment-branch-policies');oidc=api(oidc_path)
    receipt={**verify(environment,branches,oidc),'environment':environment,'branches':branches,'oidc':oidc}
    (output/'readback.json').write_text(json.dumps(receipt,indent=2)+'\n')
    return receipt


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--apply',action='store_true',help='Apply the explicitly user-approved settings, then verify readback')
    args=parser.parse_args();print(json.dumps(configure(args.output,args.apply),indent=2))

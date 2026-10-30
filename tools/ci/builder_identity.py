"""Generate the reviewable standing identity; this command never applies it."""
import argparse
import json
from pathlib import Path

import builder_namespace as names

REPOSITORY='MCMike0399/bomberman-64-multiplayer'
ENVIRONMENT='gpu-qualification'  # GitHub-local; intentionally not an AWS name.
WORKFLOW=REPOSITORY+'/.github/workflows/builder-controller.yml@refs/heads/main'
OIDC_SUBJECT=('repo:MCMike0399@40257172/bomberman-64-multiplayer@1426399566:'
              f'environment:{ENVIRONMENT}:job_workflow_ref:{WORKFLOW}')
# Resolved official AMIs, pinned rather than allowing arbitrary community images.
# A new image requires a reviewed policy update before a subsequent campaign.
AMIS=('ami-05414055cdd6e338f','ami-057fee592d97c84cb')


def policy():
    account,region=names.ACCOUNT,names.REGION
    arn=lambda service,resource:f'arn:aws:{service}:{region}:{account}:{resource}'
    iam=lambda resource:f'arn:aws:iam::{account}:{resource}'
    run=names.RUN_GLOB
    tagged={'StringLike':{'aws:ResourceTag/'+names.RUN_TAG:run}}
    requested={'StringLike':{'aws:RequestTag/'+names.RUN_TAG:run}}
    statement=[]

    def allow(sid,actions,resources,condition=None):
        row={'Sid':sid,'Effect':'Allow','Action':actions,'Resource':resources}
        if condition:row['Condition']=condition
        statement.append(row)

    allow('AuditOnly',['ec2:Describe*','cloudformation:ListStacks','tag:GetResources',
          'servicequotas:GetServiceQuota','pricing:GetProducts','ssm:DescribeInstanceInformation',
          'ssm:ListCommands','ssm:ListCommandInvocations','ssm:GetCommandInvocation',
          'iam:ListRoles','iam:ListInstanceProfiles','scheduler:ListScheduleGroups',
          'scheduler:ListSchedules','logs:DescribeLogGroups','s3:ListAllMyBuckets',
          'ssm:DescribeParameters','ssm:ListDocuments'],'*')
    stacks=arn('cloudformation','stack/'+run+'/*')
    allow('CreateOwnedStacks','cloudformation:CreateStack',stacks,requested)
    allow('InspectOwnedStacks',['cloudformation:DescribeStacks','cloudformation:DescribeStackResources',
          'cloudformation:ListStackResources','cloudformation:DescribeStackEvents','cloudformation:GetTemplate'],stacks)
    allow('DeleteOwnedStacks','cloudformation:DeleteStack',stacks,tagged)
    allow('LaunchTaggedSmallInstances','ec2:RunInstances',arn('ec2','instance/*'),
          {'StringLike':{'aws:RequestTag/'+names.RUN_TAG:run},
           'StringEquals':{'ec2:InstanceType':'g4dn.xlarge','ec2:MetadataHttpTokens':'required'}})
    # RunInstances authorizes referenced resources separately from new instances.
    allow('LaunchPinnedImages','ec2:RunInstances',[f'arn:aws:ec2:{region}::image/'+image for image in AMIS])
    allow('LaunchOwnedSecurityGroup','ec2:RunInstances',arn('ec2','security-group/*'),tagged)
    allow('LaunchDependencies','ec2:RunInstances',[arn('ec2','subnet/*'),arn('ec2','network-interface/*')])
    allow('LaunchEncryptedVolumes','ec2:RunInstances',arn('ec2','volume/*'),{'Bool':{'ec2:Encrypted':'true'}})
    allow('TagOnlyDuringCreation','ec2:CreateTags',[
          arn('ec2',kind+'/*')for kind in ('instance','volume','network-interface','security-group')],
          {'StringEquals':{'ec2:CreateAction':['RunInstances','CreateSecurityGroup']},
           'StringLike':{'aws:RequestTag/'+names.RUN_TAG:run}})
    allow('TerminateOnlyOwnedInstances','ec2:TerminateInstances',arn('ec2','instance/*'),tagged)
    allow('CreateOwnedSecurityGroup','ec2:CreateSecurityGroup',arn('ec2','security-group/*'),requested)
    allow('ReferenceVpcForSecurityGroup','ec2:CreateSecurityGroup',arn('ec2','vpc/*'))
    allow('DeleteOnlyOwnedNetworkStorage',['ec2:DeleteSecurityGroup','ec2:DeleteVolume','ec2:DeleteNetworkInterface'],
          [arn('ec2',kind+'/*')for kind in ('security-group','volume','network-interface')],tagged)
    roles=iam('role/'+run)
    allow('CreateBoundedOwnedRole','iam:CreateRole',roles,
          {'StringLike':{'aws:RequestTag/'+names.RUN_TAG:run},
           'ArnEquals':{'iam:PermissionsBoundary':iam('policy/'+names.BOUNDARY)}})
    allow('TagOwnedRoleAtCreation','iam:TagRole',roles,requested)
    allow('ManageOwnedRole',['iam:GetRole','iam:ListRoleTags','iam:ListRolePolicies','iam:GetRolePolicy',
          'iam:PutRolePolicy','iam:DeleteRolePolicy','iam:DeleteRole'],roles)
    # No Put/DeleteRolePermissionsBoundary, trust updates, managed-policy edits,
    # TagRole, or access to the standing CI role. Children cannot escape this cap.
    allow('PassOnlyOwnedRole','iam:PassRole',roles,
          {'StringEquals':{'iam:PassedToService':['ec2.amazonaws.com','scheduler.amazonaws.com']}})
    allow('ManageOwnedProfile',['iam:CreateInstanceProfile','iam:GetInstanceProfile',
          'iam:AddRoleToInstanceProfile','iam:RemoveRoleFromInstanceProfile',
          'iam:DeleteInstanceProfile','iam:TagInstanceProfile','iam:ListInstanceProfileTags'],
          iam('instance-profile/'+run))
    groups=arn('scheduler','schedule-group/'+run)
    schedules=arn('scheduler','schedule/'+run+'/'+run)
    allow('CreateOwnedScheduleGroup','scheduler:CreateScheduleGroup',groups,requested)
    allow('ManageOwnedScheduleGroup',['scheduler:GetScheduleGroup','scheduler:DeleteScheduleGroup',
          'scheduler:ListTagsForResource'],groups)
    allow('ManageOwnedSchedule',['scheduler:CreateSchedule','scheduler:GetSchedule',
          'scheduler:DeleteSchedule'],schedules)
    allow('ExecuteOnlyOwnedGuests','ssm:SendCommand',arn('ec2','instance/*'),tagged)
    allow('OnlyStandardCommandDocuments','ssm:SendCommand',[
          f'arn:aws:ssm:{region}::document/AWS-RunShellScript',
          f'arn:aws:ssm:{region}::document/AWS-RunPowerShellScript'])
    # The instance SSM agent uses these channels; they confer no host-mutation API.
    # These current AMIs use modern SSM Agent message channels. Do not grant
    # legacy ec2messages or State Manager association permissions.
    allow('AgentChannels',['ssm:UpdateInstanceInformation','ssmmessages:CreateControlChannel',
          'ssmmessages:CreateDataChannel','ssmmessages:OpenControlChannel','ssmmessages:OpenDataChannel',
          ],'*')
    allow('ReadStandardDocuments',['ssm:GetDocument','ssm:DescribeDocument'],[
          f'arn:aws:ssm:{region}::document/AWS-RunShellScript',
          f'arn:aws:ssm:{region}::document/AWS-RunPowerShellScript'])
    allow('OfficialWindowsDriver','s3:GetObject',
          'arn:aws:s3:::ec2-windows-nvidia-drivers/latest/596.86__grid_win10_win11_server2022_server2025_dch_64bit_international_aws_swl.exe')
    return {'Version':'2012-10-17','Statement':statement}


def boundary():
    # IAM's managed-policy limit is 6,144 non-whitespace characters. Statement
    # labels aid review but carry no semantics, so omit them from the boundary.
    groups={}
    for row in policy()['Statement']:
        base={key:value for key,value in row.items()if key not in ('Sid','Action')}
        key=json.dumps(base,sort_keys=True)
        actions=row['Action']if isinstance(row['Action'],list)else[row['Action']]
        if key not in groups:groups[key]={**base,'Action':[]}
        groups[key]['Action'].extend(actions)
    for row in groups.values():
        if len(row['Action'])==1:row['Action']=row['Action'][0]
    data={'Version':'2012-10-17','Statement':list(groups.values())}
    if len(json.dumps(data,separators=(',',':')))>6144:
        raise ValueError('Builder boundary exceeds the IAM managed-policy size limit')
    return data


def template():
    permissions=policy()
    data={'AWSTemplateFormatVersion':'2010-09-09',
          'Description':'Scoped builder CI identity; existing OIDC provider remains external',
          'Resources':{
              'Boundary':{'Type':'AWS::IAM::ManagedPolicy','Properties':{
                  'ManagedPolicyName':names.BOUNDARY,'PolicyDocument':boundary()}},
              'ControllerRole':{'Type':'AWS::IAM::Role','Properties':{
                  'RoleName':names.CI_ROLE,'MaxSessionDuration':10800,
                  'PermissionsBoundary':{'Ref':'Boundary'},
                  'AssumeRolePolicyDocument':{'Version':'2012-10-17','Statement':[{
                      'Effect':'Allow','Action':'sts:AssumeRoleWithWebIdentity',
                      'Principal':{'Federated':names.OIDC_PROVIDER},
                      'Condition':{'StringEquals':{
                          'token.actions.githubusercontent.com:aud':'sts.amazonaws.com',
                          'token.actions.githubusercontent.com:sub':OIDC_SUBJECT}}}]},
                  'Policies':[{'PolicyName':'sancus-builder-controller','PolicyDocument':permissions}]}}},
          'Outputs':{'RoleArn':{'Value':{'Fn::GetAtt':['ControllerRole','Arn']}}}}
    return names.validate_aws_names(data)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(template(),indent=2)+'\n')

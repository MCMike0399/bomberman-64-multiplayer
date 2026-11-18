"""One owned SSM campaign stack; references never become cleanup targets."""
import base64
import json

import builder_namespace as names
import builder_staging


def build(plan,bootstrap):
    run=plan['runId'];ref=lambda key:{'Ref':key};sub=lambda value:{'Fn::Sub':value}
    tags=[{'Key':names.RUN_TAG,'Value':ref('RunId')},
          {'Key':names.EXPIRY_TAG,'Value':plan['fixedCampaignExpiryUtc']}]
    group=names.resource_name(run,'expiry-group')
    boundary=f'arn:aws:iam::{names.ACCOUNT}:policy/{names.BOUNDARY}'
    resources=builder_staging.resources(run,plan['fixedCampaignExpiryUtc'])
    resources.update({
        'ExpiryGroup':{'Type':'AWS::Scheduler::ScheduleGroup','Properties':{'Name':group,'Tags':tags}},
        'QaSecurityGroup':{'Type':'AWS::EC2::SecurityGroup','Properties':{
            'GroupName':names.resource_name(run,'sg'),'GroupDescription':'Owned expiring builder; no ingress',
            'VpcId':ref('VpcId'),'Tags':tags}},
        'AgentRole':{'Type':'AWS::IAM::Role','DependsOn':['StagingBucketPolicy'],'Properties':{
            'RoleName':names.child_name(run,'agent'),'PermissionsBoundary':boundary,'Tags':tags,
            'AssumeRolePolicyDocument':{'Version':'2012-10-17','Statement':[{
                'Effect':'Allow','Principal':{'Service':'ec2.amazonaws.com'},'Action':'sts:AssumeRole'}]},
            'Policies':[{'PolicyName':names.resource_name(run,'agent-permissions'),'PolicyDocument':{
                'Version':'2012-10-17','Statement':[
                    {'Effect':'Allow','Action':['ssm:UpdateInstanceInformation',
                        'ssmmessages:CreateControlChannel','ssmmessages:CreateDataChannel',
                        'ssmmessages:OpenControlChannel','ssmmessages:OpenDataChannel'],'Resource':'*'},
                    {'Effect':'Allow','Action':['ssm:GetDocument','ssm:DescribeDocument'],'Resource':[
                        f'arn:aws:ssm:{names.REGION}::document/AWS-RunShellScript',
                        f'arn:aws:ssm:{names.REGION}::document/AWS-RunPowerShellScript']},
                    *builder_staging.guest_statements(run)]}}]}},
        'AgentProfile':{'Type':'AWS::IAM::InstanceProfile','Properties':{
            'InstanceProfileName':names.resource_name(run,'profile'),'Roles':[ref('AgentRole')]}},
    })
    # Windows only since 2026-11-13. Since 2026-11-17 two parallel Windows lanes
    # (builder_namespace.SLOTS): `core` keeps WindowsInstance/WindowsExpiry and the Name
    # `sancus-builder`; `remaining` is WindowsRemainingInstance/WindowsRemainingExpiry,
    # Name `sancus-builder-remaining`. Both receive identical user data; the lane is only
    # chosen by the controller's per-slot SSM descriptor.
    if [(item['os'],item.get('slot'),item.get('lane'))for item in plan['instances']]!=[
            (row['os'],slot,row['lane'])for slot,row in names.SLOTS.items()]:
        raise ValueError('This reviewed campaign requires exactly the two parallel Windows lane instances')
    for item in plan['instances']:
        osname=item['os'];slot=item['slot']
        resources[names.slot_logical(slot,'Instance')]={'Type':'AWS::EC2::Instance','DeletionPolicy':'Delete',
            'DependsOn':['StagingBucketPolicy','ExpiryGroup'],'Properties':{
                'ImageId':item['ami'],'InstanceType':'g4dn.xlarge','IamInstanceProfile':ref('AgentProfile'),
                'InstanceInitiatedShutdownBehavior':'terminate','PropagateTagsToVolumeOnCreation':True,
                'MetadataOptions':{'HttpTokens':'required','HttpEndpoint':'enabled','HttpPutResponseHopLimit':1},
                'NetworkInterfaces':[{'DeviceIndex':'0','SubnetId':ref('SubnetId'),'AssociatePublicIpAddress':True,
                    'DeleteOnTermination':True,'GroupSet':[ref('QaSecurityGroup')]}],
                'BlockDeviceMappings':[{'DeviceName':'/dev/sda1','Ebs':{
                    'VolumeSize':item['rootGb'],'VolumeType':'gp3','Encrypted':True,'DeleteOnTermination':True}}],
                'UserData':base64.b64encode(bootstrap[osname].encode()).decode(),
                'Tags':tags+[{'Key':'Name','Value':names.instance_name(osname.lower(),slot)}]}}
    instance_arns=[sub(f'arn:aws:ec2:{names.REGION}:{names.ACCOUNT}:instance/${{{names.slot_logical(slot,"Instance")}}}')for slot in names.SLOTS]
    resources['ExpiryRole']={'Type':'AWS::IAM::Role','DependsOn':['ExpiryGroup'],'Properties':{
        'RoleName':names.child_name(run,'expiry'),'PermissionsBoundary':boundary,'Tags':tags,
        'AssumeRolePolicyDocument':{'Version':'2012-10-17','Statement':[{
            'Effect':'Allow','Principal':{'Service':'scheduler.amazonaws.com'},'Action':'sts:AssumeRole',
            'Condition':{'StringEquals':{'aws:SourceAccount':names.ACCOUNT},
                         'ArnEquals':{'aws:SourceArn':f'arn:aws:scheduler:{names.REGION}:{names.ACCOUNT}:schedule-group/{group}'}}}]},
        'Policies':[{'PolicyName':names.resource_name(run,'termination'),'PolicyDocument':{
            'Version':'2012-10-17','Statement':[{'Effect':'Allow','Action':'ec2:TerminateInstances',
                'Resource':instance_arns,'Condition':{'StringEquals':{'ec2:ResourceTag/'+names.RUN_TAG:ref('RunId')}}}]}}]}}
    for slot,row in names.SLOTS.items():
        resources[names.slot_logical(slot,'Expiry')]={'Type':'AWS::Scheduler::Schedule','Properties':{
            'Name':names.child_name(run,row['expiry']),'GroupName':group,
            # CFN's live resource schema does not expose ActionAfterCompletion.
            # The controller must set DELETE through UpdateSchedule and verify
            # it before arming; CFN still owns explicit schedule deletion.
            'ScheduleExpression':sub('at(${ExpiresAt})'),
            'ScheduleExpressionTimezone':'UTC','FlexibleTimeWindow':{'Mode':'OFF'},'State':'ENABLED',
            'Target':{'Arn':'arn:aws:scheduler:::aws-sdk:ec2:terminateInstances',
                'RoleArn':{'Fn::GetAtt':['ExpiryRole','Arn']},
                'Input':sub('{"InstanceIds":["${'+names.slot_logical(slot,'Instance')+'}"]}'),
                'RetryPolicy':{'MaximumRetryAttempts':3,'MaximumEventAgeInSeconds':60}}}}
    template={'AWSTemplateFormatVersion':'2010-09-09','Description':'One owned expiring SSM builder campaign',
              'Parameters':{key:{'Type':'String'}for key in ('RunId','VpcId','SubnetId','ExpiresAt')},'Resources':resources}
    return names.validate_aws_names(template)

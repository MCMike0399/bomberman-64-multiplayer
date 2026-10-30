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
            'VpcId':ref('VpcId'),'SecurityGroupIngress':[],'Tags':tags}},
        'AgentRole':{'Type':'AWS::IAM::Role','DependsOn':['StagingBucketPolicy'],'Properties':{
            'RoleName':names.resource_name(run,'agent'),'PermissionsBoundary':boundary,'Tags':tags,
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
    if {item['os']for item in plan['instances']}!={'Linux','Windows'}or len(plan['instances'])!=2:
        raise ValueError('This reviewed campaign requires exactly one Windows and one Linux instance')
    for item in plan['instances']:
        osname=item['os']
        resources[osname+'Instance']={'Type':'AWS::EC2::Instance','DeletionPolicy':'Delete',
            'DependsOn':['StagingBucketPolicy','ExpiryGroup'],'Properties':{
                'ImageId':item['ami'],'InstanceType':'g4dn.xlarge','IamInstanceProfile':ref('AgentProfile'),
                'InstanceInitiatedShutdownBehavior':'terminate','PropagateTagsToVolumeOnCreation':True,
                'MetadataOptions':{'HttpTokens':'required','HttpEndpoint':'enabled','HttpPutResponseHopLimit':1},
                'NetworkInterfaces':[{'DeviceIndex':'0','SubnetId':ref('SubnetId'),'AssociatePublicIpAddress':True,
                    'DeleteOnTermination':True,'GroupSet':[ref('QaSecurityGroup')]}],
                'BlockDeviceMappings':[{'DeviceName':'/dev/sda1','Ebs':{
                    'VolumeSize':item['rootGb'],'VolumeType':'gp3','Encrypted':True,'DeleteOnTermination':True}}],
                'UserData':base64.b64encode(bootstrap[osname].encode()).decode(),
                'Tags':tags+[{'Key':'Name','Value':names.instance_name(osname.lower())}]}}
    instance_arns=[sub(f'arn:aws:ec2:{names.REGION}:{names.ACCOUNT}:instance/${{{osname}Instance}}')for osname in ('Linux','Windows')]
    resources['ExpiryRole']={'Type':'AWS::IAM::Role','DependsOn':['ExpiryGroup'],'Properties':{
        'RoleName':names.resource_name(run,'expiry'),'PermissionsBoundary':boundary,'Tags':tags,
        'AssumeRolePolicyDocument':{'Version':'2012-10-17','Statement':[{
            'Effect':'Allow','Principal':{'Service':'scheduler.amazonaws.com'},'Action':'sts:AssumeRole',
            'Condition':{'StringEquals':{'aws:SourceAccount':names.ACCOUNT},
                         'ArnEquals':{'aws:SourceArn':f'arn:aws:scheduler:{names.REGION}:{names.ACCOUNT}:schedule-group/{group}'}}}]},
        'Policies':[{'PolicyName':names.resource_name(run,'termination'),'PolicyDocument':{
            'Version':'2012-10-17','Statement':[{'Effect':'Allow','Action':'ec2:TerminateInstances',
                'Resource':instance_arns,'Condition':{'StringEquals':{'ec2:ResourceTag/'+names.RUN_TAG:ref('RunId')}}}]}}]}}
    for osname in ('Linux','Windows'):
        resources[osname+'Expiry']={'Type':'AWS::Scheduler::Schedule','Properties':{
            'Name':names.resource_name(run,osname.lower()+'-expiry'),'GroupName':group,
            # CFN's live resource schema does not expose ActionAfterCompletion.
            # The controller must set DELETE through UpdateSchedule and verify
            # it before arming; CFN still owns explicit schedule deletion.
            'ScheduleExpression':sub('at(${ExpiresAt})'),
            'ScheduleExpressionTimezone':'UTC','FlexibleTimeWindow':{'Mode':'OFF'},'State':'ENABLED',
            'Target':{'Arn':'arn:aws:scheduler:::aws-sdk:ec2:terminateInstances',
                'RoleArn':{'Fn::GetAtt':['ExpiryRole','Arn']},
                'Input':sub('{"InstanceIds":["${'+osname+'Instance}"]}'),
                'RetryPolicy':{'MaximumRetryAttempts':3,'MaximumEventAgeInSeconds':60}}}}
    template={'AWSTemplateFormatVersion':'2010-09-09','Description':'One owned expiring SSM builder campaign',
              'Parameters':{key:{'Type':'String'}for key in ('RunId','VpcId','SubnetId','ExpiresAt')},'Resources':resources}
    return names.validate_aws_names(template)

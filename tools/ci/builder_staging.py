"""Per-run private staging specification; no API calls or GitHub credentials."""
import re
from datetime import datetime

import builder_namespace as names

BUCKET_ACTIONS=(
    's3:CreateBucket','s3:DeleteBucket','s3:PutBucketPublicAccessBlock',
    's3:PutEncryptionConfiguration','s3:PutLifecycleConfiguration','s3:PutBucketPolicy',
    's3:DeleteBucketPolicy','s3:PutBucketTagging','s3:PutBucketOwnershipControls',
    # Read-only bucket configuration on the owned bucket. CloudFormation's
    # AWS::S3::Bucket read handler also reads accelerate, analytics,
    # intelligent-tiering, inventory, metrics and replication configuration.
    's3:GetBucket*','s3:Get*Configuration',
    's3:ListBucket','s3:ListBucketVersions','s3:ListBucketMultipartUploads',
    's3:ListTagsForResource','s3:TagResource',
)
OBJECT_ACTIONS=('s3:GetObject','s3:PutObject','s3:DeleteObject','s3:DeleteObjectVersion',
                's3:AbortMultipartUpload','s3:ListMultipartUploadParts')


def bucket_name(run):
    if not re.fullmatch(names.RUN_PATTERN,run):raise ValueError('Staging bucket requires one exact builder run')
    value=run+'-'+names.ACCOUNT
    if len(value)>63:raise ValueError('Staging bucket name exceeds S3 limit')
    return value


def controller_statements():
    bucket='arn:aws:s3:::'+names.RUN_GLOB
    owner={'StringEqualsIfExists':{'s3:ResourceAccount':names.ACCOUNT}}
    return [
        {'Sid':'OwnedStagingBucket','Effect':'Allow','Action':list(BUCKET_ACTIONS),'Resource':bucket,'Condition':owner},
        {'Sid':'OwnedStagingObjects','Effect':'Allow','Action':list(OBJECT_ACTIONS),'Resource':bucket+'/*','Condition':owner},
    ]


def guest_statements(run):
    bucket='arn:aws:s3:::'+bucket_name(run)
    # Guests cannot replace frozen payloads, list a bucket, configure it, or delete.
    return [
        {'Effect':'Allow','Action':'s3:GetObject','Resource':bucket+'/payload/*'},
        {'Effect':'Allow','Action':'s3:PutObject','Resource':bucket+'/results/*'},
    ]


def resources(run,expires):
    parsed=datetime.fromisoformat(expires.replace('Z','+00:00'))
    if parsed.tzinfo is None:raise ValueError('Staging expiration must include a timezone')
    bucket=bucket_name(run)
    tags=[{'Key':names.RUN_TAG,'Value':run},{'Key':names.EXPIRY_TAG,'Value':expires}]
    result={
        'StagingBucket':{'Type':'AWS::S3::Bucket','DeletionPolicy':'Delete',
            'UpdateReplacePolicy':'Delete','Properties':{
                'BucketName':bucket,'Tags':tags,
                'PublicAccessBlockConfiguration':{key:True for key in (
                    'BlockPublicAcls','IgnorePublicAcls','BlockPublicPolicy','RestrictPublicBuckets')},
                'OwnershipControls':{'Rules':[{'ObjectOwnership':'BucketOwnerEnforced'}]},
                'BucketEncryption':{'ServerSideEncryptionConfiguration':[
                    {'ServerSideEncryptionByDefault':{'SSEAlgorithm':'AES256'}}]},
                'LifecycleConfiguration':{'Rules':[{'Id':'sancus-builder-expire','Status':'Enabled',
                    'ExpirationInDays':1,'AbortIncompleteMultipartUpload':{'DaysAfterInitiation':1}}]},
            }},
        'StagingBucketPolicy':{'Type':'AWS::S3::BucketPolicy','DeletionPolicy':'Delete','Properties':{
            'Bucket':{'Ref':'StagingBucket'},'PolicyDocument':{'Version':'2012-10-17','Statement':[{
                'Sid':'TlsOnly','Effect':'Deny','Principal':'*','Action':'s3:*',
                'Resource':['arn:aws:s3:::'+bucket,'arn:aws:s3:::'+bucket+'/*'],
                'Condition':{'Bool':{'aws:SecureTransport':'false'}}}]}}},
    }
    return names.validate_aws_names({'Resources':result})['Resources']

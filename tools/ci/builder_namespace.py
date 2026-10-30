"""AWS ownership namespace, intentionally disjoint from sancus-runner and historical runs."""
import re

PREFIX='sancus-builder-'
TAG_PREFIX='sancus-builder:'
RUN_TAG=TAG_PREFIX+'run'
EXPIRY_TAG=TAG_PREFIX+'expires'
IDENTITY_STACK=PREFIX+'ci-identity'
CI_ROLE=PREFIX+'ci'
BOUNDARY=PREFIX+'boundary'
OIDC_PROVIDER='arn:aws:iam::524329886851:oidc-provider/token.actions.githubusercontent.com'
ACCOUNT='524329886851'
REGION='us-east-1'
RUN_PATTERN=r'sancus-builder-20[0-9]{6}-[0-9a-f]{7,12}-[0-9a-f]{8}'
# IAM glob syntax, deliberately excludes the standing CI role and boundary.
RUN_GLOB=PREFIX+'20??????-*'


def run_id(day,sha,nonce):
    if not re.fullmatch(r'20[0-9]{6}',day)or not re.fullmatch(r'[0-9a-f]{7,12}',sha)or not re.fullmatch(r'[0-9a-f]{8}',nonce):
        raise ValueError('Expected date, short source SHA and unique hexadecimal nonce')
    return PREFIX+day+'-'+sha+'-'+nonce


def resource_name(run,suffix):
    if not re.fullmatch(RUN_PATTERN,run)or not re.fullmatch(r'[a-z][a-z0-9-]*',suffix):
        raise ValueError('Resource name must belong to one exact builder run')
    name=run+'-'+suffix
    if any(word in name for word in ('gpu','blast')):raise ValueError('Forbidden AWS naming token')
    return name


def instance_name(os):
    if os not in ('windows','linux'):raise ValueError('Unsupported builder OS')
    return PREFIX+os


def validate_aws_names(template):
    """Validate actual named CFN properties, not repo-local OIDC/environment labels.

    This is an additional fail-closed check on our generated templates, not an
    ownership proof or authorization to delete discovered resources.
    """
    properties={'RoleName','ManagedPolicyName','InstanceProfileName','GroupName','Name'}
    for resource in template.get('Resources',{}).values():
        data=resource.get('Properties',{})
        for key in properties & data.keys():
            value=data[key]
            if not isinstance(value,str)or not value.startswith(PREFIX)or any(word in value.casefold()for word in ('gpu','blast')):
                raise ValueError('AWS resource names must use the plain sancus-builder namespace')
        for tag in data.get('Tags',[]):
            key,value=tag['Key'],tag['Value']
            if key=='Name':
                if not isinstance(value,str)or not value.startswith(PREFIX):raise ValueError('Unscoped Name tag')
            elif not key.startswith(TAG_PREFIX):raise ValueError('Unscoped AWS tag key')
            if any(word in str(key).casefold()or word in str(value).casefold()for word in ('gpu','blast')):
                raise ValueError('Forbidden AWS tag token')
    return template

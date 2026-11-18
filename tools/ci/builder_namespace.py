"""AWS ownership namespace, intentionally disjoint from sancus-runner and historical runs."""
import re
import secrets

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
RUN_PATTERN=r'sancus-builder-20[0-9]{6}-[0-9a-f]{7,12}-(?:[0-9a-f]{8}|[0-9a-f]{16})'
# IAM glob syntax, deliberately excludes the standing CI role and boundary.
RUN_GLOB=PREFIX+'20??????-*'
# User decision 2026-11-13: the paid builder qualifies Windows only. Linux real-GPU
# qualification runs on the owned workstation. Ownership is keyed on RUN_TAG, never on
# a display name.
# User decision 2026-11-17: one Windows g4dn.xlarge could not finish inside the fixed
# 180-minute expiry, so each campaign runs TWO Windows T4 instances in parallel. Lane
# `core` keeps the bare `sancus-builder` Name and the WindowsInstance/WindowsExpiry
# logical IDs; lane `remaining` runs the installed-launcher guard, replay and complete
# graphical suite at once, then gates against the controller's hash-bound core handoff.
# `windows-x64` remains the only qualified target; the slots are not targets.
INSTANCE_NAME=PREFIX.rstrip('-')
TARGETS={'windows-x64':'Windows'}
SYSTEMS=tuple(TARGETS.values())
CORE_SLOT='windows-x64-core'
REMAINING_SLOT='windows-x64-remaining'
# Ordered. `expiry` is the schedule-name suffix: run IDs are 48 characters, and EventBridge
# Scheduler and IAM names allow at most 64 (see child_name).
SLOTS={
    CORE_SLOT:{'target':'windows-x64','os':'Windows','lane':'core','logical':'Windows',
               'name':INSTANCE_NAME,'expiry':'windows-expiry'},
    REMAINING_SLOT:{'target':'windows-x64','os':'Windows','lane':'remaining','logical':'WindowsRemaining',
                    'name':PREFIX+'remaining','expiry':'remain-expiry'},
}
LANES={row['lane']:slot for slot,row in SLOTS.items()}
# Core files that `gpu_release_gates.stage()` reads (it reads nothing else from the core
# directory and follows no provenance reference from them). The controller hands exactly
# these bytes from the core lane to the remaining lane, bound by SHA-256.
HANDOFF_FILES=('gpu-campaign-windows-x64-core/campaign-result.json',
               'gpu-campaign-windows-x64-core/high-vulkan/visual-parity-native.json')
HANDOFF_RECEIPT_KEY='payload/core-handoff.json'
HANDOFF_OBJECT_PATTERN=r'payload/core-handoff/object-[0-9]{3}\.bin'
INSTANCE_PATTERN=r'i-[0-9a-f]{8,17}'
NAME_LIMIT=64

def run_id(day,sha,nonce):
    if not re.fullmatch(r'20[0-9]{6}',day)or not re.fullmatch(r'[0-9a-f]{7,12}',sha)or not re.fullmatch(r'(?:[0-9a-f]{8}|[0-9a-f]{16})',nonce):
        raise ValueError('Expected date, short source SHA and unique hexadecimal nonce')
    return PREFIX+day+'-'+sha+'-'+nonce


def new_run_id(day,revision):
    if not re.fullmatch(r'[0-9a-f]{40}',revision):raise ValueError('Expected full source revision')
    # Seven source characters leave room for 64 random nonce bits, the account
    # suffix in a <=63-character bucket name and <=64-character child roles.
    return run_id(day,revision[:7],secrets.token_hex(8))


def resource_name(run,suffix):
    if not re.fullmatch(RUN_PATTERN,run)or not re.fullmatch(r'[a-z][a-z0-9-]*',suffix):
        raise ValueError('Resource name must belong to one exact builder run')
    name=run+'-'+suffix
    if any(word in name for word in ('gpu','blast')):raise ValueError('Forbidden AWS naming token')
    return name


def child_name(run,suffix):
    """A named child (role, schedule) that must fit the 64-character IAM/Scheduler limit."""
    name=resource_name(run,suffix)
    if len(name)>NAME_LIMIT:raise ValueError('Builder child name exceeds 64 characters: '+name)
    return name


def instance_name(os='windows',slot=CORE_SLOT):
    if os!='windows':raise ValueError('Unsupported builder OS; the Linux AWS builder was removed 2026-11-13')
    if slot not in SLOTS:raise ValueError('Unknown builder slot')
    return SLOTS[slot]['name']


def slot_logical(slot,kind):
    """CloudFormation logical ID of one slot's Instance or Expiry resource."""
    if slot not in SLOTS or kind not in ('Instance','Expiry'):raise ValueError('Unknown builder slot resource')
    return SLOTS[slot]['logical']+kind


def slot_for_logical(logical):
    return next((slot for slot in SLOTS if logical==slot_logical(slot,'Instance')),None)


def handoff_object(index):
    return 'payload/core-handoff/object-%03d.bin'%index


def instance_object(instance,name):
    """Guest objects written before the lane is known (both instances share user data)."""
    if not re.fullmatch(INSTANCE_PATTERN,str(instance))or name not in ('bootstrap-ready.json','worker-exit.json','worker-log.bin'):
        raise ValueError('Invalid per-instance guest object')
    return 'results/instances/'+instance+'/'+name


def validate_aws_names(template):
    """Validate actual named CFN properties, not repo-local OIDC/environment labels.

    This is an additional fail-closed check on our generated templates, not an
    ownership proof or authorization to delete discovered resources.
    """
    properties={'RoleName','ManagedPolicyName','InstanceProfileName','GroupName','Name','BucketName'}
    for resource in template.get('Resources',{}).values():
        data=resource.get('Properties',{})
        for key in properties & data.keys():
            value=data[key]
            if not isinstance(value,str)or not value.startswith(PREFIX)or any(word in value.casefold()for word in ('gpu','blast')):
                raise ValueError('AWS resource names must use the plain sancus-builder namespace')
        for tag in data.get('Tags',[]):
            key,value=tag['Key'],tag['Value']
            if key=='Name':
                if not isinstance(value,str)or not(value==INSTANCE_NAME or value.startswith(PREFIX)):raise ValueError('Unscoped Name tag')
            elif not key.startswith(TAG_PREFIX):raise ValueError('Unscoped AWS tag key')
            if any(word in str(key).casefold()or word in str(value).casefold()for word in ('gpu','blast')):
                raise ValueError('Forbidden AWS tag token')
    return template

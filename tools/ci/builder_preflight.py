"""Noncreating live preflight. Unverified observations stop launch readiness."""
import argparse
import json
from pathlib import Path

import builder_arming
import builder_audit
import builder_campaign
import builder_identity
import builder_namespace as names


def check_network(plan,subnets,tables,dns):
    if len(subnets)!=1:raise ValueError('One exact referenced subnet required')
    subnet=subnets[0]
    if (subnet['SubnetId']!=plan['references']['subnet']or subnet['VpcId']!=plan['references']['vpc']or
            subnet['State']!='available'or subnet['AvailableIpAddressCount']<2 or dns.get('Value')is not True):
        raise ValueError('Referenced subnet/DNS cannot support the two disposable instances')
    explicit=[table for table in tables if any(row.get('SubnetId')==subnet['SubnetId']for row in table.get('Associations',[]))]
    selected=explicit or[table for table in tables if any(row.get('Main')for row in table.get('Associations',[]))]
    if len(selected)!=1 or not any(row.get('DestinationCidrBlock')=='0.0.0.0/0'and
                                  row.get('GatewayId','').startswith('igw-')and row.get('State')=='active'
                                  for row in selected[0].get('Routes',[])):
        raise ValueError('No existing direct internet route; never create NAT/VPC changes for QA')
    return {'subnet':subnet['SubnetId'],'vpc':subnet['VpcId'],'routeTable':selected[0]['RouteTableId'],
            'publicIpv4PerInstance':True,'routingModified':False}


def validate_images(plan,images):
    mapping={item['ImageId']:item for item in images}
    if {item['ami']for item in plan['instances']}!=set(builder_identity.AMIS):
        raise ValueError('Campaign AMIs differ from reviewed identity pins')
    for item in plan['instances']:
        image=mapping[item['ami']]
        if image['State']!='available'or image['Architecture']!='x86_64':raise ValueError('Pinned AMI is not available x86_64')
        roots=[row for row in image['BlockDeviceMappings']if row['DeviceName']==image['RootDeviceName']]
        if len(roots)!=1 or item['rootGb']<roots[0]['Ebs']['VolumeSize']:raise ValueError('Encrypted owned root override is smaller than source image')
        if image['RootDeviceName']!='/dev/sda1':raise ValueError('Template root device requires explicit adaptation to this AMI')
    return {'images':[{'id':image['ImageId'],'owner':image['OwnerId'],'rootDevice':image['RootDeviceName']}for image in mapping.values()],
            'existingImagesModified':False}


def inspect(plan,aws):
    identity=aws('sts','get-caller-identity')
    if identity.get('Account')!=names.ACCOUNT:raise ValueError('Wrong preflight AWS account')
    result={'schemaVersion':1,'runId':plan['runId'],'source':plan['payloadSource'],'account':names.ACCOUNT,
            'region':names.REGION,'caller':identity['Arn'],'createsNothing':True,'passed':False,'checks':{},'errors':[]}
    def check(name,operation):
        try:result['checks'][name]=operation()
        except Exception as error:result['errors'].append({'check':name,'error':str(error)})
    def audit():
        data=builder_audit.audit(aws,require_scoped_role=False)
        result['checks']['leftover-discovery']=data
        if not data['passed']:raise ValueError('Earlier owned resources or unverified inventory remain; reconcile exact journals before any new launch')
        return data
    check('leftover-discovery',audit)
    check('network',lambda:check_network(plan,
        aws('ec2','describe-subnets','--subnet-ids',plan['references']['subnet'])['Subnets'],
        aws('ec2','describe-route-tables','--filters','Name=vpc-id,Values='+plan['references']['vpc'])['RouteTables'],
        aws('ec2','describe-vpc-attribute','--vpc-id',plan['references']['vpc'],'--attribute','enableDnsSupport')['EnableDnsSupport']))
    check('images',lambda:validate_images(plan,aws('ec2','describe-images','--image-ids',*builder_identity.AMIS)['Images']))
    def quota():
        available=aws('service-quotas','get-service-quota','--service-code','ec2','--quota-code','L-DB2E81BA')['Quota']['Value']
        running=[item for group in aws('ec2','describe-instances','--filters','Name=instance-state-name,Values=pending,running')['Reservations']
                 for item in group['Instances']if item['InstanceType'].startswith(('g','vt'))and item.get('InstanceLifecycle')!='spot']
        types=sorted({'g4dn.xlarge',*(item['InstanceType']for item in running)})
        cores={item['InstanceType']:item['VCpuInfo']['DefaultVCpus']for item in aws('ec2','describe-instance-types','--instance-types',*types)['InstanceTypes']}
        needed=2*cores['g4dn.xlarge'];used=sum(cores[item['InstanceType']]for item in running)
        if used+needed>available:raise ValueError('Existing G/VT usage plus campaign exceeds the shared quota')
        return {'limit':available,'existingVcpus':used,'requestedVcpus':needed}
    check('quota',quota)
    result['passed']=not result['errors']
    result['scope']='Noncreating account/network/quota/image/leftover preflight only; no desktop, schedule invocation, GPU or game proof.'
    return result


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--state',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True);parser.add_argument('--require-prepared',action='store_true')
    args=parser.parse_args();builder_campaign.configure(args.state);builder_arming.configure(args.state)
    plan=json.loads(builder_campaign.PLAN.read_text())
    if args.require_prepared:builder_arming.verify_prepared(plan)
    def aws(*arguments):return builder_campaign.aws({**plan,'dryRun':True},*arguments)
    result=inspect(plan,aws);args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps({'passed':result['passed'],'createsNothing':True,'errors':result['errors']}))
    if not result['passed']:raise SystemExit(1)

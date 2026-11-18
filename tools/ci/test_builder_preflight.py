import unittest

import builder_preflight as preflight
import builder_identity


class NoInfrastructureChanges(unittest.TestCase):
    def setUp(self):
        self.plan={'references':{'subnet':'subnet-owned-reference','vpc':'vpc-protected'},
                   'instances':[{'os':'Windows','ami':builder_identity.WINDOWS_AMI,'rootGb':80}for _ in range(2)]}
        self.subnets=[{'SubnetId':'subnet-owned-reference','VpcId':'vpc-protected','State':'available','AvailableIpAddressCount':10}]
        self.tables=[{'RouteTableId':'rt-protected','Associations':[{'Main':True}],
                      'Routes':[{'DestinationCidrBlock':'0.0.0.0/0','GatewayId':'igw-existing','State':'active'}]}]

    def test_existing_outbound_route_is_observed_without_reconfiguration(self):
        result=preflight.check_network(self.plan,self.subnets,self.tables,{'Value':True})
        self.assertFalse(result['routingModified']);self.assertTrue(result['publicIpv4PerInstance'])

    def test_missing_route_never_creates_a_nat_or_reuses_a_private_service(self):
        self.tables[0]['Routes']=[]
        with self.assertRaisesRegex(ValueError,'never create NAT'):preflight.check_network(self.plan,self.subnets,self.tables,{'Value':True})

    def test_wrong_subnet_and_disabled_dns_fail_closed(self):
        for dns in ({'Value':False},{}):
            with self.assertRaises(ValueError):preflight.check_network(self.plan,self.subnets,self.tables,dns)

    def test_unreviewed_image_or_too_small_root_is_refused(self):
        # 2026-11-17: one pinned Windows image per lane slot (two instances).
        plan={'instances':[{'ami':builder_identity.WINDOWS_AMI,'rootGb':80}for _ in range(2)]}
        images=[{'ImageId':ami,'State':'available','Architecture':'x86_64','OwnerId':'official',
                 'RootDeviceName':'/dev/sda1','BlockDeviceMappings':[{'DeviceName':'/dev/sda1','Ebs':{'VolumeSize':75}}]}for ami in builder_identity.AMIS]
        self.assertFalse(preflight.validate_images(plan,images)['existingImagesModified'])
        # Windows only since 2026-11-13: the retired (still policy-allowed) Linux image is never launched.
        for instances in ([{'ami':builder_identity.AMIS[0],'rootGb':80}]*2,
                          [{'ami':ami,'rootGb':80}for ami in builder_identity.AMIS],
                          [{'ami':builder_identity.WINDOWS_AMI,'rootGb':80}],
                          [{'ami':builder_identity.WINDOWS_AMI,'rootGb':80}]*3):
            with self.subTest(instances=instances),self.assertRaises(ValueError):preflight.validate_images({'instances':instances},images)
        plan['instances'][1]['rootGb']=20
        with self.assertRaises(ValueError):preflight.validate_images(plan,images)
        plan['instances'][1]['ami']='ami-community'
        with self.assertRaises(ValueError):preflight.validate_images(plan,images)

    def test_live_preflight_reads_and_reserves_both_windows_lane_instances(self):
        calls=[]
        def aws(*args):
            calls.append(args)
            if args[0]=='sts':return {'Account':preflight.names.ACCOUNT,'Arn':'arn:aws:sts::x:assumed-role/r/s'}
            if args[:2]==('ec2','describe-subnets'):return {'Subnets':self.subnets}
            if args[:2]==('ec2','describe-route-tables'):return {'RouteTables':self.tables}
            if args[:2]==('ec2','describe-vpc-attribute'):return {'EnableDnsSupport':{'Value':True}}
            if args[:2]==('ec2','describe-images'):
                return {'Images':[{'ImageId':ami,'State':'available','Architecture':'x86_64','OwnerId':'official','RootDeviceName':'/dev/sda1',
                                   'BlockDeviceMappings':[{'DeviceName':'/dev/sda1','Ebs':{'VolumeSize':30}}]}for ami in args[3:]]}
            if args[0]=='service-quotas':return {'Quota':{'Value':8}}
            if args[:2]==('ec2','describe-instances'):return {'Reservations':[]}
            if args[:2]==('ec2','describe-instance-types'):return {'InstanceTypes':[{'InstanceType':'g4dn.xlarge','VCpuInfo':{'DefaultVCpus':4}}]}
            raise AssertionError(args)
        from unittest.mock import patch
        plan={**self.plan,'runId':'run','payloadSource':'a'*40}
        with patch.object(preflight.builder_audit,'audit',return_value={'passed':True}):
            result=preflight.inspect(plan,aws)
        self.assertTrue(result['passed'],result['errors'])
        self.assertEqual(result['checks']['quota']['requestedVcpus'],8)
        self.assertIn(('ec2','describe-images','--image-ids',builder_identity.WINDOWS_AMI),calls)


if __name__=='__main__':unittest.main()

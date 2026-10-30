import unittest

import builder_preflight as preflight
import builder_identity


class NoInfrastructureChanges(unittest.TestCase):
    def setUp(self):
        self.plan={'references':{'subnet':'subnet-owned-reference','vpc':'vpc-protected'}}
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
        plan={'instances':[{'ami':ami,'rootGb':80}for ami in builder_identity.AMIS]}
        images=[{'ImageId':ami,'State':'available','Architecture':'x86_64','OwnerId':'official',
                 'RootDeviceName':'/dev/sda1','BlockDeviceMappings':[{'DeviceName':'/dev/sda1','Ebs':{'VolumeSize':75}}]}for ami in builder_identity.AMIS]
        self.assertFalse(preflight.validate_images(plan,images)['existingImagesModified'])
        plan['instances'][0]['rootGb']=20
        with self.assertRaises(ValueError):preflight.validate_images(plan,images)
        plan['instances'][0]['ami']='ami-community'
        with self.assertRaises(ValueError):preflight.validate_images(plan,images)


if __name__=='__main__':unittest.main()

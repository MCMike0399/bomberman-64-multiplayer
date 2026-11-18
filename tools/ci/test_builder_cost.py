import unittest

import builder_cost as cost


class ElapsedEstimate(unittest.TestCase):
    def test_observed_lifetime_uses_actual_rate_and_retains_invoice_limitation(self):
        plan={'hourlyRatesUsd':{'Windows':.710},'priorEstimateUsd':4.8}
        rows=[{'event':'CREATED','type':'AWS::EC2::Instance','id':'i-owned','details':{'logicalId':'WindowsInstance'}},
              {'event':'STATUS','id':'i-owned','details':{'launchTime':'2026-10-30T00:00:00Z'}},
              {'event':'STATUS','id':'i-owned','details':{'terminatedObservedAtUtc':'2026-10-30T01:00:00Z'}}]
        result=cost.calculate(plan,rows,[{'type':'AWS::EC2::Instance','id':'i-owned','finalState':'terminated'}],'2026-10-30T01:10:00Z')
        self.assertTrue(result['complete']);self.assertFalse(result['invoice']);self.assertEqual(result['computeUsd'],.71)
        self.assertEqual(result['instances'][0]['secondsUpperBound'],3600)

    def test_both_lane_instances_are_priced_by_their_own_lifetimes(self):
        # 2026-11-17: two parallel Windows lanes; compute is per instance, the rate per OS.
        plan={'hourlyRatesUsd':{'Windows':.710},'priorEstimateUsd':4.8}
        rows=[{'event':'CREATED','type':'AWS::EC2::Instance','id':'i-core','details':{'logicalId':'WindowsInstance'}},
              {'event':'CREATED','type':'AWS::EC2::Instance','id':'i-lane','details':{'logicalId':'WindowsRemainingInstance'}},
              {'event':'STATUS','id':'i-core','details':{'launchTime':'2026-10-30T00:00:00Z'}},
              {'event':'STATUS','id':'i-lane','details':{'launchTime':'2026-10-30T00:00:00Z'}},
              {'event':'STATUS','id':'i-core','details':{'terminatedObservedAtUtc':'2026-10-30T01:00:00Z'}},
              {'event':'STATUS','id':'i-lane','details':{'terminatedObservedAtUtc':'2026-10-30T02:00:00Z'}}]
        final=[{'type':'AWS::EC2::Instance','id':identity,'finalState':'terminated'}for identity in ('i-core','i-lane')]
        result=cost.calculate(plan,rows,final,'2026-10-30T02:10:00Z')
        self.assertTrue(result['complete']);self.assertEqual(result['computeUsd'],2.13)
        self.assertEqual([(row['id'],row['slot'],row['lane'],row['secondsUpperBound'])for row in result['instances']],
                         [('i-core','windows-x64-core','core',3600),('i-lane','windows-x64-remaining','remaining',7200)])
        self.assertFalse(cost.calculate(plan,rows,final[:1],'2026-10-30T02:10:00Z')['complete'])

    def test_unverified_instance_state_never_invents_a_closed_cost(self):
        plan={'hourlyRatesUsd':{'Windows':.710}}
        rows=[{'event':'CREATED','type':'AWS::EC2::Instance','id':'i-owned','details':{'logicalId':'WindowsInstance'}}]
        self.assertFalse(cost.calculate(plan,rows,[],'2026-10-30T01:00:00Z')['complete'])
        # A retired Linux instance or a two-system rate plan is not this reviewed campaign.
        linux=[{**rows[0],'details':{'logicalId':'LinuxInstance'}}]
        terminated=[{'type':'AWS::EC2::Instance','id':'i-owned','finalState':'terminated'}]
        self.assertFalse(cost.calculate(plan,linux,terminated,'2026-10-30T01:00:00Z')['complete'])
        self.assertFalse(cost.calculate({'hourlyRatesUsd':{'Linux':.526,'Windows':.710}},rows,terminated,'2026-10-30T01:00:00Z')['complete'])

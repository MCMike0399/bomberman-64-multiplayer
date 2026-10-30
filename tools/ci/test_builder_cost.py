import unittest

import builder_cost as cost


class ElapsedEstimate(unittest.TestCase):
    def test_observed_lifetime_uses_actual_rate_and_retains_invoice_limitation(self):
        plan={'hourlyRatesUsd':{'Linux':.526,'Windows':.710},'priorEstimateUsd':4.8}
        rows=[{'event':'CREATED','type':'AWS::EC2::Instance','id':'i-owned','details':{'logicalId':'LinuxInstance'}},
              {'event':'STATUS','id':'i-owned','details':{'launchTime':'2026-10-30T00:00:00Z'}},
              {'event':'STATUS','id':'i-owned','details':{'terminatedObservedAtUtc':'2026-10-30T01:00:00Z'}}]
        result=cost.calculate(plan,rows,[{'type':'AWS::EC2::Instance','id':'i-owned','finalState':'terminated'}],'2026-10-30T01:10:00Z')
        self.assertTrue(result['complete']);self.assertFalse(result['invoice']);self.assertEqual(result['computeUsd'],.526)
        self.assertEqual(result['instances'][0]['secondsUpperBound'],3600)

    def test_unverified_instance_state_never_invents_a_closed_cost(self):
        plan={'hourlyRatesUsd':{'Linux':.526,'Windows':.710}}
        rows=[{'event':'CREATED','type':'AWS::EC2::Instance','id':'i-owned','details':{'logicalId':'LinuxInstance'}}]
        self.assertFalse(cost.calculate(plan,rows,[],'2026-10-30T01:00:00Z')['complete'])

import copy
import datetime
import unittest

import builder_approval as approval


class DisclosedPaidPlan(unittest.TestCase):
    def setUp(self):
        self.now=datetime.datetime(2026,10,30,tzinfo=datetime.timezone.utc)
        self.expiry=(self.now+datetime.timedelta(minutes=160)).isoformat()
        self.spending={'budgetUsd':25,'closedEstimateUsd':4.80}
        self.data=approval.build('a'*40,'123',self.expiry,self.spending)
        self.plan={'payloadSource':'a'*40,'verificationRunId':'123','fixedCampaignExpiryUtc':self.expiry,
                   **{k:self.data[k]for k in ('account','region','instances','priorEstimateUsd','budgetUsd')},
                   'hourlyRatesUsd':dict(approval.RATE_CEILINGS),'estimatedCampaignUsd':5.29}

    def test_hold_defaults_closed_without_affecting_release_kind(self):
        for value in (None,'','true','FALSE','garbage'):
            with self.subTest(value=value),self.assertRaisesRegex(ValueError,'hold'):approval.require_unheld(value)
        approval.require_unheld('false')

    def test_summary_discloses_exact_inputs_cost_and_remaining_budget(self):
        text=approval.summary(self.data)
        for value in ('a'*40,'123','g4dn.xlarge',*approval.builder_identity.AMIS,self.expiry,'$5.29','$20.20','$14.91','Instance count: 2'):
            self.assertIn(value,text)
        self.assertEqual(approval.validate_prepared(self.data,self.plan,self.spending,self.now.timestamp()),self.data)

    def test_late_approval_preserves_expiry_and_refuses_before_allocation(self):
        with self.assertRaisesRegex(ValueError,'Late approval'):
            approval.validate(self.data,'a'*40,'123',self.expiry,self.spending,(self.now+datetime.timedelta(minutes=41)).timestamp())
        self.assertEqual(self.data['fixedCampaignExpiryUtc'],self.expiry)

    def test_source_verification_ami_price_or_budget_tampering_is_refused(self):
        for key,value in [('source','b'*40),('verificationRunId','456'),('estimatedCampaignUsd',.01),('budgetUsd',30)]:
            changed={**self.data,key:value}
            with self.subTest(key=key),self.assertRaises(ValueError):approval.validate(changed,'a'*40,'123',self.expiry,self.spending,self.now.timestamp())
        changed=copy.deepcopy(self.plan);changed['instances'][0]['ami']='ami-other'
        with self.assertRaises(ValueError):approval.validate_prepared(self.data,changed,self.spending,self.now.timestamp())
        changed=copy.deepcopy(self.plan);changed['hourlyRatesUsd']['Windows']=.711
        with self.assertRaisesRegex(ValueError,'price'):approval.validate_prepared(self.data,changed,self.spending,self.now.timestamp())
        with self.assertRaises(ValueError):approval.build('a'*40,'123',self.expiry,{'budgetUsd':25,'closedEstimateUsd':24})

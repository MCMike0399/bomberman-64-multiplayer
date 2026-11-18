import copy
import datetime
import unittest
from unittest import mock

import builder_approval as approval


class DisclosedPaidPlan(unittest.TestCase):
    def setUp(self):
        self.now=datetime.datetime(2026,10,30,tzinfo=datetime.timezone.utc)
        self.expiry=(self.now+datetime.timedelta(minutes=160)).isoformat()
        self.spending={'budgetUsd':25,'closedEstimateUsd':4.80}
        self.data=approval.build('a'*40,'123',self.expiry,self.spending)
        self.plan={'payloadSource':'a'*40,'verificationRunId':'123','fixedCampaignExpiryUtc':self.expiry,
                   **{k:self.data[k]for k in ('account','region','instances','priorEstimateUsd','budgetUsd')},
                   'hourlyRatesUsd':dict(approval.RATE_CEILINGS),'estimatedCampaignUsd':3.67}

    def test_hold_defaults_closed_without_affecting_release_kind(self):
        for value in (None,'','true','FALSE','garbage'):
            with self.subTest(value=value),self.assertRaisesRegex(ValueError,'hold'):approval.require_unheld(value)
        approval.require_unheld('false')

    def test_summary_discloses_exact_inputs_cost_and_remaining_budget(self):
        text=approval.summary(self.data)
        for value in ('a'*40,'123','g4dn.xlarge',approval.builder_identity.WINDOWS_AMI,self.expiry,'$3.67','$20.20','$16.53','Instance count: 1'):
            self.assertIn(value,text)
        # Windows only since 2026-11-13: the retired Linux image is never disclosed or launched.
        self.assertNotIn(approval.builder_identity.AMIS[0],text);self.assertNotIn('Linux',text)
        self.assertEqual(self.data['instances'],[{'os':'Windows','type':'g4dn.xlarge','ami':approval.builder_identity.AMIS[1],'rootGb':80}])
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

    def test_cumulative_limit_accepts_exactly_two_hundred_and_rejects_above(self):
        spending = {'budgetUsd': 200, 'closedEstimateUsd': 196.33}
        plan = approval.build('a' * 40, '123', self.expiry, spending)
        self.assertEqual(plan['remainingBudgetAfterUsd'], 0)
        for invalid in ({'budgetUsd': 200.01, 'closedEstimateUsd': 0},
                        {'budgetUsd': 200, 'closedEstimateUsd': 196.34}):
            with self.subTest(spending=invalid), self.assertRaises(ValueError):
                approval.build('a' * 40, '123', self.expiry, invalid)

    def test_twenty_dollar_run_limit_accepts_the_boundary_and_refuses_above(self):
        # Synthetic reviewed-price inputs exercise the dollar policy only.
        # Production hourly ceilings and instance/lifetime limits stay unchanged.
        self.assertEqual((approval.PER_RUN_CAP_USD, approval.CUMULATIVE_CAP_USD), (20, 200))
        spending = {'budgetUsd': 200, 'closedEstimateUsd': 0}
        with mock.patch.dict(approval.RATE_CEILINGS, {'Windows': 6.154}, clear=True):
            plan = approval.build('a' * 40, '123', self.expiry, spending)
            self.assertEqual(plan['estimatedCampaignUsd'], 20)
        with mock.patch.dict(approval.RATE_CEILINGS, {'Windows': 6.155}, clear=True):
            with self.assertRaisesRegex(ValueError, 'cap'):
                approval.build('a' * 40, '123', self.expiry, spending)

    def test_current_summary_discloses_new_limits_and_actual_prior_spending(self):
        plan = approval.build('a' * 40, '123', self.expiry, {'budgetUsd': 200, 'closedEstimateUsd': 4.80})
        text = approval.summary(plan)
        self.assertIn('refuse above $20', text)
        self.assertIn('$4.80 / $200.00', text)
        self.assertIn('without a human click', text)
        self.assertEqual(plan['remainingBudgetBeforeUsd'], 195.20)
        self.assertEqual(plan['remainingBudgetAfterUsd'], 191.53)

    def test_one_windows_instance_costs_less_than_the_retired_two_instance_campaign(self):
        # The legacy two-instance formula (Linux .526 + Windows .710, 155 GiB, two IPv4) gave $5.29.
        self.assertEqual(approval.estimate(approval.RATE_CEILINGS), 3.67)
        self.assertEqual(set(approval.RATE_CEILINGS), {'Windows'})
        self.assertEqual(len(approval.instances()), 1)

    def test_checked_in_ledger_is_consistent_and_unrecorded_receipts_are_refused(self):
        import json, tempfile
        from pathlib import Path
        data = approval.ledger()
        self.assertEqual((data['budgetUsd'], data['perRunCapUsd'], data['closedEstimateUsd']), (200, 20, 40.1))
        self.assertEqual([(row['workflowRunId'], row['estimateUsd']) for row in data['recordedRuns']],
                         [('41136197647', 0.0), ('41151908001', 0.71), ('41178169234', 3.4), ('41198567742', 1.18), ('41214884627', 3.88), ('41240998617', 3.91), ('41265927585', 3.85), ('41285894565', 3.72), ('41304300722', 4.23), ('41370107447', 4.23), ('41403605524', 4.13), ('44012050675', 1.03), ('44199687411', 1.03)])
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'ledger.json'
            for change in ({'closedEstimateUsd': 4.8, 'recordedRuns': [{'workflowRunId': '7', 'estimateUsd': 5.1}]},
                           {'recordedRuns': [{'workflowRunId': '7'}]},
                           {'recordedRuns': [{'estimateUsd': 1}], 'closedEstimateUsd': 5.8}):
                path.write_text(json.dumps({**data, **change}))
                with self.subTest(change=change), self.assertRaises(ValueError):
                    approval.ledger(path)
            path.write_text(json.dumps({**data, 'closedEstimateUsd': 9.9, 'recordedRuns': [{'workflowRunId': '7', 'estimateUsd': 5.1}]}))
            self.assertEqual(approval.ledger(path)['closedEstimateUsd'], 9.9)

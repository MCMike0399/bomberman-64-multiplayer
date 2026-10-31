import datetime
import json
import unittest

import builder_timing as timing


class CredentialEnvelope(unittest.TestCase):
    def setUp(self):
        self.started=datetime.datetime(2026,10,30,tzinfo=datetime.timezone.utc);self.now=self.started.timestamp()
        self.plan={'fixedCampaignExpiryUtc':(self.started+datetime.timedelta(minutes=160)).isoformat(),
                   'timing':{'jobStartedAtUtc':self.started.isoformat(),'jobTimeoutSeconds':210*60,
                             'jobMarginSeconds':900,'cleanupBudgetSeconds':1200}}
    def expiry(self,minutes):return(self.started+datetime.timedelta(minutes=minutes)).isoformat()

    def test_fixed_expiry_plus_cleanup_fits_the_actual_job_deadline(self):
        timing.envelope(self.plan)
        with self.assertRaisesRegex(ValueError,'does not fit'):
            timing.envelope({**self.plan,'fixedCampaignExpiryUtc':self.expiry(190)})

    def test_selected_175_minute_expiry_is_the_exact_job_envelope_boundary(self):
        # Reviewer 2026-10-31: +175 min (was 160) = job timeout 210 - cleanup 20 - margin 15.
        import builder_job
        self.assertEqual(builder_job.CAMPAIGN_SECONDS+timing.CLEANUP_SECONDS+timing.MARGIN_SECONDS,builder_job.JOB_TIMEOUT_SECONDS)
        timing.envelope({**self.plan,'fixedCampaignExpiryUtc':self.expiry(175)})
        late=(self.started+datetime.timedelta(minutes=175,seconds=1)).isoformat()
        with self.assertRaisesRegex(ValueError,'does not fit'):timing.envelope({**self.plan,'fixedCampaignExpiryUtc':late})
        self.assertLessEqual(builder_job.CAMPAIGN_SECONDS,10800)  # <= 3 h lifetime rule
        source=open(__import__('builder_trigger').__file__).read()
        self.assertIn('timedelta(minutes=175)',source)

    def test_each_phase_refuses_insufficient_remaining_session_time(self):
        for phase in timing.PHASE_SECONDS:
            if phase in ('terminate','retain','cleanup'):continue
            with self.subTest(phase=phase),self.assertRaisesRegex(ValueError,'Credential lifetime'):
                timing.grant(self.plan,phase,now=self.now,expiration=self.expiry(1))

    def test_actual_action_date_output_can_be_json_quoted(self):
        result=timing.grant(self.plan,'launch',now=self.now,expiration=json.dumps(self.expiry(180)))
        self.assertEqual(result['budgetSeconds'],1200)

    def test_fresh_short_cleanup_session_has_full_headroom(self):
        result=timing.grant(self.plan,'cleanup',now=self.now+160*60,expiration=self.expiry(220))
        self.assertEqual(result['budgetSeconds'],1200);self.assertFalse(result['cleanupCredentialShortfall'])

    def test_late_failure_never_skips_available_ownership_checked_cleanup(self):
        bad={**self.plan,'fixedCampaignExpiryUtc':self.expiry(200)}
        result=timing.grant(bad,'cleanup',now=self.now+211*60,expiration=self.expiry(271))
        self.assertEqual(result['budgetSeconds'],1200);self.assertIsNotNone(result['planEnvelopeError'])

    def test_bootstrap_window_fits_the_latest_permitted_launch(self):
        # Latest CreateStack allowed by minimumLaunchHeadroomSeconds, then a full
        # launch grant. The 900 s bootstrap deadline still precedes the
        # controller stop, and the core grant covers bootstrap plus the guest's
        # 2400 s core step with its own 20-minute expiry headroom.
        expiry=self.started.timestamp()+160*60
        submitted=expiry-timing.MINIMUM_LAUNCH_HEADROOM_SECONDS
        launched=submitted+timing.PHASE_SECONDS['launch']
        bootstrap_deadline=min(launched+timing.BOOTSTRAP_SECONDS,expiry-timing.CONTROLLER_STOP_SECONDS)
        self.assertEqual(bootstrap_deadline,launched+900)
        core=timing.grant(self.plan,'core',now=launched,expiration=self.expiry(400))['budgetSeconds']
        self.assertEqual(core,4800)
        self.assertGreaterEqual(core,timing.BOOTSTRAP_SECONDS+2400)
        self.assertEqual(expiry-timing.CONTROLLER_STOP_SECONDS-bootstrap_deadline,3900)
        self.assertGreaterEqual(expiry-bootstrap_deadline,20*60+2400)
        # Nominal: launch starting at job start leaves the full 90-minute core grant.
        self.assertEqual(timing.grant(self.plan,'core',now=self.now+timing.PHASE_SECONDS['launch'],
                                      expiration=self.expiry(400))['budgetSeconds'],5400)
        timing.envelope(self.plan)

    def test_missing_or_expired_session_is_not_invented(self):
        for expiration in ('',self.expiry(-1)):
            with self.subTest(expiration=expiration),self.assertRaises(ValueError):
                timing.grant(self.plan,'cleanup',now=self.now,expiration=expiration)

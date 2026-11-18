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

    def test_selected_180_minute_expiry_is_the_exact_job_envelope_boundary(self):
        # Reviewer 2026-10-31: +175 min (was 160); 2026-11-01: +180 min (the 3 h campaign rule) = job timeout
        # 215 - cleanup 20 - margin 15, for the Linux role-popup repeats.
        import builder_job
        self.assertEqual(builder_job.CAMPAIGN_SECONDS+timing.CLEANUP_SECONDS+timing.MARGIN_SECONDS,builder_job.JOB_TIMEOUT_SECONDS)
        plan={**self.plan,'timing':{**self.plan['timing'],'jobTimeoutSeconds':builder_job.JOB_TIMEOUT_SECONDS}}
        timing.envelope({**plan,'fixedCampaignExpiryUtc':self.expiry(180)})
        late=(self.started+datetime.timedelta(minutes=180,seconds=1)).isoformat()
        with self.assertRaisesRegex(ValueError,'does not fit'):timing.envelope({**plan,'fixedCampaignExpiryUtc':late})
        self.assertLessEqual(builder_job.CAMPAIGN_SECONDS,10800)  # <= 3 h lifetime rule
        source=open(__import__('builder_trigger').__file__).read()
        self.assertIn('timedelta(minutes=180)',source)

    def test_final_phase_is_capped_by_expiry_and_covered_by_the_3_hour_collection_session(self):
        # The collection role session is 3 h from job start and is re-assumed only before cleanup.
        # With a 180-minute expiry the final grant ends at expiry - 20 min, 18 min before that session
        # (less its 2-minute margin), wherever the final phase starts.
        import builder_job
        plan={**self.plan,'fixedCampaignExpiryUtc':self.expiry(180),
              'timing':{**self.plan['timing'],'jobTimeoutSeconds':builder_job.JOB_TIMEOUT_SECONDS}}
        for start_minutes in (60,66.5,80):
            now=self.now+start_minutes*60
            budget=timing.grant(plan,'final',now=now,expiration=self.expiry(180))['budgetSeconds']
            self.assertEqual(budget,min(timing.PHASE_SECONDS['final'],int((160-start_minutes)*60)))
        # Run 41304300722's final phase started at +66.5 min: the grant is now 5610 s (was 5300 s).
        self.assertEqual(timing.grant(plan,'final',now=self.now+66.5*60,expiration=self.expiry(180))['budgetSeconds'],5610)

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

    def test_bootstrap_window_and_both_lanes_fit_the_latest_permitted_launch(self):
        # Latest CreateStack allowed by minimumLaunchHeadroomSeconds, then a full launch
        # grant. The 1,200 s bootstrap deadline precedes the controller stop, and the core
        # grant covers bootstrap plus the core lane's 2400 s step with its own headroom.
        import builder_guest,gpu_campaign
        expiry=self.started.timestamp()+160*60
        submitted=expiry-timing.MINIMUM_LAUNCH_HEADROOM_SECONDS
        launched=submitted+timing.PHASE_SECONDS['launch']
        bootstrap_deadline=min(launched+timing.BOOTSTRAP_SECONDS,expiry-timing.CONTROLLER_STOP_SECONDS)
        self.assertEqual(bootstrap_deadline,launched+1200)
        core=timing.grant(self.plan,'core',now=launched,expiration=self.expiry(400))['budgetSeconds']
        self.assertEqual(core,5400)
        self.assertGreaterEqual(core,timing.BOOTSTRAP_SECONDS+builder_guest.CORE_STEP_SECONDS)
        self.assertGreaterEqual(expiry-bootstrap_deadline,20*60+builder_guest.CORE_STEP_SECONDS)
        # Remaining lane (2026-11-17): replay + the 4,800 s Windows suite + inspection is
        # admitted only with the handoff/gate/publish reserve still ahead, so it must start
        # by E-132 min. Nominal intent-to-ready (~25 min) plus desktop/engine/launcher
        # (~4 min) fits the 9,900 s launch boundary; 9,000 s and 7,200 s could not.
        self.assertEqual(builder_guest.REMAINING_STEP_SECONDS,300+gpu_campaign.WINDOWS_GRAPHICAL_SUITE_BUDGET_SECONDS+120)
        latest_remaining=expiry-20*60-builder_guest.HANDOFF_PUBLISH_RESERVE_SECONDS-builder_guest.REMAINING_STEP_SECONDS
        self.assertEqual(expiry-latest_remaining,7920)
        self.assertLessEqual(submitted+25*60+4*60,latest_remaining)
        for former in (7200,9000):self.assertGreater(expiry-former+25*60+4*60,latest_remaining)
        # The suite step inside gpu_campaign keeps its own E-now >= 20 min + limit check.
        self.assertLessEqual(20*60+gpu_campaign.WINDOWS_GRAPHICAL_SUITE_BUDGET_SECONDS+300,expiry-latest_remaining)
        # The handoff wait ends early enough for the gate step's own headroom check.
        self.assertGreaterEqual(builder_guest.HANDOFF_PUBLISH_RESERVE_SECONDS,builder_guest.GATE_STEP_SECONDS)
        # Nominal: launch starting at job start leaves the full 90-minute core grant.
        self.assertEqual(timing.grant(self.plan,'core',now=self.now+timing.PHASE_SECONDS['launch'],
                                      expiration=self.expiry(400))['budgetSeconds'],5400)
        timing.envelope(self.plan)

    def test_missing_or_expired_session_is_not_invented(self):
        for expiration in ('',self.expiry(-1)):
            with self.subTest(expiration=expiration),self.assertRaises(ValueError):
                timing.grant(self.plan,'cleanup',now=self.now,expiration=expiration)

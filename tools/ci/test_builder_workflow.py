from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]


class HostedCleanupPlacement(unittest.TestCase):
    def setUp(self):self.source=(ROOT/'.github/workflows/builder-controller.yml').read_text()

    def block(self,name):
        return self.source.split('      - name: '+name,1)[1].split('      - ',1)[0]

    def test_cleanup_reassumption_and_audit_are_always_in_the_campaign_job(self):
        campaign=self.source.split('  campaign:',1)[1].split('  report:',1)[0]
        self.assertIn('name: gpu-qualification',campaign)
        self.assertIn('actions/runs/{1}#summary',campaign)
        for name in ('Re-assume fresh credentials immediately before cleanup','Record the real cleanup credential deadline',
                     'Always perform owned cleanup and its audit in this same job'):
            block=self.block(name);self.assertIn('if: always()',block)
        self.assertIn('role-duration-seconds: 3600',self.block('Re-assume fresh credentials immediately before cleanup'))
        self.assertNotIn('environment:',self.source.split('  report:',1)[1])

    def test_ordinary_phases_stop_on_failure_and_cancel(self):
        for name in ('Prepare exact trusted artifacts before allocation','Persist the exact creation intent without allocating',
                     'Launch and arm only the reviewed owned stack','Collect both immutable High checkpoints first',
                     'Acknowledge durable core evidence','Collect the remaining exact-byte machine evidence'):
            with self.subTest(name=name):self.assertIn('success() && !cancelled()',self.block(name))

    def test_job_and_credential_budgets_are_explicit(self):
        self.assertIn('|| 210',self.source);self.assertIn('role-duration-seconds: 10800',self.source)
        self.assertIn('steps.cleanup_auth.outputs.aws-expiration',self.source)
        self.assertIn('builder_phase.py --state artifacts/builder-state cleanup',self.source)

    def test_intent_and_core_uploads_precede_the_operations_they_authorize(self):
        self.assertLess(self.source.index('Durably retain the intent'),self.source.index('Launch and arm'))
        self.assertLess(self.source.index('Retain core evidence before'),self.source.index('Acknowledge durable'))

    def test_cancel_path_terminates_before_slow_evidence_and_purges_afterward(self):
        names=('Re-assume fresh credentials immediately before cleanup',
               'Request owned instance termination before slow evidence work',
               'Retain available immutable evidence after stopping writers',
               'Preserve all available game evidence before teardown',
               'Always perform owned cleanup and its audit in this same job')
        positions=[self.source.index(name)for name in names]
        self.assertEqual(positions,sorted(positions))
        self.assertIn('if: always()',self.block(names[1]))

    def test_plan_is_disclosed_before_the_protected_paid_job(self):
        trigger=(ROOT/'.github/workflows/gpu-qualification.yml').read_text()
        self.assertLess(trigger.index('Retain the paid plan before any approval prompt'),trigger.index('  controlled:'))
        self.assertIn('approval-plan: ${{ needs.select.outputs.approval-plan }}',trigger)
        self.assertIn('SANCUS_BUILDER_PAID_HOLD: ${{ vars.SANCUS_BUILDER_PAID_HOLD }}',trigger)
        self.assertIn('PAID up to ${0}',self.source)
        self.assertIn('FREE rehearsal - no allocation',self.source)
        self.assertLess(self.source.index('Refuse held, changed or late paid approvals'),self.source.index('Assume the scoped role for preparation'))

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
        self.assertIn('|| 215',self.source);self.assertIn('role-duration-seconds: 10800',self.source)
        self.assertIn('steps.cleanup_auth.outputs.aws-expiration',self.source)
        self.assertIn('builder_phase.py --state artifacts/builder-state cleanup',self.source)

    def test_workflow_clock_branches_are_accepted_and_match_platform_envelopes(self):
        import json
        import os
        import re
        import runpy
        import tempfile
        from unittest.mock import patch
        import builder_job

        clock=self.block('Record the actual hosted job clock')
        branches=re.search(r'--timeout-seconds "\$\{\{ (.*?) \}\}"',clock).group(1)
        platform=self.source.split('  campaign:',1)[1].split('timeout-minutes: ${{ ',1)[1].split(' }}',1)[0]
        pattern=(r"inputs.mode == 'permissions-smoke' && (\d+) \|\| "
                 r"inputs.mode == 'rehearse' && inputs.rehearsal-fault == 'job-timeout' && (\d+) \|\| (\d+)")
        seconds=tuple(map(int,re.fullmatch(pattern,branches).groups()))
        minutes=tuple(map(int,re.fullmatch(pattern,platform).groups()))
        self.assertEqual(seconds,(builder_job.SMOKE_TIMEOUT_SECONDS,180,builder_job.JOB_TIMEOUT_SECONDS))
        self.assertEqual(seconds,tuple(value*60 for value in minutes))
        self.assertEqual(seconds[-1],215*60)

        # Execute the real CLI for every selected branch: an unreviewed value
        # must fail here before a real workflow can reach credentials or AWS.
        job={'id':2,'status':'in_progress','runner_name':'fixture','started_at':'2026-11-01T00:00:00Z'}
        for mode,value in zip(('smoke','rehearse-timeout','default'),seconds):
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as directory:
                output=Path(directory)/'job-clock.json'
                args=['builder_job.py','--output',str(output),'--timeout-seconds',str(value)]
                env={'GITHUB_RUN_ID':'123','GITHUB_RUN_ATTEMPT':'1','GITHUB_JOB':'campaign','RUNNER_NAME':'fixture'}
                with patch('sys.argv',args),patch.dict(os.environ,env),patch(
                        'subprocess.check_output',return_value=json.dumps({'jobs':[job]})):
                    runpy.run_path(str(Path(builder_job.__file__)),run_name='__main__')
                self.assertEqual(json.loads(output.read_text())['jobTimeoutSeconds'],value)

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
        self.assertLess(trigger.index('Retain the paid plan and its qualification key before allocation'),trigger.index('  controlled:'))
        self.assertIn('approval-plan: ${{ needs.select.outputs.approval-plan }}',trigger)
        self.assertIn('SANCUS_BUILDER_PAID_HOLD: ${{ vars.SANCUS_BUILDER_PAID_HOLD }}',trigger)
        self.assertIn('PAID up to ${0}',self.source)
        self.assertIn('FREE rehearsal - no allocation',self.source)
        self.assertLess(self.source.index('Refuse held, changed or late paid approvals'),self.source.index('Assume the scoped role for preparation'))

    def test_every_instance_holding_step_has_a_platform_timeout_near_its_phase_grant(self):
        # A GitHub run cancel did not signal the running step (probes 41109335883/41111011040),
        # while a timeout delivered SIGINT and always-cleanup ran (41112729113). Each step that
        # runs while instances exist therefore needs its own platform-enforced bound.
        import re
        import builder_timing
        steps = self.source.split('      - name: ')
        phases = {}
        for step in steps:
            found = re.search(r'builder_phase\.py --state artifacts/builder-state ([a-z-]+)', step)
            if found and found.group(1) in ('launch', 'core', 'acknowledge', 'final'):
                limit = re.search(r'\n\s+timeout-minutes: (\d+)\n', step)
                self.assertIsNotNone(limit, found.group(1))
                phases[found.group(1)] = int(limit.group(1))
        self.assertEqual(set(phases), {'launch', 'core', 'acknowledge', 'final'})
        for phase, minutes in phases.items():
            grant = builder_timing.PHASE_SECONDS[phase] / 60
            self.assertGreaterEqual(minutes, grant, phase)
            self.assertLessEqual(minutes, grant + 2, phase)

    def test_permissions_smoke_is_a_dispatchable_free_mode_in_the_protected_campaign_job(self):
        trigger=(ROOT/'.github/workflows/gpu-qualification.yml').read_text()
        self.assertIn('options: [audit, rehearse, permissions-smoke, qualify]',trigger)
        campaign=self.source.split('  campaign:',1)[1].split('  report:',1)[0]
        self.assertIn("inputs.mode != 'audit'",campaign.split('runs-on:',1)[0])
        self.assertIn('name: gpu-qualification',campaign)
        self.assertIn("inputs.mode == 'permissions-smoke' && 30 ||",campaign)
        self.assertIn("inputs.mode == 'permissions-smoke' && 1800 ||",campaign)
        self.assertIn('FREE permissions smoke - dry-run launches, no instances',campaign)
        # Same OIDC assumption step as rehearse; no separate credential path.
        self.assertEqual(campaign.count('role-to-assume: arn:aws:iam::524329886851:role/sancus-builder-ci'),2)

    def test_permissions_smoke_steps_are_fenced_bounded_and_never_paid(self):
        smoke_steps=('Fence every permissions-smoke AWS CLI call','Prepare the FREE permissions smoke',
                     'Durably retain the smoke intent','Create the instance-free stack')
        for name in smoke_steps:
            with self.subTest(name=name):
                block=self.block(name)
                self.assertIn("success() && !cancelled() && inputs.mode == 'permissions-smoke'",block)
                if name!=smoke_steps[0]:self.assertRegex(block,r'timeout-minutes: (2|5|12)\n')
        self.assertIn('--smoke',self.block(smoke_steps[0]))
        self.assertIn('builder_smoke.py --state artifacts/builder-state prepare',self.block(smoke_steps[1]))
        self.assertIn('name: builder-smoke-intent-${{ github.run_id }}-${{ github.run_attempt }}',self.block(smoke_steps[2]))
        self.assertIn('builder_smoke.py --state artifacts/builder-state run',self.block(smoke_steps[3]))
        positions=[self.source.index(name)for name in (*smoke_steps,'Re-assume fresh credentials immediately before cleanup',
                                                         'Always perform owned cleanup and its audit in this same job')]
        self.assertEqual(positions,sorted(positions))
        for name in ('Refuse held, changed or late paid approvals','Persist the exact creation intent without allocating',
                     'Durably retain the intent before any allocation','Launch and arm only the reviewed owned stack'):
            with self.subTest(paid=name):self.assertIn("inputs.mode == 'qualify'",self.block(name))
        for name in ('Prepare exact trusted artifacts before allocation','Noncreating prepared-input and phase rehearsal'):
            with self.subTest(skipped=name):self.assertIn("inputs.mode != 'permissions-smoke'",self.block(name))
        self.assertNotIn('builder-intent-',''.join(self.block(name)for name in smoke_steps))

    def test_permissions_smoke_uses_the_paid_exact_id_cleanup_and_the_outcome_report(self):
        for name in ('Request owned instance termination before slow evidence work','Always perform owned cleanup and its audit in this same job'):
            with self.subTest(name=name):
                block=self.block(name)
                self.assertIn('if: always()',block)
                self.assertIn('[ "$MODE" = permissions-smoke ]',block)
                self.assertIn("inputs.mode == 'permissions-smoke' && steps.smoke.outcome || steps.launch.outcome",block)
        self.assertIn('builder_phase.py --state artifacts/builder-state cleanup',self.block('Always perform owned cleanup and its audit in this same job'))
        record=self.block('Retain the distinct permissions-smoke record')
        self.assertIn("always() && inputs.mode == 'permissions-smoke'",record)
        self.assertIn('name: builder-smoke-${{ github.run_id }}-${{ github.run_attempt }}',record)
        report=self.source.split('  report:',1)[1]
        self.assertIn("if: always() && inputs.mode != 'audit'",report)
        self.assertIn('pattern: builder-*-${{ github.run_id }}-${{ github.run_attempt }}*',report)
        self.assertIn('name: builder-outcome-${{ github.run_id }}-${{ github.run_attempt }}',report)
        import builder_guard
        for artifact in ('builder-smoke-intent-1-1','builder-smoke-1-1','builder-outcome-1-1'):
            self.assertTrue(artifact.startswith(builder_guard.BUILDER_ARTIFACTS))
            self.assertFalse(artifact.startswith(('builder-intent-',builder_guard.PLAN_ARTIFACT)))

    def test_native_contract_upload_never_races_owned_uninstaller_files(self):
        source=(ROOT/'.github/workflows/ci.yml').read_text().split('  native-contracts:',1)[1].split('  native-cohort:',1)[0]
        root='artifacts/native-contracts/${{ matrix.target }}'
        for phase in ('replay','inputs'):
            self.assertIn('!'+root+'/'+phase+'/installed/**',source)
        self.assertIn(root+'/',source)

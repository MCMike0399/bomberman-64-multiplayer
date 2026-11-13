"""Workstation GPU qualification workflow and selection contracts."""
from pathlib import Path
import unittest

import gpu_select

ROOT = Path(__file__).resolve().parents[2]
SHA = 'a' * 40


def verification(**changes):
    run = {'head_repository': {'full_name': gpu_select.REPOSITORY}, 'head_sha': SHA, 'head_branch': 'main',
           'path': '.github/workflows/ci.yml', 'event': 'push', 'status': 'completed', 'conclusion': 'success'}
    run.update(changes)
    return run


def artifacts():
    return [{'name': name, 'expired': False} for name in gpu_select.builder_trigger.REQUIRED_ARTIFACTS]


class WorkstationWorkflow(unittest.TestCase):
    """Hybrid contract (2026-11-13): AWS only through the reusable controller for Windows,
    the owned workstation only for Linux, one run producing both promotion artifacts."""

    def setUp(self):
        self.source = (ROOT / '.github/workflows/gpu-qualification.yml').read_text()

    def job(self, name, following):
        return self.source.split('  ' + name + ':\n', 1)[1].split('  ' + following + ':\n', 1)[0]

    def test_aws_is_reachable_only_through_the_reusable_windows_controller(self):
        workflows = ROOT / '.github/workflows'
        self.assertTrue((workflows / 'builder-controller.yml').is_file())
        self.assertFalse((workflows / 'gpu-once.yml').exists())
        controlled = self.job('controlled', 'native')
        self.assertIn('uses: ./.github/workflows/builder-controller.yml', controlled)
        self.assertIn('id-token: write', controlled)
        self.assertEqual(self.source.count('id-token: write'), 1)
        outside = self.source.replace(controlled, '')
        for word in ('id-token', 'aws-actions', 'role-to-assume'):
            self.assertNotIn(word, outside)
        # The deployed OIDC trust pins the reusable workflow path and environment.
        import builder_identity
        self.assertIn('.github/workflows/builder-controller.yml@refs/heads/main', builder_identity.OIDC_SUBJECT)
        controller = (workflows / 'builder-controller.yml').read_text()
        self.assertIn('name: gpu-qualification', controller)
        self.assertNotIn('self-hosted', controller)
        self.assertNotIn('linux-x64', controller)

    def test_native_workstation_job_is_linux_only_on_the_real_adapter(self):
        native = self.job('native', 'finalize')
        self.assertIn('runs-on: [self-hosted, blast-gpu, Linux, X64]', native)
        self.assertIn('EXPECTED_ADAPTER: RTX 2080 SUPER', native)
        self.assertIn('BLAST_GPU_HOST: workstation', native)
        self.assertIn('name: Workstation GPU linux-x64', native)
        self.assertIn('TARGET: linux-x64', native)
        self.assertIn("if: needs.select.outputs.linux-run == 'true'", native)
        for word in ('matrix', 'Windows', 'windows', 'BlastQa', 'WDDM'):
            self.assertNotIn(word, native)
        self.assertNotIn('self-hosted', self.source.replace(native, ''))

    def test_select_runs_both_selectors_and_the_workstation_only_qualifies(self):
        select = self.job('select', 'controlled')
        self.assertIn('run: python3 tools/ci/builder_trigger.py', select)
        self.assertIn('run: python3 tools/ci/gpu_select.py', select)
        self.assertIn('SANCUS_BUILDER_PAID_HOLD: ${{ vars.SANCUS_BUILDER_PAID_HOLD }}', select)
        self.assertIn("if: github.event_name == 'workflow_run' || inputs.mode == 'qualify'", select)
        self.assertIn('linux-run: ${{ steps.workstation.outputs.run }}', select)
        self.assertIn('run: ${{ steps.aws.outputs.run }}', select)
        self.assertIn('options: [audit, rehearse, permissions-smoke, qualify]', self.source)
        for name in ('fixed-expiry', 'rehearsal-fault', 'diagnosed-failure-run', 'verification-run-id'):
            self.assertIn('      ' + name + ':\n', self.source.split('jobs:', 1)[0])
        self.assertIn("REQUESTED_MODE: ${{ github.event_name == 'workflow_run' && 'qualify' || inputs.mode }}", select)

    def test_trusted_main_only_and_read_only_token(self):
        self.assertIn("github.ref == 'refs/heads/main'", self.source)
        self.assertNotIn('pull_request', self.source)
        self.assertNotIn('contents: write', self.source)

    def test_one_run_produces_both_promotion_artifacts_without_shared_file_names(self):
        import gpu_release_gates
        finalize = self.source.split('  finalize:\n', 1)[1]
        self.assertIn("needs.native.result == 'success'", finalize)
        self.assertIn('--bind-host artifacts/gpu-qualified', finalize)
        self.assertIn('name: release-gate-gpu-linux-x64', finalize)
        self.assertNotIn('release-gate-gpu-windows-x64', self.source)
        self.assertNotIn('gpu-staged-windows-x64', finalize)
        controller = (ROOT / '.github/workflows/builder-controller.yml').read_text()
        self.assertIn('name: release-gate-gpu-windows-x64', controller)
        self.assertNotIn('release-gate-gpu-linux-x64', controller)

        def files(text, artifact):
            block = text.split('name: ' + artifact + '\n', 1)[1].split('if-no-files-found', 1)[0]
            return {line.strip().rsplit('/', 1)[-1] for line in block.splitlines() if line.strip().startswith('artifacts/')}
        windows = files(controller, gpu_release_gates.artifact_name('windows-x64'))
        linux = files(self.source, gpu_release_gates.artifact_name('linux-x64'))
        self.assertEqual(windows, {'Blast-Circuit-windows-x64-gpu.json', 'Blast-Circuit-windows-x64-graphical.json',
                                   gpu_release_gates.CLEANUP_RECEIPT})
        self.assertEqual(linux, {'Blast-Circuit-linux-x64-gpu.json', 'Blast-Circuit-linux-x64-graphical.json',
                                 gpu_release_gates.HOST_RECEIPT})
        self.assertFalse(windows & linux)

    def test_actionlint_declares_the_workstation_label(self):
        self.assertIn('blast-gpu', (ROOT / '.github/actionlint.yaml').read_text())

    def test_native_contract_upload_never_races_owned_uninstaller_files(self):
        source = (ROOT / '.github/workflows/ci.yml').read_text().split('  native-contracts:', 1)[1].split('  native-cohort:', 1)[0]
        root = 'artifacts/native-contracts/${{ matrix.target }}'
        for phase in ('replay', 'inputs'):
            self.assertIn('!' + root + '/' + phase + '/installed/**', source)
        self.assertIn(root + '/', source)


class WorkstationSelection(unittest.TestCase):
    def test_manual_dispatch_qualifies_any_complete_current_main_run(self):
        self.assertEqual(gpu_select.select('workflow_dispatch', verification(), SHA, SHA, artifacts())[0], True)

    def test_automatic_runs_follow_the_major_minor_policy(self):
        self.assertTrue(gpu_select.select('workflow_run', verification(), SHA, SHA, artifacts(), {'requiresGpu': True})[0])
        approved, reason = gpu_select.select('workflow_run', verification(), SHA, SHA, artifacts(), {'requiresGpu': False})
        self.assertFalse(approved)
        self.assertIn('Patch', reason)
        self.assertFalse(gpu_select.select('workflow_run', verification(), SHA, SHA, artifacts(), None)[0])

    def test_superseded_untrusted_or_incomplete_runs_never_qualify(self):
        cases = [
            (verification(), SHA, 'b' * 40, artifacts()),
            (verification(head_sha='b' * 40), SHA, SHA, artifacts()),
            (verification(head_branch='feature'), SHA, SHA, artifacts()),
            (verification(head_repository={'full_name': 'fork/repo'}), SHA, SHA, artifacts()),
            (verification(conclusion='failure'), SHA, SHA, artifacts()),
            (verification(), SHA, SHA, artifacts()[1:]),
        ]
        for run, source, checkout, rows in cases:
            with self.subTest(run=run, checkout=checkout, rows=len(rows)):
                self.assertFalse(gpu_select.select('workflow_dispatch', run, source, checkout, rows)[0])

    def test_unknown_trigger_is_refused(self):
        self.assertFalse(gpu_select.select('push', verification(), SHA, SHA, artifacts())[0])


if __name__ == '__main__':
    unittest.main()

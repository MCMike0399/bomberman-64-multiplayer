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
    def setUp(self):
        self.source = (ROOT / '.github/workflows/gpu-qualification.yml').read_text()

    def test_no_paid_builder_workflow_or_cloud_identity_remains(self):
        workflows = ROOT / '.github/workflows'
        for name in ('builder-controller.yml', 'builder-bootstrap-preflight.yml', 'gpu-once.yml'):
            self.assertFalse((workflows / name).exists(), name)
        for word in ('id-token', 'aws-actions', 'role-to-assume', 'SANCUS_BUILDER', 'sancus-builder'):
            self.assertNotIn(word, self.source)

    def test_native_jobs_run_only_on_workstation_gpu_runners_with_the_real_adapter(self):
        self.assertIn("runs-on: [self-hosted, blast-gpu, '${{ matrix.os }}', X64]", self.source)
        self.assertIn('EXPECTED_ADAPTER: RTX 2080 SUPER', self.source)
        self.assertIn('BLAST_GPU_HOST: workstation', self.source)
        self.assertIn("name: Workstation GPU ${{ matrix.target }}", self.source)

    def test_trusted_main_only_and_read_only_token(self):
        self.assertIn("github.ref == 'refs/heads/main'", self.source)
        self.assertNotIn('pull_request', self.source)
        self.assertNotIn('contents: write', self.source)

    def test_promotion_artifacts_are_bound_after_both_native_jobs(self):
        finalize = self.source.split('  finalize:', 1)[1]
        self.assertIn("needs.native.result == 'success'", finalize)
        self.assertIn('--bind-host artifacts/gpu-qualified', finalize)
        for target in ('windows-x64', 'linux-x64'):
            self.assertIn('name: release-gate-gpu-' + target, finalize)
            self.assertIn('artifacts/gpu-qualified/Blast-Circuit-gpu-host.json', finalize)
            self.assertNotIn('release-gate-gpu-' + target, self.source.split('  finalize:', 1)[0])

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

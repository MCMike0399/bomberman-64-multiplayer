"""Keep the same-repository functional lane distinct from fork and host access."""
from pathlib import Path
import re
import unittest

WORKFLOW=Path(__file__).resolve().parents[2]/'.github/workflows/ci.yml'


def condition(text,job):
    match=re.search(r'^  '+re.escape(job)+r':\n(.*?)(?=^  [a-zA-Z0-9_-]+:|\Z)',text,re.M|re.S)
    if not match:raise AssertionError('Missing workflow job '+job)
    expression=re.search(r'^    if: (.*?)(?=^    [a-zA-Z0-9_-]+:|\Z)',match[1],re.M|re.S)
    if not expression:raise AssertionError('Missing explicit trust guard for '+job)
    return ''.join(expression[1].removeprefix('>-').split())


class FunctionalWorkflowTrust(unittest.TestCase):
    def test_diagnostic_artifacts_retain_only_scoped_startup_and_peer_evidence(self):
        text=WORKFLOW.read_text()
        startup=re.search(r'^  startup-gate:\n(.*?)(?=^  [a-zA-Z0-9_-]+:)',text,re.M|re.S)[1]
        self.assertIn('artifacts/release-gate-startup/*/app.log',startup)
        self.assertNotIn('Library/Application Support',startup)
        self.assertEqual(3,text.count('artifacts/*-regression-details/**/peer-ice-order-*.json'))
        self.assertNotIn('artifacts/release-gate-startup/**',startup)

    def test_download_containers_do_not_race_the_server_publisher(self):
        text=WORKFLOW.read_text()
        download=re.search(r'^  desktop-download:\n(.*?)(?=^  [a-zA-Z0-9_-]+:)',text,re.M|re.S)[1]
        self.assertNotIn('name: Blast-Circuit-server-dotnet10',download)
        native=re.search(r'^  desktop-native:\n(.*?)(?=^  [a-zA-Z0-9_-]+:)',text,re.M|re.S)[1]
        self.assertIn('needs: [desktop-download, managed]',native)
        self.assertIn('name: Blast-Circuit-server-dotnet10',native)

    def test_all_private_functional_builds_allow_only_main_or_same_repo_prs(self):
        text=WORKFLOW.read_text()
        expected="""needs.public.outputs.runtime == 'true' && vars.KERNEL_CI_ENABLED == 'true' &&
          ((github.event_name == 'pull_request' && github.event.pull_request.head.repo.full_name == github.repository) ||
           (github.event_name != 'pull_request' && github.ref == 'refs/heads/main'))"""
        for job in ('managed','headless','package','windows-package','linux-package'):
            with self.subTest(job=job):self.assertEqual(condition(text,job),''.join(expected.split()))
        self.assertNotRegex(text,re.compile(r'^  pull_request_target:',re.M))

    def test_self_hosted_graphics_and_mac_update_gates_stay_main_only(self):
        text=WORKFLOW.read_text()
        for job in ('graphical','startup-gate','update-gate'):
            expression=condition(text,job)
            with self.subTest(job=job):
                self.assertIn("github.event_name!='pull_request'",expression)
                self.assertIn("github.ref=='refs/heads/main'",expression)
                self.assertNotIn('||',expression)

    def test_three_os_crossplay_waits_for_the_actual_native_artifacts(self):
        text=WORKFLOW.read_text()
        self.assertRegex(text,r'(?s)\n  crossplay:\n.*?needs: \[package, desktop-download, managed\].*?uses: \./\.github/workflows/crossplay.yml\n +with:\n +mode: verification')
        crossplay = WORKFLOW.with_name('crossplay.yml').read_text()
        self.assertIn("inputs.mode == 'verification' && format('Blast-Circuit-{0}-download', matrix.target)", crossplay)
        self.assertIn("format('Blast-Circuit-{0}-smoke', matrix.target)", crossplay)
        self.assertIn('name: Exported macos-arm64 regression', text)
        self.assertIn('name: Exported ${{ matrix.target }} smoke', text)
        self.assertIn("""fromJSON(github.event_name == 'pull_request' && '["functional"]' || '["all-tour","other-cases"]')""", text)
        self.assertIn('needs: mac-regression-parts', text)
        self.assertIn('--merge-ci-parts', text)
        self.assertIn("needs.mac-regression-parts.result != 'skipped'", text)
        self.assertIn("needs.mac-regression-parts.result != 'success'", text)
        self.assertNotIn('\n  desktop-tour:',text)

    def test_promotion_downloads_exclude_mac_diagnostics_and_installer_fixtures(self):
        text = WORKFLOW.read_text()
        self.assertRegex(text, r'name: Blast-Circuit-macos-arm64-regression\n +path: artifacts/Blast-Circuit-macos-arm64-regression.json\n')
        self.assertRegex(text, r'name: Blast-Circuit-macos-arm64-regression-diagnostics-.*\n +path: \|')
        for target in ('windows-x64', 'linux-x64'):
            with self.subTest(target=target):
                self.assertRegex(text, rf'name: Blast-Circuit-{target}-update-fixture\n +path: \|\n +artifacts/Blast-Circuit-{target}-update-fixture.zip\n')
        native = text.split('\n  desktop-native:', 1)[1].split('\n  desktop-smoke:', 1)[0]
        self.assertIn('name: Blast-Circuit-${{ matrix.target }}-update-fixture', native)
        self.assertIn('package-update-fixture.py --target "$TARGET" --unpack', native)

    def test_gpu_candidates_cannot_enter_automatic_promotion(self):
        automatic = WORKFLOW.with_name('release.yml').read_text()
        self.assertIn('--check-only --automatic', automatic)
        self.assertIn("if: needs.candidate.outputs.publish == 'true'", automatic)
        promote = WORKFLOW.with_name('promote.yml').read_text()
        deploy = promote.split('\n  deploy:', 1)[1].split('\n  draft:', 1)[0]
        self.assertIn('DOTNET_INSTALL_DIR: ${{ runner.temp }}/dotnet', deploy)


if __name__=='__main__':unittest.main()

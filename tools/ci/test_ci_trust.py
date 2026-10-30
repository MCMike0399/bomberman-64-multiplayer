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
        self.assertRegex(text,r'(?s)\n  crossplay:\n.*?needs: \[desktop-smoke, mac-regression\].*?uses: \./\.github/workflows/crossplay.yml')
        self.assertIn('if [ "$EVENT_NAME" = pull_request ]; then args+=(--functional-only); fi',text)
        self.assertNotIn('\n  desktop-tour:',text)


if __name__=='__main__':unittest.main()

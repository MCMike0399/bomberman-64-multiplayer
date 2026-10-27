"""Mixed OS receipts must describe one real released cohort, renderer, timer and accepted actions."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

import crossplay
import desktop_packages as desktop
import desktop_validation as validation
import download_formats
from test_desktop_packages import write_desktop, SHA, RELEASE


def write_crossplay(directory, revision=SHA, release=None):
    release = release or RELEASE
    roster = ['actor-a', 'actor-b', 'actor-c']; deadline = 100000
    reports = []
    for target in crossplay.TARGETS:
        reports.append({'schemaVersion': 1, 'passed': True, 'target': target, 'release': release, 'revision': revision,
            'assertions': 20, 'runtimeErrors': [], 'captures': {'countdown.png': 'a' * 64, 'reset.png': 'b' * 64, 'gameplay.png': 'c' * 64},
            'downloadSha256': desktop.digest(directory / download_formats.FILES[target]),
            'serverArchiveSha256': desktop.digest(directory / 'Blast-Circuit-server-dotnet10.zip'),
            'crossplay': {'passed': True, 'role': target, 'lobbyId': 'shared-public-lobby', 'deadline': deadline, 'roster': roster,
                'resetObserved': True, 'moved': roster, 'bombOwners': roster, 'hosting': 'cluster', 'snapshots': 80,
                'firstGameplayServerTime': deadline, 'firstServerTime': deadline + 3000, 'lastServerTime': deadline + 23000,
                'lateExcluded': target == 'linux-x64', 'driver': {'windows-x64': 'd3d12', 'linux-x64': 'vulkan', 'macos-arm64': 'metal'}[target],
                'method': 'forward_plus', 'adapter': 'Fixture GPU'}})
    (directory / crossplay.RECEIPT).write_text(json.dumps({'schemaVersion': 1, 'passed': True, 'revision': revision, 'release': release,
        'targets': crossplay.TARGETS, 'lobbyId': 'shared-public-lobby', 'deadline': deadline, 'roster': roster, 'reports': reports}))
    cases = []
    for scenario in (*validation.HEADLESS, *validation.GRAPHICAL):
        graphical = scenario in validation.GRAPHICAL
        cases.append({'passed': True, 'platform': 'macOS', 'release': dict(release, revision=revision), 'scenario': scenario,
            'catalogSha256': validation.catalog_digest(), 'completedScenarios': validation.expanded(scenario, validation.catalog()),
            'assertions': 20, 'checks': [{'passed': True}] * 20, 'runtimeErrors': [],
            'screenshotMode': 'graphical' if graphical else 'disabled-headless', 'renderingDriver': 'metal',
            'renderingMethod': 'forward_plus', 'adapter': 'Fixture GPU', 'screenshots': ['capture.png'] if graphical else []})
    (directory / validation.MAC_RECEIPT).write_text(json.dumps({'schemaVersion': 1, 'passed': True, 'release': release, 'revision': revision,
        'archiveSha256': desktop.digest(directory / 'Blast-Circuit-macos-arm64.zip'),
        'serverSha256': desktop.digest(directory / 'Blast-Circuit-server-dotnet10.zip'),
        'downloadSha256': desktop.digest(directory / download_formats.FILES['macos-arm64']), 'cases': cases}))


class CrossplayTests(unittest.TestCase):
    def test_three_separate_os_passes_do_not_replace_a_shared_match_witness(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            write_desktop(root)
            for name in ('Blast-Circuit-macos-arm64.zip', download_formats.FILES['macos-arm64']): (root / name).write_bytes(b'Mac fixture')
            write_crossplay(root)
            original = json.loads((root / crossplay.RECEIPT).read_text())
            crossplay.inspect_summary(original, SHA, RELEASE, root)
            for field, value in [('lobbyId', 'different-lobby'), ('deadline', 123), ('moved', []), ('bombOwners', []),
                                 ('hosting', 'auto'), ('firstGameplayServerTime', 1), ('driver', 'opengl3')]:
                broken = copy.deepcopy(original); broken['reports'][1]['crossplay'][field] = value
                with self.subTest(field=field), self.assertRaises(RuntimeError): crossplay.inspect_summary(broken, SHA, RELEASE, root)
            broken = copy.deepcopy(original); broken['reports'][0]['downloadSha256'] = '0' * 64
            with self.assertRaisesRegex(RuntimeError, 'exact player download'): crossplay.inspect_summary(broken, SHA, RELEASE, root)
            broken = copy.deepcopy(original); broken['reports'][0]['serverArchiveSha256'] = '0' * 64
            with self.assertRaisesRegex(RuntimeError, 'exact staged server'): crossplay.inspect_summary(broken, SHA, RELEASE, root)

    def test_mac_full_regression_is_bound_to_server_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            write_desktop(root)
            for name in ('Blast-Circuit-macos-arm64.zip', download_formats.FILES['macos-arm64']):
                (root / name).write_bytes(b'Mac fixture')
            write_crossplay(root)
            receipt = json.loads((root / validation.MAC_RECEIPT).read_text())
            receipt['serverSha256'] = '0' * 64
            with self.assertRaisesRegex(RuntimeError, 'server'):
                validation.inspect_mac(receipt, SHA, RELEASE, desktop.digest(root / 'Blast-Circuit-macos-arm64.zip'),
                    desktop.digest(root / download_formats.FILES['macos-arm64']), desktop.digest(root / 'Blast-Circuit-server-dotnet10.zip'))

    def test_game_process_environment_excludes_ci_credentials(self):
        from unittest.mock import patch
        with patch.dict(crossplay.os.environ, {'GH_TOKEN': 'fixture', 'ACTIONS_RUNTIME_TOKEN': 'fixture', 'GITHUB_TOKEN': 'fixture'}):
            for name in ('GH_TOKEN', 'GITHUB_TOKEN', 'ACTIONS_RUNTIME_TOKEN'):
                self.assertNotIn(name, crossplay.clean_environment())


if __name__ == '__main__': unittest.main()

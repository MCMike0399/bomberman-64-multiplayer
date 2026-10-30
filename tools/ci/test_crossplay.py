"""Mixed OS receipts must describe one real released cohort, renderer, timer and accepted actions."""
import copy
import json
from pathlib import Path
import tempfile
import subprocess
import unittest
from unittest.mock import patch

import crossplay
import desktop_packages as desktop
import desktop_validation as validation
import download_formats
from test_desktop_packages import write_desktop, add_display_restart, SHA, RELEASE


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
                'lateExcluded': target == 'linux-x64', 'driver': {'windows-x64': 'vulkan', 'linux-x64': 'vulkan', 'macos-arm64': 'metal'}[target],
                'method': 'forward_plus', 'adapter': 'Fixture GPU'}})
    (directory / crossplay.RECEIPT).write_text(json.dumps({'schemaVersion': 1, 'passed': True, 'revision': revision, 'release': release,
        'targets': crossplay.TARGETS, 'lobbyId': 'shared-public-lobby', 'deadline': deadline, 'roster': roster, 'reports': reports}))
    cases = []
    for scenario in (*validation.HEADLESS, *validation.GRAPHICAL):
        graphical = scenario in validation.GRAPHICAL
        cases.append(add_display_restart({'passed': True, 'platform': 'macOS', 'release': dict(release, revision=revision), 'scenario': scenario,
            'catalogSha256': validation.catalog_digest(), 'completedScenarios': validation.expanded(scenario, validation.catalog()),
            'assertions': 20, 'checks': [{'passed': True}] * 20, 'runtimeErrors': [],
            'screenshotMode': 'graphical' if graphical else 'disabled-headless', 'renderingDriver': 'metal',
            'renderingMethod': 'forward_plus', 'adapter': 'Fixture GPU', 'screenshots': ['capture.png'] if graphical else []}))
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

    def test_mac_functional_pr_receipt_cannot_replace_release_graphics(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);write_desktop(root)
            for name in ('Blast-Circuit-macos-arm64.zip',download_formats.FILES['macos-arm64']):(root/name).write_bytes(b'Mac fixture')
            write_crossplay(root)
            receipt=json.loads((root/validation.MAC_RECEIPT).read_text())
            receipt.update(scope='functional-regression',graphicsQualified=False)
            receipt['cases']=receipt['cases'][:len(validation.HEADLESS)]
            args=(receipt,SHA,RELEASE,desktop.digest(root/'Blast-Circuit-macos-arm64.zip'),
                  desktop.digest(root/download_formats.FILES['macos-arm64']),desktop.digest(root/'Blast-Circuit-server-dotnet10.zip'))
            validation.inspect_mac(*args,require_graphics=False)
            with self.assertRaises(RuntimeError):validation.inspect_mac(*args)

    def test_game_process_environment_excludes_ci_credentials(self):
        from unittest.mock import patch
        with patch.dict(crossplay.os.environ, {'GH_TOKEN': 'fixture', 'ACTIONS_RUNTIME_TOKEN': 'fixture', 'GITHUB_TOKEN': 'fixture'}):
            for name in ('GH_TOKEN', 'GITHUB_TOKEN', 'ACTIONS_RUNTIME_TOKEN'):
                self.assertNotIn(name, crossplay.clean_environment())


if __name__ == '__main__': unittest.main()


class CrossplayRendezvousTests(unittest.TestCase):
    def test_transient_api_failures_are_retried_but_persistent_failures_still_fail(self):
        calls = []
        def flaky(args, **kwargs):
            calls.append(args)
            if len(calls) == 1: return subprocess.CompletedProcess(args, 1, '', 'net/http: TLS handshake timeout')
            return subprocess.CompletedProcess(args, 0, '{"ok": true}', '')
        self.assertEqual('{"ok": true}', crossplay.github('api', 'x', pause=lambda _: None, run=flaky))
        self.assertEqual(2, len(calls))
        def hung(args, **kwargs): raise subprocess.TimeoutExpired('gh', 60)
        with self.assertRaises(subprocess.TimeoutExpired):
            crossplay.github('api', 'x', pause=lambda _: None, run=hung)

    def test_clients_start_only_when_every_peer_runner_is_live(self):
        def job(name, status, conclusion=None): return {'name': 'Diagnostic cross-platform Quick Play / ' + name, 'status': status, 'conclusion': conclusion}
        names = ['Client windows-x64', 'Client linux-x64', 'Client macos-arm64']
        sequence = iter([[job(n, 'queued' if 'macos' in n else 'in_progress') for n in names],
                         [job(n, 'in_progress') for n in names]])
        clock = iter(range(0, 1000, 10))
        crossplay.wait_jobs(names, 1800, now=lambda: next(clock), pause=lambda _: None, jobs=lambda: next(sequence))
        ended = [job(n, 'completed', 'cancelled') if 'macos' in n else job(n, 'in_progress') for n in names]
        with self.assertRaisesRegex(RuntimeError, 'ended before the match'):
            crossplay.wait_jobs(names, 1800, now=lambda: 0, pause=lambda _: None, jobs=lambda: ended)
        queued = [job(n, 'queued') for n in names]
        times = iter([0, 2000])
        with self.assertRaisesRegex(RuntimeError, 'did not all start'):
            crossplay.wait_jobs(names, 1800, now=lambda: next(times), pause=lambda _: None, jobs=lambda: queued)

    def test_artifact_wait_tracks_only_missing_artifacts_producers(self):
        environment = {'GITHUB_REPOSITORY': 'o/r', 'GITHUB_RUN_ID': '1'}
        names = ['crossplay-windows-x64', 'crossplay-linux-x64', 'crossplay-macos-arm64']
        producers = ['Client windows-x64', 'Client linux-x64', 'Client macos-arm64']
        def artifacts(*present): return json.dumps({'artifacts': [{'name': n, 'expired': False} for n in present]})
        def jobs(**status): return [{'name': 'X / Client ' + t, 'status': s, 'conclusion': 'success' if s == 'completed' else None} for t, s in status.items()]
        finished_ok = jobs(**{'windows-x64': 'completed', 'linux-x64': 'in_progress', 'macos-arm64': 'in_progress'})
        with patch.dict(crossplay.os.environ, environment):
            # A finished producer whose artifact exists never blocks the others.
            replies = iter([artifacts(names[0]), artifacts(*names)])
            with patch.object(crossplay, 'github', side_effect=lambda *a, **k: next(replies)):
                crossplay.wait_artifacts(names, 10, producers, 3900, now=lambda: 0, pause=lambda _: None, jobs=lambda: finished_ok)
            # A missing artifact whose producer ended fails promptly, before any deadline.
            dead = jobs(**{'windows-x64': 'completed', 'linux-x64': 'completed', 'macos-arm64': 'in_progress'})
            with patch.object(crossplay, 'github', return_value=artifacts(names[0])):
                with self.assertRaisesRegex(RuntimeError, 'ended without its artifact: Client linux-x64'):
                    crossplay.wait_artifacts(names, 10, producers, 3900, now=lambda: 0, pause=lambda _: None, jobs=lambda: dead)
            # Live producers extend past the base timeout only up to the cap.
            times = iter([0, 500, 500, 4000, 4000])
            with patch.object(crossplay, 'github', return_value=artifacts(names[0])):
                with self.assertRaisesRegex(RuntimeError, 'Timed out'):
                    crossplay.wait_artifacts(names, 10, producers, 3900, now=lambda: next(times), pause=lambda _: None, jobs=lambda: finished_ok)

    def test_permanent_github_errors_are_not_retried_and_retries_respect_the_deadline(self):
        calls = []
        def failing(args, **kwargs):
            calls.append(args); return subprocess.CompletedProcess(args, 1, '', 'HTTP 401: Bad credentials')
        with self.assertRaises(subprocess.CalledProcessError):
            crossplay.github('api', 'x', pause=lambda _: None, run=failing)
        self.assertEqual(1, len(calls))
        with self.assertRaises(TimeoutError):
            crossplay.github('api', 'x', deadline=5, now=lambda: 10, run=failing)
        clock = [0]; delays = []
        def timeout(args, **kwargs):
            clock[0] += kwargs['timeout']
            raise subprocess.TimeoutExpired(args, kwargs['timeout'])
        def pause(seconds):
            delays.append(seconds); clock[0] += seconds
        with self.assertRaises(TimeoutError):
            crossplay.github('api', 'x', deadline=1, now=lambda: clock[0], pause=pause, run=timeout)
        self.assertEqual(1, clock[0])
        self.assertEqual([0], delays)


class CrossplayLifetimeContractTests(unittest.TestCase):
    def test_every_nested_crossplay_wait_fits_inside_its_enclosing_lifetime(self):
        import re
        text = (crossplay.ROOT / '.github/workflows/crossplay.yml').read_text()
        def minutes(job):
            section = text[text.index('\n  ' + job + ':'):]
            return int(re.search(r'timeout-minutes: (\d+)', section).group(1)) * 60
        self.assertGreaterEqual(minutes('clients'), crossplay.CLIENT_SETUP_SECONDS + crossplay.PEER_START_SECONDS + crossplay.CLIENT_PROCESS_SECONDS)
        self.assertGreaterEqual(minutes('server'), crossplay.SERVER_LIFETIME_SECONDS + 300)
        self.assertGreaterEqual(crossplay.SERVER_LIFETIME_SECONDS, crossplay.PEER_START_SECONDS + crossplay.CLIENT_MATCH_SECONDS + 300)
        cap = int(re.search(r'--cap (\d+)', text).group(1))
        self.assertLessEqual(cap, crossplay.ARTIFACT_CAP_SECONDS)
        self.assertGreaterEqual(cap, crossplay.CLIENT_SETUP_SECONDS + crossplay.PEER_START_SECONDS + crossplay.CLIENT_PROCESS_SECONDS)
        runtime = (crossplay.ROOT / 'tools/ci/crossplay_runtime.py').read_text()
        self.assertIn('process.wait(timeout=crossplay.CLIENT_PROCESS_SECONDS)', runtime)
        self.assertGreater(crossplay.CLIENT_PROCESS_SECONDS, crossplay.CLIENT_MATCH_SECONDS)
        self.assertIn('--names crossplay-ready-windows-x64 crossplay-ready-linux-x64 crossplay-ready-macos-arm64 --timeout ' + str(crossplay.CLIENT_READY_BARRIER_SECONDS), text)
        self.assertIn('--names crossplay-actions-windows-x64 crossplay-actions-linux-x64 crossplay-actions-macos-arm64 --timeout ' + str(crossplay.CLIENT_FINAL_BARRIER_SECONDS), text)
        self.assertLess(crossplay.ARTIFACT_CAP_SECONDS, crossplay.SERVER_LIFETIME_SECONDS)
        gate = int(re.search(r'wait-jobs .*--timeout (\d+)', text).group(1))
        self.assertLessEqual(gate, crossplay.PEER_START_SECONDS)

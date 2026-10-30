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

    def test_server_errors_back_off_for_a_bounded_outage_and_log_stderr(self):
        import io
        calls = []; delays = []; log = io.StringIO()
        def outage(args, **kwargs):
            calls.append(args); return subprocess.CompletedProcess(args, 1, '', 'HTTP 502: Server Error (https://api.github.com/x)')
        with self.assertRaises(subprocess.CalledProcessError) as raised:
            crossplay.github('api', 'x', pause=delays.append, run=outage, log=log)
        # Outlasts the five quick attempts but stops at the outage budget.
        self.assertEqual(crossplay.SERVER_ERROR_RETRY_SECONDS, sum(delays))
        self.assertGreater(len(calls), 5)
        self.assertIn('HTTP 502', raised.exception.stderr)
        self.assertIn('gh api x failed (exit 1): HTTP 502: Server Error', log.getvalue())
        # Recovery within the outage returns the result.
        replies = iter(['HTTP 503: Service Unavailable'] * 7 + [None])
        def recovers(args, **kwargs):
            error = next(replies)
            return subprocess.CompletedProcess(args, 1, '', error) if error else subprocess.CompletedProcess(args, 0, '{"ok": true}', '')
        self.assertEqual('{"ok": true}', crossplay.github('api', 'x', pause=lambda _: None, run=recovers, log=io.StringIO()))
        # The enclosing deadline still bounds an outage.
        clock = [0]; waits = []
        def pause(seconds): waits.append(seconds); clock[0] += seconds
        with self.assertRaises(TimeoutError):
            crossplay.github('api', 'x', deadline=20, now=lambda: clock[0], pause=pause, run=outage, log=io.StringIO())
        self.assertEqual(20, clock[0])

    def test_client_errors_stay_fatal_and_unknown_errors_keep_five_attempts(self):
        import io
        for error in ('HTTP 403: Forbidden', 'HTTP 422: Unprocessable Entity'):
            calls = []
            def failing(args, **kwargs):
                calls.append(args); return subprocess.CompletedProcess(args, 1, '', error)
            with self.assertRaises(subprocess.CalledProcessError):
                crossplay.github('api', 'x', pause=lambda _: None, run=failing, log=io.StringIO())
            self.assertEqual(1, len(calls), error)
        calls = []; delays = []
        def limited(args, **kwargs):
            calls.append(args); return subprocess.CompletedProcess(args, 1, '', 'HTTP 403: API rate limit exceeded')
        with self.assertRaises(subprocess.CalledProcessError):
            crossplay.github('api', 'x', pause=delays.append, run=limited, log=io.StringIO())
        self.assertEqual((5, [60] * 4), (len(calls), delays))
        calls = []
        def unknown(args, **kwargs):
            calls.append(args); return subprocess.CompletedProcess(args, 1, '', 'something odd')
        with self.assertRaises(subprocess.CalledProcessError):
            crossplay.github('api', 'x', pause=lambda _: None, run=unknown, log=io.StringIO())
        self.assertEqual(5, len(calls))


class CrossplayRerunIsolationTests(unittest.TestCase):
    """Run 41095133025 attempt 2: artifacts are run-scoped, so a rerun saw attempt 1's
    client/origin/ready artifacts, stopped its server at once and used a dead tunnel."""
    ENV = {'GITHUB_REPOSITORY': 'o/r', 'GITHUB_RUN_ID': '1', 'GITHUB_RUN_ATTEMPT': '2'}
    SINCE = '2026-10-30T18:32:00Z'

    @staticmethod
    def artifact(identity, name, created): return {'id': identity, 'name': name, 'created_at': created, 'expired': False}

    def test_stale_prior_attempt_artifacts_never_satisfy_a_barrier(self):
        names = ['crossplay-windows-x64', 'crossplay-linux-x64', 'crossplay-macos-arm64']
        stale = [self.artifact(10 + i, name, '2026-10-30T18:02:00Z') for i, name in enumerate(names)]
        replies = iter([stale, stale + [self.artifact(30, names[0], '2026-10-30T18:35:00Z')]])
        times = iter([0, 0, 2000])
        with patch.dict(crossplay.os.environ, self.ENV):
            with patch.object(crossplay, 'github', side_effect=lambda *a, **k: json.dumps({'artifacts': next(replies)})):
                with self.assertRaisesRegex(RuntimeError, 'Timed out waiting.*crossplay-linux-x64'):
                    crossplay.wait_artifacts(names, 10, now=lambda: next(times), pause=lambda _: None, since=self.SINCE)
            fresh = [self.artifact(40 + i, name, '2026-10-30T18:40:00Z') for i, name in enumerate(names)]
            with patch.object(crossplay, 'github', return_value=json.dumps({'artifacts': stale + fresh})):
                crossplay.wait_artifacts(names, 10, now=lambda: 0, pause=lambda _: None, since=self.SINCE)

    def test_download_selects_this_attempts_artifact_by_id_never_a_stale_name(self):
        import io, tempfile, zipfile
        stale = self.artifact(99999, 'crossplay-origin', '2026-10-30T17:50:32Z')
        current = self.artifact(12345, 'crossplay-origin', '2026-10-30T18:33:28Z')
        older_same_attempt = self.artifact(12000, 'crossplay-origin', '2026-10-30T18:33:00Z')
        self.assertEqual(crossplay.fresh_artifacts([stale, current, older_same_attempt], self.SINCE)['crossplay-origin']['id'], 12345)
        self.assertEqual(crossplay.fresh_artifacts([stale], self.SINCE), {})
        payload = io.BytesIO()
        with zipfile.ZipFile(payload, 'w') as bundle: bundle.writestr('origin.json', '{"origin": "https://fresh"}')
        requested = []
        def run(command, **kwargs):
            requested.append(command[-1]); return subprocess.CompletedProcess(command, 0, payload.getvalue(), b'')
        with tempfile.TemporaryDirectory() as directory, patch.dict(crossplay.os.environ, self.ENV):
            with patch.object(crossplay, 'github', return_value=json.dumps({'artifacts': [stale, current]})):
                crossplay.download_artifact('crossplay-origin', Path(directory), self.SINCE, run=run)
                self.assertEqual(json.loads((Path(directory) / 'origin.json').read_text()), {'origin': 'https://fresh'})
            self.assertEqual(requested, ['repos/o/r/actions/artifacts/12345/zip'])
            with patch.object(crossplay, 'github', return_value=json.dumps({'artifacts': [stale]})):
                with self.assertRaisesRegex(RuntimeError, 'No crossplay-origin artifact from this run attempt'):
                    crossplay.download_artifact('crossplay-origin', Path(directory), self.SINCE, run=run)

    def test_attempt_start_comes_from_this_attempt(self):
        with patch.dict(crossplay.os.environ, self.ENV), patch.object(crossplay, 'github', return_value=json.dumps({'run_started_at': self.SINCE})) as api:
            self.assertEqual(crossplay.attempt_started(), self.SINCE)
        self.assertEqual(api.call_args.args, ('api', 'repos/o/r/actions/runs/1/attempts/2'))

    def test_every_crossplay_barrier_artifact_is_attempt_scoped_and_downloaded_by_id(self):
        import re
        text = (crossplay.ROOT / '.github/workflows/crossplay.yml').read_text()
        names = re.findall(r'^\s+name: (crossplay-.+)$', text, re.MULTILINE)
        self.assertGreaterEqual(len(names), 9)
        self.assertEqual([], [name for name in names if not name.endswith('-a${{ github.run_attempt }}')])
        for listed in re.findall(r'--names ([^\n]+?) --(?:timeout|directory)', text):
            parts = [part.strip() for part in listed.split('-a${{ github.run_attempt }}')]
            self.assertEqual('', parts[-1], listed)
            self.assertTrue(all(part.startswith('crossplay-') and ' ' not in part for part in parts[:-1]), listed)
        self.assertNotIn('gh run download', text)
        self.assertIn('crossplay.py download --names crossplay-origin-a${{ github.run_attempt }}', text)


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
        self.assertIn('--names crossplay-ready-windows-x64-a${{ github.run_attempt }} crossplay-ready-linux-x64-a${{ github.run_attempt }} crossplay-ready-macos-arm64-a${{ github.run_attempt }} --timeout ' + str(crossplay.CLIENT_READY_BARRIER_SECONDS), text)
        self.assertIn('--names crossplay-actions-windows-x64-a${{ github.run_attempt }} crossplay-actions-linux-x64-a${{ github.run_attempt }} crossplay-actions-macos-arm64-a${{ github.run_attempt }} --timeout ' + str(crossplay.CLIENT_FINAL_BARRIER_SECONDS), text)
        self.assertLess(crossplay.ARTIFACT_CAP_SECONDS, crossplay.SERVER_LIFETIME_SECONDS)
        gate = int(re.search(r'wait-jobs .*--timeout (\d+)', text).group(1))
        self.assertLessEqual(gate, crossplay.PEER_START_SECONDS)

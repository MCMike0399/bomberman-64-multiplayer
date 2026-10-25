"""Failure-path contracts; these fixtures never launch Godot or a product server."""
import contextlib
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import Mock, patch
import xml.etree.ElementTree as ET

SPEC = importlib.util.spec_from_file_location('qa_runner', Path(__file__).with_name('run.py'))
qa = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(qa)


class HarnessTests(unittest.TestCase):
    def test_each_playtest_has_its_own_receipt_directory(self):
        with tempfile.TemporaryDirectory() as temporary:
            suite = Mock(output=Path(temporary), godot='fixture', desktop='isolated', vm='fixture')
            qa.Suite.playtest(suite, 'package-smoke', headless=True)
            qa.Suite.playtest(suite, 'peer-rooms', headless=True)
            commands = [call.args[1] for call in suite.require.call_args_list]
            outputs = [command[command.index('--output') + 1] for command in commands]
            self.assertEqual(len(set(outputs)), 2)

    def test_playtest_uses_catalog_deadline_and_keeps_outer_cleanup_headroom(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            catalog = root / 'assets/testing/scenarios.json'
            catalog.parent.mkdir(parents=True)
            catalog.write_text(json.dumps({'scenarios': [
                {'id': 'all', 'timeout': 900}, {'id': 'core-headless', 'timeout': 600}]}))
            for desktop, scenario, headless, seconds, outer in (
                    ('shared', 'all', False, 900, 960),
                    ('isolated', 'all', False, 900, 1800),
                    ('shared', 'core-headless', True, 600, 660)):
                with self.subTest(desktop=desktop, scenario=scenario):
                    suite = qa.Suite.__new__(qa.Suite)
                    suite.output, suite.godot, suite.desktop, suite.vm = root / 'qa', 'fixture-godot', desktop, 'fixture-vm'
                    suite.require = Mock()
                    with patch.object(qa, 'ROOT', root):
                        suite.playtest(scenario, headless=headless)
                    command = suite.require.call_args.args[1]
                    self.assertEqual(str(seconds), command[command.index('--timeout') + 1])
                    self.assertEqual(outer, suite.require.call_args.kwargs['timeout'])

    def test_isolated_graphical_profile_uses_guest_server_without_touching_host_listeners(self):
        suite = Mock(profile='graphical', desktop='isolated', godot='fixture-godot')
        qa.execute(suite)
        suite.server.assert_not_called()
        suite.playtest.assert_called_once_with('all', app=None)

    @unittest.skipUnless(os.name == 'posix', 'POSIX process-group cleanup')
    def test_post_reap_zombie_group_permission_error_is_harmless(self):
        process = Mock(pid=12345)
        process.poll.return_value = None
        with patch.object(qa.os, 'killpg', side_effect=[None, PermissionError('terminated group')]):
            qa.stop(process)
        process.wait.assert_called_once_with(timeout=5)
        with patch.object(qa.os, 'killpg', side_effect=PermissionError('live owned process')):
            with self.assertRaises(PermissionError):
                qa.stop(process)

    def test_soak_rejects_recorded_failures_inactivity_liveness_and_duplicate_rows(self):
        result = {'Stage': 'fixture', 'Difficulty': 'fixture', 'Bots': 3, 'Seed': 0,
                  'Ticks': 10, 'ActiveTicks': 5, 'EligibleBotTicks': 6, 'MovingBotTicks': 3,
                  'Bombs': 1, 'LongestOpenIdleTicks': 1}
        report = {'fairnessChecks': 1, 'idleLimitTicks': 3, 'tickRate': 1,
                  'results': [dict(result, Seed=seed) for seed in range(60)]}
        with tempfile.TemporaryDirectory() as temporary:
            receipt = Path(temporary) / 'soak.json'
            receipt.write_text(json.dumps(report))
            self.assertEqual(60, qa.validate_soak(receipt)['scenarios'])
            changes = [('passed', False), ('EligibleBotTicks', 0), ('MovingBotTicks', 0),
                       ('Bombs', 0), ('ActiveTicks', 0), ('LongestOpenIdleTicks', 4), ('Seed', 1)]
            for key, value in changes:
                changed = copy.deepcopy(report)
                changed['results'][0][key] = value
                receipt.write_text(json.dumps(changed))
                with self.subTest(key=key), self.assertRaises(RuntimeError):
                    qa.validate_soak(receipt)

    def test_missing_zero_and_ambiguous_managed_footers_fail(self):
        with tempfile.TemporaryDirectory() as temporary:
            log = Path(temporary) / 'test.log'
            for text in ('', 'booted successfully\n', 'Passed 0 game integration checks.\n',
                         'Passed 1 game integration checks.\nPassed 2 game integration checks.\n'):
                log.write_text(text)
                with self.subTest(text=text), self.assertRaises(RuntimeError):
                    qa.verify_footer(log, qa.FOOTERS['Bomber.Game.Tests'])

    def test_all_managed_footers_record_positive_counts_without_fixed_count_lock(self):
        examples = {'Bomber.Game.Tests': 'Passed 123 game integration checks.',
                    'Bomber.Client.Tests': 'PASS: 7 client contract and session checks',
                    'Bomber.Ai.Tests': 'PASS: 8 AI behavior, timing, determinism and allocation checks',
                    'Bomber.Audio.Tests': 'PASS 19 original PCM cues: fixture'}
        with tempfile.TemporaryDirectory() as temporary:
            log = Path(temporary) / 'test.log'
            for name, text in examples.items():
                log.write_text('earlier output\n' + text + '\n')
                with self.subTest(name=name):
                    evidence = qa.verify_footer(log, qa.FOOTERS[name])
                    self.assertGreater(evidence['checks'], 0)
                    self.assertEqual(text, evidence['completionFooter'])
            log.write_text('PASS one network check\n')
            with self.assertRaises(RuntimeError):
                qa.verify_footer(log, qa.FOOTERS['network-smoke'])
            log.write_text('ALL NETWORK SMOKE CHECKS PASSED (43.2s)\n')
            self.assertIn('completionFooter', qa.verify_footer(log, qa.FOOTERS['network-smoke']))

    def test_exit_zero_without_footer_fails_the_step_receipt(self):
        with tempfile.TemporaryDirectory() as temporary, contextlib.redirect_stdout(io.StringIO()):
            suite = self.suite(temporary)
            self.assertFalse(suite.step('empty-test', [sys.executable, '-c', 'pass'], footer=qa.FOOTERS['Bomber.Game.Tests']))
            self.assertEqual(0, suite.steps[0]['exitCode'])
            self.assertFalse(suite.steps[0]['passed'])

    def test_managed_smoke_selects_release_delivery_instead_of_debug_server(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            suite = Mock(profile='managed', output=root / 'qa', steps=[])
            suite.server.return_value = contextlib.nullcontext('http://127.0.0.1:12345')
            with patch.object(qa, 'ROOT', root):
                qa.execute(suite)
            self.assertEqual(2, suite.server.call_count)
            self.assertEqual({'dll':root / 'artifacts/server-staged/Bomber.Server.dll'},suite.server.call_args_list[0].kwargs)
            self.assertEqual({'dll':root / 'artifacts/server-staged/Bomber.Server.dll','simulation_capacity':0},suite.server.call_args_list[1].kwargs)
            calls = {call.args[0]: call.args[1] for call in suite.require.call_args_list}
            self.assertIn('Release', calls['publish-server'])
            self.assertIn('-p:UseAppHost=false', calls['publish-server'])
            self.assertIn('--stage-only', calls['stage-server-delivery'])
            self.assertIn('package-tested-server', calls)
            self.assertIn('--adaptive-peer-only', calls['network-adaptive-relay'])
            self.assertIn('--fleet', calls['network-fleet'])
            self.assertIn(str(root / 'artifacts/server-staged/Bomber.Server.dll'), calls['network-fleet'])
            self.assertIn('publish-Realtime.Redis', calls)

    def test_package_builds_missing_controller_backend_before_export(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            suite = Mock(profile='package', godot='fixture-godot', output=root / 'qa', steps=[])
            suite.server.return_value = contextlib.nullcontext('http://127.0.0.1:12345')
            order = []
            def fake_require(name, command, **kwargs):
                order.append(name)
                if name == 'controller-backend-build':
                    self.assertEqual(['bash', 'tools/controllers/bootstrap-sdl.sh'], command)
                    self.assertEqual(600, kwargs['timeout'])
                    destination = root / 'native/macos'
                elif name == 'macos-export':
                    destination = Path(command[command.index('--output') + 1]) / 'Contents/Frameworks/Controllers'
                else:
                    return
                destination.mkdir(parents=True)
                for filename in qa.CONTROLLER_FILES:
                    (destination / filename).write_text('fixture')
                if kwargs.get('validate'):
                    kwargs['validate']()
            suite.require.side_effect = fake_require
            with patch.object(qa, 'ROOT', root), patch.object(qa.platform, 'system', return_value='Darwin'), patch.object(qa.platform, 'machine', return_value='arm64'):
                qa.execute(suite)
            self.assertLess(order.index('controller-backend-build'), order.index('macos-export'))
            self.assertTrue(suite.steps[0]['evidence']['switch2UsbBackend'])
            self.assertEqual([c.args[0] for c in suite.playtest.call_args_list], ['package-smoke', 'peer-rooms', 'adaptive-rooms'])
            self.assertEqual(suite.playtest.call_args_list[1].kwargs['origin'], 'http://127.0.0.1:12345')

    def test_package_refuses_incomplete_backend_after_bootstrap(self):
        with tempfile.TemporaryDirectory() as temporary:
            suite = self.suite(temporary)
            with patch.object(qa, 'ROOT', Path(temporary)), patch.object(qa.platform, 'system', return_value='Darwin'), patch.object(qa.platform, 'machine', return_value='arm64'), patch.object(suite, 'require') as require:
                def fake_require(name, command, **kwargs):
                    kwargs['validate']()
                require.side_effect = fake_require
                with self.assertRaisesRegex(RuntimeError, 'incomplete'):
                    qa.ensure_controller_backend(suite)

    def test_package_rejects_native_startup_failure_before_export(self):
        suite = Mock(profile='package', godot='fixture-godot')
        def require(name, command, **kwargs):
            if name == 'controller-backend-runtime':
                raise RuntimeError('native startup failed')
        suite.require.side_effect = require
        with patch.object(qa, 'ensure_controller_backend'):
            with self.assertRaisesRegex(RuntimeError, 'native startup failed'):
                qa.execute(suite)
        self.assertNotIn('macos-export', [call.args[0] for call in suite.require.call_args_list])
        suite.playtest.assert_not_called()

    def suite(self, destination):
        with patch.object(qa, 'version', return_value='fixture'):
            return qa.Suite('public', Path(destination), 'missing-godot-fixture')

    def test_missing_executable_fails_with_json_and_junit(self):
        with tempfile.TemporaryDirectory() as temporary, contextlib.redirect_stdout(io.StringIO()):
            suite = self.suite(temporary)
            self.assertFalse(suite.step('missing', [str(Path(temporary) / 'not-an-executable')]))
            self.assertEqual(1, suite.finish())
            self.assertFalse(json.loads((Path(temporary) / 'summary.json').read_text())['passed'])
            self.assertEqual('1', ET.parse(Path(temporary) / 'junit.xml').getroot().attrib['failures'])

    def test_missing_and_failed_playtest_reports_cannot_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaisesRegex(RuntimeError, 'receipt'):
                qa.validate_playtest(root, True)
            run = root / 'fresh-run'
            run.mkdir()
            (run / 'runner-summary.json').write_text(json.dumps({'passed': True}))
            (run / 'summary.json').write_text(json.dumps({'passed': False, 'assertions': 3}))
            with self.assertRaisesRegex(RuntimeError, 'failed'):
                qa.validate_playtest(root, True)

    def test_zero_assertions_and_missing_graphical_captures_fail(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            run = root / 'fresh-run'
            run.mkdir()
            (run / 'runner-summary.json').write_text(json.dumps({'passed': True}))
            (run / 'summary.json').write_text(json.dumps({'passed': True, 'assertions': 0}))
            with self.assertRaises(RuntimeError):
                qa.validate_playtest(root, True)
            (run / 'summary.json').write_text(json.dumps({'passed': True, 'assertions': 2, 'screenshots': []}))
            with self.assertRaisesRegex(RuntimeError, 'graphical'):
                qa.validate_playtest(root, False)

    @unittest.skipUnless(os.name == 'posix', 'process-group ownership is a POSIX contract')
    def test_timeout_stops_parent_and_descendant_processes(self):
        with tempfile.TemporaryDirectory() as temporary, contextlib.redirect_stdout(io.StringIO()):
            suite = self.suite(temporary)
            code = 'import subprocess,sys,time; child=subprocess.Popen([sys.executable,"-c","import time; time.sleep(60)"]); print(child.pid,flush=True); time.sleep(60)'
            self.assertFalse(suite.step('timeout', [sys.executable, '-c', code], timeout=.5))
            record = suite.steps[0]
            self.assertTrue(record['timedOut'])
            child = int(Path(record['log']).read_text().strip())
            deadline = time.monotonic() + 3
            for pid in (record['pid'], child):
                while time.monotonic() < deadline:
                    result = subprocess.run(['ps', '-p', str(pid), '-o', 'stat='], capture_output=True, text=True)
                    if result.returncode or not result.stdout.strip() or result.stdout.strip().startswith('Z'):
                        break
                    time.sleep(.05)
                else:
                    self.fail(f'Owned process {pid} survived timeout cleanup')

    def test_server_uses_its_own_dynamic_port_and_cleans_up_on_failure(self):
        code = '''import http.server,json
class Handler(http.server.BaseHTTPRequestHandler):
 def do_GET(self):
  self.send_response(200);self.end_headers();self.wfile.write(b'{"value":[{"id":"blast"}]}')
server=http.server.HTTPServer(('127.0.0.1',0),Handler)
print('Now listening on: http://127.0.0.1:'+str(server.server_port),flush=True)
server.serve_forever()
'''
        actual_popen = subprocess.Popen
        launched = []
        def fake_server(command, **kwargs):
            self.assertEqual(['--urls', 'http://127.0.0.1:0'], command[-2:])
            process = actual_popen([sys.executable, '-c', code], **kwargs)
            launched.append(process)
            return process
        with tempfile.TemporaryDirectory() as temporary:
            suite = self.suite(temporary)
            with patch.object(qa.subprocess, 'Popen', side_effect=fake_server):
                with self.assertRaisesRegex(RuntimeError, 'fixture interruption'):
                    with suite.server() as origin:
                        self.assertRegex(origin, r'^http://127\.0\.0\.1:\d+$')
                        raise RuntimeError('fixture interruption')
            self.assertEqual(1, len(launched))
            self.assertIsNotNone(launched[0].poll())
            self.assertFalse(suite.steps[0]['passed'])


if __name__ == '__main__':
    unittest.main()

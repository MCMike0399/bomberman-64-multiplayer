"""Release gates exercise stale workers, unsafe deployment evidence and partial rollback."""
import json
import os
from pathlib import Path
import tempfile
import subprocess
import unittest
from unittest.mock import patch

import deploy_release
import release_identity
import verify_deployment
import promote_release

RELEASE = {'version': '1.2.3', 'build': '42', 'protocolRevision': 4, 'admissionToken': '4/1.2.3/42'}
SHA = 'a' * 40
TARGETS = {'primary': 'https://primary.example', 'fallback': 'https://fallback.example'}


class IdentityTests(unittest.TestCase):
    def test_superseded_main_run_cannot_deploy_or_publish(self):
        with patch.object(promote_release, 'gh', return_value=json.dumps({'object': {'sha': 'b' * 40}})):
            with self.assertRaisesRegex(RuntimeError, 'stale deployment'):
                promote_release.require_current_main('example/game', SHA)

    def test_only_exact_source_draft_is_promotable_even_before_tag_creation(self):
        for target, draft, allowed in ((SHA, True, True), ('b' * 40, True, False), (SHA, False, False)):
            responses = ['[]', json.dumps([[{'tag_name': 'v1.2.3', 'draft': draft, 'target_commitish': target}]])]
            with self.subTest(target=target, draft=draft), patch.object(promote_release, 'gh', side_effect=responses):
                if allowed:
                    self.assertTrue(promote_release.inspect_existing_release('example/game', '1.2.3', SHA, publish=True))
                else:
                    with self.assertRaises(RuntimeError): promote_release.inspect_existing_release('example/game', '1.2.3', SHA, publish=True)

    def test_export_drift_fails_and_a_version_change_invalidates_same_schema(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            props = root / 'Release.props'
            props.write_text('<Project><PropertyGroup><BlastVersion>1.2.3</BlastVersion><BlastBuild>42</BlastBuild><BlastProtocolRevision>4</BlastProtocolRevision></PropertyGroup></Project>')
            (root / 'export_presets.cfg').write_text('application/short_version="1.2.2"\napplication/version="41"\n')
            with self.assertRaisesRegex(ValueError, 'differs from exports'):
                release_identity.check_exports(root)
            self.assertEqual(RELEASE, release_identity.check_exports(root, write=True))
            props.write_text(props.read_text().replace('1.2.3', '1.2.4'))
            self.assertNotEqual(RELEASE['admissionToken'], release_identity.identity(root)['admissionToken'])


class DeploymentVerificationTests(unittest.TestCase):
    def test_a_probe_needs_a_positive_completion_footer_not_just_exit_zero(self):
        for output, accepted in (('booted\n', False), ('PASS 0 release connection and cleanup checks\n', False),
                                 ('PASS 15 release connection and cleanup checks\n', True)):
            with self.subTest(output=output), patch.object(verify_deployment, 'build_probe', return_value=(Path('/fixture'), Path('/fixture/probe'))), patch.object(verify_deployment.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, stdout=output, stderr='')):
                if accepted:
                    verify_deployment.verify_connections(TARGETS)
                else:
                    with self.assertRaisesRegex(RuntimeError, 'WebSocket connection failed'):
                        verify_deployment.verify_connections(TARGETS)

    def reply(self, origin, path, token=None, body=None):
        header = [RELEASE['admissionToken']]
        if path == '/game':
            return 200, header, {'release': dict(RELEASE, revision=SHA)}
        if path.endswith('/games'):
            return 200, header, {'value': [{'id': 'blast-room'}]}
        if token == RELEASE['admissionToken']:
            return 201, header, {'playerId': 'test', 'token': 'fixture-private-token'}
        return 409, header, {'error': {'code': 'client_update_required'}}

    def test_every_worker_is_checked_and_receipts_exclude_guest_tokens(self):
        with patch.object(verify_deployment, 'request', side_effect=self.reply) as request:
            receipts = verify_deployment.verify(TARGETS, RELEASE, SHA)
        self.assertEqual(10, request.call_count)
        self.assertEqual(set(TARGETS), {item['node'] for item in receipts})
        self.assertNotIn('fixture-private-token', json.dumps(receipts))

    def test_stale_fallback_wrong_commit_missing_echo_and_disabled_admission_fail(self):
        for fault in ('release', 'revision', 'echo', 'legacy', 'current'):
            def reply(origin, path, token=None, body=None):
                status, headers, data = self.reply(origin, path, token, body)
                if origin == TARGETS['fallback']:
                    if path == '/game':
                        if fault == 'release': data['release']['build'] = '41'
                        if fault == 'revision': data['release']['revision'] = 'b' * 40
                        if fault == 'echo': headers = None
                    elif path.endswith('/guests'):
                        if fault == 'legacy' and token != RELEASE['admissionToken']: status = 201
                        if fault == 'current' and token == RELEASE['admissionToken']: status = 409
                return status, headers, data
            with self.subTest(fault=fault), patch.object(verify_deployment, 'request', side_effect=reply):
                with self.assertRaises(RuntimeError): verify_deployment.verify(TARGETS, RELEASE, SHA)

    def test_target_configuration_is_explicit_and_safe(self):
        for value in ('{}', '[]', '{"node":"http://internet.example"}', '{"node":"https://user:password@host"}', '{"node":"https://host?secret=x"}', '{"a":"https://host","b":"https://host"}'):
            with self.subTest(value=value), self.assertRaises(RuntimeError): verify_deployment.targets_from_json(value)
        self.assertEqual(TARGETS, verify_deployment.targets_from_json(json.dumps(TARGETS)))


class DeploymentTransactionTests(unittest.TestCase):
    def test_invalid_signing_key_prevents_any_host_change(self):
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, {'BLAST_RELEASE_SERVER_URLS': json.dumps(TARGETS)}), \
                patch.object(deploy_release, 'inspect_run', return_value=SHA), \
                patch.object(deploy_release, 'check_exports', return_value=RELEASE), \
                patch.object(deploy_release, 'require_current_main'), patch.object(deploy_release, 'inspect_existing_release'), \
                patch.object(deploy_release, 'build_probe'), patch.object(deploy_release, 'gh'), \
                patch.object(deploy_release, 'inspect_package', return_value=Path('/fixture/archive.zip')), \
                patch.object(deploy_release, 'inspect_server'), \
                patch.object(deploy_release.subprocess, 'run', return_value=subprocess.CompletedProcess([], 1, b'', b'wrong key')), \
                patch.object(deploy_release, 'deploy') as host:
            with self.assertRaisesRegex(RuntimeError, 'signing preflight failed'):
                deploy_release.main(['--run-id', '12', '--version', RELEASE['version'], '--expected-sha', SHA,
                                     '--repository', 'example/game', '--adapter', '/operator/adapter',
                                     '--signing-key', '/fixture/key', '--output', str(Path(temporary) / 'result.json')])
            host.assert_not_called()

    def test_host_adapter_never_inherits_release_credentials(self):
        with patch.dict(os.environ, {'GH_TOKEN': 'github-fixture', 'BLAST_UPDATE_SIGNING_KEY': 'signing-fixture', 'BLAST_OPERATOR_TOKEN': 'operator-fixture'}), \
                patch.object(deploy_release.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, b'', b'')) as command:
            deploy_release.adapter_call(Path('/operator/adapter'), 'apply', Path('/context'))
        environment = command.call_args.kwargs['env']
        self.assertNotIn('GH_TOKEN', environment)
        self.assertNotIn('BLAST_UPDATE_SIGNING_KEY', environment)
        self.assertEqual('operator-fixture', environment['BLAST_OPERATOR_TOKEN'])

    def run_deploy(self, failed_phase=None, evidence=None, verify_failure=False, connection_failure=False):
        phases = []
        def adapter_call(adapter, phase, context):
            phases.append(phase)
            if phase == failed_phase: raise RuntimeError('failed ' + phase)
            if phase == 'drain':
                return evidence if evidence is not None else {name: {'drained': True, 'rooms': 0} for name in TARGETS}
        with tempfile.TemporaryDirectory() as temporary:
            adapter = Path(temporary) / 'adapter'
            adapter.touch()
            adapter.chmod(0o700)
            with patch.object(deploy_release, 'adapter_call', side_effect=adapter_call), patch.object(deploy_release, 'verify', side_effect=RuntimeError('stale node') if verify_failure else None, return_value=[]), patch.object(deploy_release, 'verify_connections', side_effect=RuntimeError('unplayable') if connection_failure else None):
                try:
                    deploy_release.deploy(adapter, Path(temporary) / 'context.json', TARGETS, RELEASE, SHA)
                except RuntimeError:
                    return phases, False
        return phases, True

    def test_success_drains_entire_fleet_before_apply_and_resumes_after_verification(self):
        self.assertEqual((['drain', 'apply', 'resume'], True), self.run_deploy())

    def test_missing_active_private_room_or_malformed_evidence_prevents_apply(self):
        for evidence in ({}, {'primary': {'drained': True, 'rooms': 0}},
                         {name: {'drained': True, 'rooms': 1} for name in TARGETS},
                         {name: {'drained': True, 'rooms': False} for name in TARGETS}):
            self.assertEqual((['drain', 'resume'], False), self.run_deploy(evidence=evidence))

    def test_partial_apply_and_stale_worker_roll_back_before_resuming(self):
        self.assertEqual((['drain', 'apply', 'rollback', 'resume'], False), self.run_deploy(failed_phase='apply'))
        self.assertEqual((['drain', 'apply', 'rollback', 'resume'], False), self.run_deploy(verify_failure=True))

    def test_failed_rollback_leaves_fleet_drained(self):
        self.assertEqual((['drain', 'apply', 'rollback'], False), self.run_deploy(failed_phase='rollback', verify_failure=True))

    def test_correct_version_with_broken_actual_connection_rolls_back(self):
        self.assertEqual((['drain', 'apply', 'resume', 'rollback', 'resume'], False), self.run_deploy(connection_failure=True))

    def test_partial_drain_failure_resumes_unchanged_fleet(self):
        self.assertEqual((['drain', 'resume'], False), self.run_deploy(failed_phase='drain'))


if __name__ == '__main__':
    unittest.main()

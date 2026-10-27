"""Exercise the release CLI's prepared draft, single fleet probe and durable retry."""
from contextlib import ExitStack
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import deploy_release
import promote_release
from deployment_state import Journal, locked
from test_promotion import RELEASE, SHA, write_package, write_server

TARGETS = {'public': 'https://public.example', 'fallback': 'https://fallback.example'}


class ReleaseCutoverTests(unittest.TestCase):
    def setUp(self):
        self.stack = ExitStack()
        self.addCleanup(self.stack.close)
        self.root = Path(self.stack.enter_context(tempfile.TemporaryDirectory()))
        self.state = self.root / 'state'
        self.adapter = self.root / 'operator-adapter'
        self.adapter.touch(mode=0o700)
        self.events, self.assets = [], {}
        self.corrupt_upload = self.fail_publish = self.stale_fleet = False
        self.downloads = 0
        self.stack.enter_context(patch.dict(os.environ, {
            'BLAST_RELEASE_SERVER_URLS': json.dumps(TARGETS),
            'BLAST_RELEASE_PLACEMENTS': json.dumps({name: {'serverId': name, 'region': name} for name in TARGETS})}))
        for module in (deploy_release, promote_release):
            self.stack.enter_context(patch.object(module, 'gh', side_effect=self.github))
            self.stack.enter_context(patch.object(module, 'check_exports', return_value=RELEASE))
        for name, kwargs in {
            'inspect_run': {'return_value': SHA}, 'inspect_gates': {}, 'build_probe': {},
            'require_current_main': {},
            'inspect_existing_release': {'side_effect': lambda *a, **k: bool(self.assets)},
            'adapter_call': {'side_effect': self.host}, 'verify': {'side_effect': self.identities},
            'verify_connections': {'side_effect': self.connections},
        }.items():
            self.stack.enter_context(patch.object(deploy_release, name, **kwargs))
        self.stack.enter_context(patch.object(deploy_release.subprocess, 'run', side_effect=self.sign))
        self.args = ['--run-id', '12', '--version', RELEASE['version'], '--expected-sha', SHA,
                     '--repository', 'example/game', '--adapter', str(self.adapter), '--state-dir', str(self.state),
                     '--signing-key', str(self.root / 'fixture.pem'), '--output', str(self.root / 'receipt.json'), '--publish']

    def sign(self, command, **kwargs):
        directory = Path(command[command.index('--output') + 1])
        for name in ('Blast-Circuit-update.json', 'Blast-Circuit-update.json.sig',
                     'Blast-Circuit-release.json', 'Blast-Circuit-release.json.sig'):
            (directory / name).write_text('fixture signature; crypto is covered by test_release_signing')
        self.events.append('sign')
        return subprocess.CompletedProcess(command, 0)

    def github(self, *args):
        if args[:2] == ('run', 'download'):
            self.downloads += 1
            directory = Path(args[args.index('--dir') + 1])
            if args[args.index('--name') + 1] == promote_release.ARTIFACT:
                write_package(directory)
            else:
                write_server(directory)
                # The production rollout also requires both runtime plugins.
                metadata = directory / 'server-build-metadata.json'
                value = json.loads(metadata.read_text())
                archive = directory / promote_release.SERVER_ARCHIVE
                with zipfile.ZipFile(archive, 'a') as bundle:
                    for plugin in ('Blast.Cluster', 'Realtime.Redis'):
                        name = f'plugins/{plugin}/plugin.json'
                        bundle.writestr('Blast-Circuit-server-dotnet10/' + name, b'{}')
                        value['files'][name] = hashlib.sha256(b'{}').hexdigest()
                metadata.write_text(json.dumps(value))
                (directory / (archive.name + '.sha256')).write_text(hashlib.sha256(archive.read_bytes()).hexdigest() + '  ' + archive.name)
        elif args[:2] in (('release', 'create'), ('release', 'upload')):
            self.events.append('stage')
            for path in args[3:args.index('--repo')]:
                self.assets[Path(path).name] = Path(path).read_bytes()
        elif args[:2] == ('release', 'download'):
            self.events.append('check-upload')
            destination = Path(args[args.index('--dir') + 1])
            for name, data in self.assets.items():
                (destination / name).write_bytes(b'tampered' if self.corrupt_upload else data)
        elif args[:2] == ('release', 'edit'):
            self.events.append('publish')
            with self.assertRaisesRegex(RuntimeError, 'Another deployment'):
                with locked(self.state):
                    self.fail('Publication must hold the fleet lock')
            if self.fail_publish:
                raise RuntimeError('publication unavailable')
        else:
            self.fail('Unexpected GitHub command: ' + repr(args[:2]))
        return ''

    def host(self, adapter, phase, context):
        self.events.append(phase)
        if phase == 'drain':
            return {name: {'drained': True, 'rooms': 0} for name in TARGETS}

    def identities(self, *args):
        self.events.append('identity')
        if self.stale_fleet:
            raise RuntimeError('stale fleet')
        return [{'node': name} for name in TARGETS]

    def connections(self, *args):
        self.events.append('match')
        return ['fallback']

    def test_upload_is_verified_before_drain_and_one_match_cycle_precedes_publication(self):
        deploy_release.main(self.args)
        self.assertEqual(['sign', 'stage', 'check-upload', 'drain', 'apply', 'identity', 'resume', 'match', 'identity', 'publish'], self.events)
        self.assertEqual(2, self.downloads, 'download client/server once')
        receipt = json.loads((self.root / 'receipt.json').read_text())
        self.assertTrue(receipt['nodes'][1]['roomPlacementDeferred'])

    def test_bad_draft_download_never_touches_servers(self):
        self.corrupt_upload = True
        with self.assertRaisesRegex(RuntimeError, 'Uploaded release bytes differ'):
            deploy_release.main(self.args)
        self.assertNotIn('drain', self.events)
        self.assertNotIn('publish', self.events)

    def test_publication_retry_reuses_completed_deployment_and_retains_fleet_lock(self):
        self.fail_publish = True
        with self.assertRaisesRegex(RuntimeError, 'publication unavailable'):
            deploy_release.main(self.args)
        self.assertEqual('succeeded', Journal(self.state / '12/journal.json').value['status'])
        self.fail_publish = False
        self.events.clear()
        with patch.object(deploy_release, 'require_current_main', side_effect=RuntimeError('main advanced')):
            deploy_release.main(self.args)
        self.assertEqual(['stage', 'check-upload', 'identity', 'match', 'identity', 'publish'], self.events)
        self.assertEqual(2, self.downloads, 'retry must use the durable inspected artifacts')

    def test_retry_against_newer_fleet_cannot_publish_old_candidate(self):
        self.fail_publish = True
        with self.assertRaises(RuntimeError):
            deploy_release.main(self.args)
        self.events.clear()
        self.fail_publish = False
        self.stale_fleet = True
        with self.assertRaisesRegex(RuntimeError, 'stale fleet'):
            deploy_release.main(self.args)
        self.assertNotIn('publish', self.events)
        self.assertNotIn('apply', self.events)


if __name__ == '__main__':
    unittest.main()

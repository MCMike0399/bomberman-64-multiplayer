"""Public tests for CI publication and credential-handling boundaries."""
import contextlib
import hashlib
import importlib.util
import io
import os
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile


def module(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + '.py'))
    loaded = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loaded)
    return loaded


bootstrap = module('bootstrap_kernel')
installer = module('install_godot')
staging = module('stage_reports')


class PublicationBoundaryTests(unittest.TestCase):
    def test_staging_rejects_ancestor_destination_without_deleting_receipts(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / 'qa'
            source.mkdir()
            receipt = source / 'summary.json'
            receipt.write_text('{}')
            with patch.object(sys, 'argv', ['stage', '--source', str(source), '--destination', str(root)]), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit):
                    staging.main()
            self.assertTrue(receipt.is_file())
    def test_staging_excludes_source_logs_hidden_and_escaping_symlinks(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / 'qa'
            source.mkdir()
            for name in ('summary.json', 'junit.xml', 'capture.png', 'runtime.log', 'private.cs'):
                (source / name).write_text('example')
            (source / '.private').mkdir()
            (source / '.private/secret.json').write_text('secret')
            (root / 'secret.json').write_text('secret')
            (source / 'escape.json').symlink_to(root / 'secret.json')
            destination = root / 'upload'
            with patch.object(sys, 'argv', ['stage', '--source', str(source), '--destination', str(destination)]), contextlib.redirect_stdout(io.StringIO()):
                staging.main()
            self.assertEqual({'summary.json', 'junit.xml', 'capture.png'}, {item.name for item in destination.iterdir()})

    def test_runtime_log_opt_in_never_includes_build_or_bootstrap_logs(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);source=root/'qa';source.mkdir();destination=root/'upload'
            for name in ('runtime.log','build.log','bootstrap.log','private.cs'):(source/name).write_text('example')
            with patch.object(sys,'argv',['stage','--source',str(source),'--destination',str(destination),'--include-runtime-logs']),contextlib.redirect_stdout(io.StringIO()):staging.main()
            self.assertEqual({row.name for row in destination.iterdir()},{'runtime.log'})

    def test_installer_rejects_checksum_mismatch_before_extraction(self):
        with tempfile.TemporaryDirectory() as temporary:
            destination = Path(temporary) / 'editor'
            def fake_download(url, target):
                Path(target).write_bytes(b'not an authenticated archive')
            with patch.object(installer.urllib.request, 'urlretrieve', side_effect=fake_download):
                with self.assertRaisesRegex(RuntimeError, 'checksum mismatch'):
                    installer.extract('mono_linux_x86_64.zip', destination)
            self.assertFalse(destination.exists())

    def test_installer_rejects_archive_traversal_even_with_matching_hash(self):
        with tempfile.TemporaryDirectory() as temporary:
            archive = Path(temporary) / 'input.zip'
            with zipfile.ZipFile(archive, 'w') as stream:
                stream.writestr('../outside', 'forbidden')
            def fake_download(url, target):
                Path(target).write_bytes(archive.read_bytes())
            digest = hashlib.sha512(archive.read_bytes()).hexdigest()
            with patch.object(installer.urllib.request, 'urlretrieve', side_effect=fake_download), patch.dict(installer.HASHES, {'test.zip': digest}):
                with self.assertRaisesRegex(RuntimeError, 'Unsafe archive'):
                    installer.extract('test.zip', Path(temporary) / 'editor')
            self.assertFalse((Path(temporary) / 'outside').exists())


class CredentialBoundaryTests(unittest.TestCase):
    def test_metadata_token_authenticates_only_https_and_never_reaches_git_or_output(self):
        for rejected in (False, True):
            with self.subTest(git_rejected=rejected), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                metadata_token = 'fixture-metadata-token'
                requests = []
                def metadata(request, timeout):
                    requests.append(request)
                    self.assertEqual('https://api.github.com/meta', request.full_url)
                    self.assertEqual('Bearer ' + metadata_token, request.get_header('Authorization'))
                    return io.BytesIO(b'{"ssh_keys":["ssh-ed25519 example"]}')
                def check_environment(command, environment):
                    self.assertNotIn('GITHUB_METADATA_TOKEN', environment)
                    self.assertNotIn(metadata_token, ' '.join(command))
                def fake_git(command, **kwargs):
                    check_environment(command, kwargs['env'])
                    ssh = shlex.split(kwargs['env']['GIT_SSH_COMMAND'])
                    key = Path(ssh[ssh.index('-i') + 1])
                    for path in key.parent.iterdir():
                        self.assertNotIn(metadata_token, path.read_text())
                    if command[1] == 'init':
                        (root / 'burbuja-kernel').mkdir()
                    return subprocess.CompletedProcess(command, 128 if rejected else 0,
                        stderr=('private diagnostic ' + metadata_token).encode())
                def revision(command, **kwargs):
                    check_environment(command, kwargs['env'])
                    return bootstrap.REVISION + '\n'
                environment = {'KERNEL_REPOSITORY': 'private-owner/private-name',
                    'KERNEL_DEPLOY_KEY': 'fixture-private-key', 'GITHUB_METADATA_TOKEN': metadata_token}
                output = io.StringIO()
                with patch.object(bootstrap, 'ROOT', root), patch.dict(os.environ, environment, clear=True), \
                        patch.object(bootstrap.urllib.request, 'urlopen', side_effect=metadata), \
                        patch.object(bootstrap.subprocess, 'run', side_effect=fake_git), \
                        patch.object(bootstrap.subprocess, 'check_output', side_effect=revision), \
                        contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
                    if rejected:
                        with self.assertRaises(SystemExit) as failure:
                            bootstrap.main()
                        self.assertNotIn(metadata_token, str(failure.exception))
                    else:
                        bootstrap.main()
                self.assertEqual(1, len(requests))
                self.assertNotIn(metadata_token, output.getvalue())

    def test_unadvertised_pin_fallback_never_substitutes_branch_tip(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            calls = []
            def fake_git(command, **kwargs):
                calls.append(command)
                if command[1] == 'init':
                    (root / 'burbuja-kernel').mkdir()
                if '--depth=1' in command:
                    return subprocess.CompletedProcess(command, 128, stderr=b'fatal: not our ref')
                return subprocess.CompletedProcess(command, 0, stderr=b'')
            environment = {'KERNEL_REPOSITORY': 'private-owner/private-name', 'KERNEL_DEPLOY_KEY': 'fixture-key'}
            metadata = io.BytesIO(b'{"ssh_keys":["ssh-ed25519 example"]}')
            with patch.object(bootstrap, 'ROOT', root), patch.dict(os.environ, environment, clear=True), patch.object(bootstrap.urllib.request, 'urlopen', return_value=metadata), patch.object(bootstrap.subprocess, 'run', side_effect=fake_git), patch.object(bootstrap.subprocess, 'check_output', return_value=bootstrap.REVISION + '\n'), contextlib.redirect_stdout(io.StringIO()):
                bootstrap.main()
            self.assertTrue(any('+refs/heads/*:refs/remotes/bootstrap/*' in command for command in calls))
            checkout = calls[-1]
            self.assertEqual(bootstrap.REVISION, checkout[-1])
            self.assertNotIn('FETCH_HEAD', checkout)

    def test_safe_failure_categories_never_reflect_private_diagnostics(self):
        examples = [(b'git@private.example: Permission denied (publickey). secret-owner/repo', 'authentication'),
                    (b'Load key /secret/key: error in libcrypto; secret-owner/repo', 'format'),
                    (b'PRIVATE-SECRET unknown diagnostic secret-owner/repo', 'recognized safe diagnostic')]
        for message, expected in examples:
            with self.subTest(message=message):
                result = bootstrap.failure_category(message)
                self.assertIn(expected, result)
                self.assertNotIn('secret', result.lower())
                self.assertNotIn('private.example', result)

    def test_missing_credentials_fail_before_running_git(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(bootstrap.subprocess, 'run') as run:
            with self.assertRaises(SystemExit):
                bootstrap.main()
            run.assert_not_called()

    def test_ssh_key_is_temporary_and_not_in_git_arguments_or_output(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            keys = []
            arguments = []
            def fake_git(command, **kwargs):
                arguments.extend(command)
                if command[1] == 'init':
                    (root / 'burbuja-kernel').mkdir()
                ssh = shlex.split(kwargs['env']['GIT_SSH_COMMAND'])
                self.assertEqual(os.devnull, ssh[ssh.index('-F') + 1])
                self.assertEqual(os.devnull, kwargs['env']['GIT_CONFIG_GLOBAL'])
                self.assertEqual('1', kwargs['env']['GIT_CONFIG_NOSYSTEM'])
                key = Path(ssh[ssh.index('-i') + 1])
                self.assertEqual('private-key-material\n', key.read_text())
                self.assertEqual(0o600, key.stat().st_mode & 0o777)
                keys.append(key)
                return subprocess.CompletedProcess(command, 0)
            metadata = io.BytesIO(b'{"ssh_keys":["ssh-ed25519 example"]}')
            output = io.StringIO()
            environment = {'KERNEL_REPOSITORY': 'private-owner/private-name', 'KERNEL_DEPLOY_KEY': 'private-key-material'}
            with patch.object(bootstrap, 'ROOT', root), patch.dict(os.environ, environment, clear=True), patch.object(bootstrap.urllib.request, 'urlopen', return_value=metadata), patch.object(bootstrap.subprocess, 'run', side_effect=fake_git), patch.object(bootstrap.subprocess, 'check_output', return_value=bootstrap.REVISION + '\n'), contextlib.redirect_stdout(output):
                bootstrap.main()
            self.assertTrue(keys)
            self.assertTrue(all(not key.exists() for key in keys))
            self.assertNotIn('private-key-material', ' '.join(arguments))
            self.assertNotIn('private-owner', output.getvalue())
            self.assertTrue((root / 'burbuja-kernel/.gdignore').is_file())


if __name__ == '__main__':
    unittest.main()

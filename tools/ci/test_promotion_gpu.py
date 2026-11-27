"""Promotion boundary tests using real archive/receipt fixtures, without AWS."""
import copy
import json
import os
from pathlib import Path
import tempfile
import subprocess
import unittest
from unittest.mock import patch

import desktop_packages
import download_formats
import gpu_release_gates as gpu
import promote_release as promotion
import release_gpu_policy as policy
from test_gpu_release_gates import receipt,cleanup
from test_promotion import RELEASE,SHA,FINGERPRINT,write_package,write_server


class GithubFailureDiagnostics(unittest.TestCase):
    def test_error_retains_bounded_stderr_without_credentials(self):
        secrets=('fixture-auth-token','fixture-signing-key','ghp_unlistedcredential','github_pat_unlistedcredential',
                 'encoded-auth','signed-url-token')
        stderr=('file exists: Sancus-builder-cleanup.json; '+secrets[0]+' '+secrets[1]+' '+secrets[2]+' '+secrets[3]+
                '\nAuthorization: Bearer '+secrets[4]+'\nhttps://example.invalid/?sig='+secrets[5]+'&other=value\n'+'x'*3000)
        result=subprocess.CompletedProcess([],1,stdout='',stderr=stderr)
        with patch.dict(os.environ,{'GH_TOKEN':secrets[0],'BLAST_UPDATE_SIGNING_KEY':secrets[1]}),patch.object(
                promotion.subprocess,'run',return_value=result),self.assertRaises(RuntimeError) as caught:
            promotion.gh('run','download','123')
        message=str(caught.exception)
        self.assertIn('file exists: Sancus-builder-cleanup.json',message)
        self.assertIn('<redacted>',message)
        for secret in secrets:self.assertNotIn(secret,message)
        self.assertLessEqual(len(message),1550)

    def test_timed_out_transfer_retries_exact_artifact_without_merging_partial_files(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            seen = []

            def transfer(*command):
                seen.append(command[:-1])
                stage = Path(command[-1])
                self.assertEqual([], list(stage.iterdir()))
                if len(seen) == 1:
                    (stage / 'incomplete.zip').write_bytes(b'partial')
                    raise subprocess.TimeoutExpired(command, 900)
                (stage / 'package.zip').write_bytes(b'tested package')

            with patch.object(promotion, 'github_command', side_effect=transfer), patch.object(promotion.time, 'sleep'):
                promotion.gh('run', 'download', '12', '--repo', 'example/game', '--name', 'package', '--dir', str(root))
            self.assertEqual([seen[0], seen[0]], seen)
            self.assertEqual({'package.zip': b'tested package'}, {file.name: file.read_bytes() for file in root.iterdir()})

    def test_permanent_failure_and_conflicting_bytes_do_not_overwrite_release_inputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            package = root / 'package.zip'
            package.write_bytes(b'original')
            command = ('run', 'download', '12', '--repo', 'example/game', '--name', 'package', '--dir', str(root))
            with patch.object(promotion, 'github_command', side_effect=RuntimeError('HTTP 404')) as transfer:
                with self.assertRaisesRegex(RuntimeError, 'HTTP 404'):
                    promotion.gh(*command)
                transfer.assert_called_once()

            def transfer_with_conflict(*command):
                (Path(command[-1]) / package.name).write_bytes(b'wrong input')

            with patch.object(promotion, 'github_command', side_effect=transfer_with_conflict):
                with self.assertRaisesRegex(RuntimeError, 'disagree'):
                    promotion.gh(*command)
            self.assertEqual(b'original', package.read_bytes())
            self.assertEqual([package], list(root.iterdir()))

    def test_transient_error_retries_once_and_persistent_timeout_surfaces(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(promotion.time, 'sleep'):
            command = ('run', 'download', '12', '--repo', 'example/game', '--name', 'package', '--dir', temporary)
            with patch.object(promotion, 'github_command', side_effect=[
                    RuntimeError('HTTP 502'), subprocess.TimeoutExpired(command, 900)]) as transfer:
                with self.assertRaises(subprocess.TimeoutExpired):
                    promotion.gh(*command)
            self.assertEqual(2, transfer.call_count)
            self.assertEqual([], list(Path(temporary).iterdir()))


class PromotionPreflight(unittest.TestCase):
    def test_automatic_route_skips_a_superseded_verification_before_gpu_lookup(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / 'workflow-output'
            with (patch.dict(os.environ, {'GITHUB_OUTPUT': str(output)}),
                  patch.object(promotion, 'inspect_run', return_value=SHA),
                  patch.object(promotion, 'current_main', return_value='b' * 40),
                  patch.object(promotion, 'check_promotion_gpu') as gpu_check):
                promotion.main(['--check-only', '--automatic', '--run-id', '12', '--version', '0.47.0',
                                '--repository', 'example/game'])
            gpu_check.assert_not_called()
            self.assertIn('publish=false', output.read_text().splitlines())

    def test_explicit_durable_retry_checks_the_verification_run_without_fetching_gpu_artifacts(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / 'workflow-output'
            with (patch.dict(os.environ, {'GITHUB_OUTPUT': str(output)}),
                  patch.object(promotion, 'inspect_run', return_value=SHA) as verification,
                  patch.object(promotion, 'gh') as github):
                promotion.main(['--check-only', '--retry-deployed', '--run-id', '12', '--version', '0.47.0',
                                '--repository', 'example/game'])
            verification.assert_called_once_with('example/game', '12')
            github.assert_not_called()
            self.assertIn(f'revision={SHA}', output.read_text().splitlines())

    def test_automatic_route_skips_major_and_accepts_patch_without_gpu_queries(self):
        stable = json.dumps([[{'tag_name': 'v0.46.0'}, {'tag_name': 'v0.47.0', 'draft': True}]])
        with patch.object(promotion, 'gh', return_value=stable) as github, patch.dict(os.environ, {'BLAST_RELEASE_KIND_OVERRIDE': ''}):
            self.assertFalse(promotion.check_promotion_gpu('example/game', '0.47.0', SHA, '', automatic=True))
            self.assertTrue(promotion.check_promotion_gpu('example/game', '0.46.1', SHA, '', automatic=True))
            self.assertTrue(all('/releases?' in call.args[1] for call in github.call_args_list))

    def test_manual_major_requires_successful_exact_main_gpu_run(self):
        stable = json.dumps([[{'tag_name': 'v0.46.0'}]])
        gpu_run = {'head_repository': {'full_name': 'example/game'}, 'head_sha': SHA, 'head_branch': 'main',
                   'path': '.github/workflows/gpu-qualification.yml', 'event': 'workflow_dispatch',
                   'status': 'completed', 'conclusion': 'success'}
        qualified = [{'name': gpu.artifact_name(target)} for target in desktop_packages.TARGETS]
        with patch.dict(os.environ, {'BLAST_RELEASE_KIND_OVERRIDE': ''}):
            with patch.object(promotion, 'gh', return_value=stable):
                with self.assertRaisesRegex(RuntimeError, 'explicit GPU qualification run ID'):
                    promotion.check_promotion_gpu('example/game', '0.47.0', SHA, '')
            for changes in ({}, {'head_sha': 'b' * 40}, {'conclusion': 'failure'}):
                with self.subTest(changes=changes), patch.object(promotion, 'gh', side_effect=[
                        stable, json.dumps({**gpu_run, **changes}), json.dumps([{'artifacts': qualified}])]):
                    if changes:
                        with self.assertRaisesRegex(RuntimeError, 'exact trusted main SHA'):
                            promotion.check_promotion_gpu('example/game', '0.47.0', SHA, '34')
                    else:
                        self.assertTrue(promotion.check_promotion_gpu('example/game', '0.47.0', SHA, '34'))
            with patch.object(promotion, 'gh', side_effect=[stable, json.dumps(gpu_run), json.dumps([{'artifacts': qualified[:1]}])]):
                with self.assertRaisesRegex(RuntimeError, 'unexpired artifact for linux-x64'):
                    promotion.check_promotion_gpu('example/game', '0.47.0', SHA, '34')


def write_gpu_files(directory):
    cleanup_path=directory/gpu.CLEANUP_RECEIPT
    cleanup_path.write_text(json.dumps(cleanup()))
    for target in desktop_packages.TARGETS:
        data=copy.deepcopy(receipt(target))
        values={'archiveSha256':desktop_packages.digest(directory/desktop_packages.names(target)['archive']),
                'downloadSha256':desktop_packages.digest(directory/download_formats.FILES[target]),
                'serverSha256':desktop_packages.digest(directory/promotion.SERVER_ARCHIVE)}
        data.update(values,renderFingerprint=FINGERPRINT)
        data['cleanupSha256']=desktop_packages.digest(cleanup_path)
        data['primaryHigh'].update({key:value for key,value in values.items()if key!='serverSha256'})
        data['graphicalSha256']=desktop_packages.digest(directory/desktop_packages.names(target)['graphical'])
        (directory/gpu.receipt_name(target)).write_text(json.dumps(data))


def write_hybrid_gpu_files(directory):
    """Hybrid qualification (2026-11-13): Windows on the Sancus AWS builder bound to its cleanup
    receipt, Linux on the owned workstation bound to its host receipt."""
    write_gpu_files(directory)
    host={'schemaVersion':1,'source':SHA,'host':gpu.WORKSTATION_HOST,'runId':'22','runnerMode':'jit-single-use',
          'jobs':[{'target':'linux-x64','jobId':2,'runnerName':'blast-gpu-linux-2','labels':['self-hosted','blast-gpu','Linux','X64'],
                   'conclusion':'success'}],'devices':['NVIDIA GeForce RTX 2080 SUPER'],'passed':True,'errors':[]}
    host_path=directory/gpu.HOST_RECEIPT;host_path.write_text(json.dumps(host))
    path=directory/gpu.receipt_name('linux-x64');data=json.loads(path.read_text())
    for key in ('cleanupAudited','cleanupSha256','campaignRunId'):data.pop(key)
    data.update(qualificationHost='workstation',hostVerified=True,qualificationRunId='22',
                hostReceiptSha256=desktop_packages.digest(host_path))
    path.write_text(json.dumps(data))


class PromotionGraphicsPolicy(unittest.TestCase):
    def setUp(self):
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
        self.root=Path(temporary.name)
        write_package(self.root);write_server(self.root)
        self.github=self.enterContext(patch.object(promotion,'gh',return_value=''))
        self.enterContext(patch.object(promotion,'verify_uploaded'))
        self.enterContext(patch.object(promotion,'check_exports',return_value=RELEASE))
        self.enterContext(patch.dict(os.environ,{'BLAST_GPU_QUALIFICATION_RUN_ID':'22'}))

    def stage(self,decision):
        with patch.object(policy,'derive',return_value=decision):
            return promotion.stage_draft('example/game','12',RELEASE['version'],SHA,self.root,False,False)

    def major(self):return policy.decide('0.14.9',RELEASE['version'],FINGERPRINT,FINGERPRINT)

    def gpu_files(self):
        write_gpu_files(self.root)

    def test_major_without_gpu_receipt_is_refused_before_upload(self):
        with self.assertRaisesRegex(RuntimeError,'Missing mandatory GPU'):self.stage(self.major())
        self.github.assert_not_called()

    def test_major_exact_gpu_receipts_are_attached_with_policy(self):
        self.gpu_files();self.stage(self.major())
        call=self.github.call_args_list[0].args
        self.assertIn(str(self.root/policy.RECEIPT),call)
        for target in desktop_packages.TARGETS:self.assertIn(str(self.root/gpu.receipt_name(target)),call)

    def test_legacy_aws_shape_attaches_only_the_shared_cleanup_receipt(self):
        self.gpu_files();self.stage(self.major())
        call=self.github.call_args_list[0].args
        self.assertIn(str(self.root/gpu.CLEANUP_RECEIPT),call);self.assertNotIn(str(self.root/gpu.HOST_RECEIPT),call)

    def test_hybrid_attaches_both_bound_receipts_once(self):
        write_hybrid_gpu_files(self.root);self.stage(self.major())
        call=self.github.call_args_list[0].args
        for name in (gpu.CLEANUP_RECEIPT,gpu.HOST_RECEIPT):self.assertEqual(call.count(str(self.root/name)),1)
        for target in desktop_packages.TARGETS:self.assertIn(str(self.root/gpu.receipt_name(target)),call)

    def test_hybrid_without_the_linux_host_receipt_is_refused_before_upload(self):
        write_hybrid_gpu_files(self.root);(self.root/gpu.HOST_RECEIPT).unlink()
        with self.assertRaisesRegex(RuntimeError,'host receipt'):self.stage(self.major())
        self.github.assert_not_called()

    def test_revalidation_never_rewrites_a_prepared_signed_policy(self):
        self.gpu_files();decision=self.major();self.stage(decision)
        path=self.root/policy.RECEIPT
        # A valid signed serialization is immutable even if formatting differs.
        raw=json.dumps(json.loads(path.read_text()),sort_keys=True).encode()
        path.write_bytes(raw)
        self.stage(decision)
        self.assertEqual(path.read_bytes(),raw)
        changed={**decision,'previousFingerprint':'changed'}
        self.github.reset_mock()
        with self.assertRaisesRegex(RuntimeError,'changed after preparation'):self.stage(changed)
        self.github.assert_not_called()
        self.assertEqual(path.read_bytes(),raw)

    def test_major_wrong_hash_source_or_software_is_refused_before_upload(self):
        for field,value in [('source','b'*40),('archiveSha256','wrong'),('downloadSha256','wrong'),
                            ('serverSha256','wrong'),('adapter','Microsoft WARP')]:
            self.gpu_files()
            path=self.root/gpu.receipt_name('windows-x64');data=json.loads(path.read_text())
            if field=='adapter':data['devices'][0]['adapter']=value
            else:data[field]=value
            path.write_text(json.dumps(data))
            with self.subTest(field=field),self.assertRaises(RuntimeError):self.stage(self.major())
            self.github.assert_not_called()

    def test_major_missing_or_failed_cleanup_is_refused_before_upload(self):
        self.gpu_files();path=self.root/gpu.CLEANUP_RECEIPT;path.unlink()
        with self.assertRaisesRegex(RuntimeError,'cleanup receipt'):self.stage(self.major())
        self.github.assert_not_called()
        self.gpu_files();data=json.loads(path.read_text());data['checked']['EBS']['state']='present'
        path.write_text(json.dumps(data))
        for target in desktop_packages.TARGETS:
            receipt_path=self.root/gpu.receipt_name(target);data=json.loads(receipt_path.read_text())
            data['cleanupSha256']=desktop_packages.digest(path);receipt_path.write_text(json.dumps(data))
        with self.assertRaisesRegex(RuntimeError,'cleanup is incomplete'):self.stage(self.major())
        self.github.assert_not_called()

    def test_patch_with_changed_rendering_needs_no_gpu_or_graphical_receipt(self):
        with patch.dict(RELEASE,{'version':'0.15.1','admissionToken':'4/0.15.1/10'}):
            write_package(self.root);write_server(self.root)
            for target in desktop_packages.TARGETS:(self.root/desktop_packages.names(target)['graphical']).unlink()
            decision=policy.decide('0.15.0','0.15.1','old-rendering',FINGERPRINT)
            self.assertIn('minor bump',decision['warning'])
            self.stage(decision)
            stored=policy.read(self.root)
            self.assertFalse(stored['requiresGpu'])
            self.assertIn('minor bump',stored['warning'])
            call=self.github.call_args_list[0].args
            self.assertNotIn(str(self.root/gpu.receipt_name('windows-x64')),call)

    def test_patch_still_requires_every_functional_package_receipt(self):
        decision=policy.decide('0.14.9',RELEASE['version'],FINGERPRINT,FINGERPRINT,
                               {'version':RELEASE['version'],'kind':'patch'})
        for target,key in [('windows-x64','updater_test'),('windows-x64','startup_guard'),('linux-x64','smoke')]:
            write_package(self.root);write_server(self.root)
            (self.root/desktop_packages.names(target)[key]).unlink()
            with self.subTest(target=target,key=key),self.assertRaises((RuntimeError,FileNotFoundError)):
                self.stage(decision)
            self.github.assert_not_called()


if __name__=='__main__':unittest.main()

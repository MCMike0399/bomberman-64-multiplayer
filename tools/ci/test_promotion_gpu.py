"""Promotion boundary tests using real archive/receipt fixtures, without AWS."""
import copy
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import desktop_packages
import download_formats
import gpu_release_gates as gpu
import promote_release as promotion
import release_gpu_policy as policy
from test_gpu_release_gates import receipt,cleanup
from test_promotion import RELEASE,SHA,FINGERPRINT,write_package,write_server


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

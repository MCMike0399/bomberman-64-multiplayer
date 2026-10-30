import hashlib
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import urllib.error

import install_godot


class OfflineEngine(unittest.TestCase):
    def test_transient_reset_discards_partial_bytes_and_retries_the_pinned_download(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);source=root/'source.zip'
            with zipfile.ZipFile(source,'w')as output:output.writestr('engine/fixture',b'pinned')
            data=source.read_bytes();calls=[]
            def retrieve(url,destination):
                calls.append(url)
                if len(calls)==1:
                    Path(destination).write_bytes(b'partial')
                    raise urllib.error.URLError(ConnectionResetError(104,'Connection reset by peer'))
                Path(destination).write_bytes(data)
            with patch.dict(install_godot.HASHES,{'mono_win64.zip':hashlib.sha512(data).hexdigest()}),patch.object(install_godot.urllib.request,'urlretrieve',side_effect=retrieve),patch.object(install_godot.time,'sleep'):
                install_godot.extract('mono_win64.zip',root/'out')
            self.assertEqual(len(calls),2);self.assertEqual((root/'out/engine/fixture').read_bytes(),b'pinned')

    def test_transport_retries_are_finite(self):
        with tempfile.TemporaryDirectory() as root:
            error = urllib.error.URLError(ConnectionResetError(104, 'reset'))
            with patch.object(install_godot.urllib.request, 'urlretrieve', side_effect=error) as download, patch.object(install_godot.time, 'sleep'):
                with self.assertRaises(urllib.error.URLError):
                    install_godot.extract('mono_win64.zip', Path(root)/'out')
            self.assertEqual(download.call_count, 3)

    def test_http_and_certificate_failures_are_not_retried(self):
        import ssl
        errors = [urllib.error.HTTPError('https://example.invalid', 404, 'missing', {}, None),
                  urllib.error.URLError(ssl.SSLCertVerificationError('invalid certificate'))]
        for error in errors:
            with self.subTest(error=type(error).__name__), tempfile.TemporaryDirectory() as root:
                with patch.object(install_godot.urllib.request, 'urlretrieve', side_effect=error) as download:
                    with self.assertRaises(urllib.error.URLError):
                        install_godot.extract('mono_win64.zip', Path(root)/'out')
                self.assertEqual(download.call_count, 1)
                if isinstance(error, urllib.error.HTTPError):
                    error.close()

    def test_download_checksum_failure_is_not_retried(self):
        with tempfile.TemporaryDirectory() as root:
            def corrupt(url, destination):
                Path(destination).write_bytes(b'corrupt')
            with patch.object(install_godot.urllib.request, 'urlretrieve', side_effect=corrupt) as download:
                with self.assertRaisesRegex(RuntimeError, 'checksum'):
                    install_godot.extract('mono_win64.zip', Path(root)/'out')
            self.assertEqual(download.call_count, 1)

    def test_staged_archive_uses_the_same_hash_without_network_access(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);archive=root/f'Godot_v{install_godot.VERSION}-stable_mono_win64.zip'
            with zipfile.ZipFile(archive,'w')as output:output.writestr('engine/fixture','pinned')
            digest=hashlib.sha512(archive.read_bytes()).hexdigest()
            with patch.dict(install_godot.HASHES,{'mono_win64.zip':digest}),patch.object(install_godot.urllib.request,'urlretrieve',side_effect=AssertionError('Network forbidden')):
                install_godot.extract('mono_win64.zip',root/'out',root)
            self.assertEqual((root/'out/engine/fixture').read_text(),'pinned')

    def test_corrupt_staged_archive_never_extracts(self):
        with tempfile.TemporaryDirectory()as root:
            root=Path(root);archive=root/f'Godot_v{install_godot.VERSION}-stable_mono_win64.zip'
            archive.write_bytes(b'corrupt')
            with patch.object(install_godot.urllib.request,'urlretrieve',side_effect=AssertionError('Network forbidden')):
                with self.assertRaisesRegex(RuntimeError,'checksum'):install_godot.extract('mono_win64.zip',root/'out',root)
            self.assertFalse((root/'out').exists())


if __name__=='__main__':unittest.main()

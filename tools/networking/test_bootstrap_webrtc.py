import hashlib
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

spec=importlib.util.spec_from_file_location('bootstrap_webrtc',Path(__file__).with_name('bootstrap_webrtc.py'))
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)

class BootstrapChecks(unittest.TestCase):
    def archive(self, name):
        result=io.BytesIO()
        with zipfile.ZipFile(result,'w') as z:z.writestr(name,b'library fixture')
        return result.getvalue()

    def test_verified_cache_repairs_modified_library(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);payload=self.archive('addons/webrtc_native/lib/fixture.dylib')
            archive=root/'artifacts/dependencies/webrtc-native.zip';archive.parent.mkdir(parents=True);archive.write_bytes(payload)
            registry=root/'.godot/extension_list.cfg';registry.parent.mkdir();registry.write_text('res://addons/existing/plugin.gdextension\n')
            with patch.object(module,'ROOT',root),patch.object(module,'SHA256',hashlib.sha256(payload).hexdigest()),patch.object(module.urllib.request,'urlopen',side_effect=AssertionError('must use verified cache')):
                module.install();library=root/'addons/webrtc_native/lib/fixture.dylib';library.write_bytes(b'modified');module.install()
                self.assertEqual(library.read_bytes(),b'library fixture')
                self.assertEqual(registry.read_text().splitlines(),['res://addons/existing/plugin.gdextension','res://addons/webrtc_native/webrtc_native.gdextension'])

    def test_archive_cannot_escape_extension_directory(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);payload=self.archive('addons/webrtc_native/../../escape')
            with patch.object(module,'ROOT',root),patch.object(module,'SHA256',hashlib.sha256(payload).hexdigest()),patch.object(module.urllib.request,'urlopen',return_value=io.BytesIO(payload)):
                with self.assertRaisesRegex(RuntimeError,'Unexpected'):module.install()
                self.assertFalse((root/'escape').exists())

    def test_bad_download_is_not_installed(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            with patch.object(module,'ROOT',root),patch.object(module.urllib.request,'urlopen',return_value=io.BytesIO(b'corrupt')):
                with self.assertRaisesRegex(RuntimeError,'checksum'):module.install()
                self.assertFalse((root/'addons').exists())

if __name__=='__main__':unittest.main()

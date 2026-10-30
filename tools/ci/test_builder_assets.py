import hashlib
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import builder_assets as assets


class FrozenTools(unittest.TestCase):
    def test_python_cache_supplies_the_actual_installer_without_running_vendor_setup(self):
        stream=io.BytesIO()
        with zipfile.ZipFile(stream,'w')as archive:archive.writestr('python.exe',b'MZ frozen installer')
        data=stream.getvalue()
        spec={'role':'python','target':'windows-x64','fileName':'python.exe','member':'python.exe',
              'url':'https://vendor.invalid/cache.zip','algorithm':'sha256','hash':hashlib.sha256(data).hexdigest()}
        with tempfile.TemporaryDirectory()as root,patch.object(assets.urllib.request,'urlopen',return_value=io.BytesIO(data)):
            result=assets.fetch(spec,root);self.assertEqual(Path(result['path']).read_bytes(),b'MZ frozen installer')

    def test_corrupt_archive_is_rejected_before_extracting(self):
        spec={'role':'git','target':'windows-x64','fileName':'git.exe','url':'https://vendor.invalid/tool','algorithm':'sha256','hash':'0'*64}
        with tempfile.TemporaryDirectory()as root,patch.object(assets.urllib.request,'urlopen',return_value=io.BytesIO(b'bad')):
            with self.assertRaises(ValueError):assets.fetch(spec,root)
            self.assertFalse((Path(root)/'git.exe').exists())

    def test_sdk_metadata_must_contain_both_pinned_platforms(self):
        files=[{'rid':rid,'url':'https://builds.dotnet.microsoft.com/'+name,'hash':'a'*128}for rid,name in [('win-x64','sdk.zip'),('linux-x64','sdk.tar.gz')]]
        metadata={'releases':[{'sdk':{'version':assets.SDK_VERSION,'files':files}}]}
        self.assertEqual({item['target']for item in assets.sdk_specs(metadata)},{'windows-x64','linux-x64'})
        metadata['releases'][0]['sdk']['version']='other'
        with self.assertRaises(ValueError):assets.sdk_specs(metadata)

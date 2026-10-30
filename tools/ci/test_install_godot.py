import hashlib
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import install_godot


class OfflineEngine(unittest.TestCase):
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

import tempfile
from pathlib import Path
import unittest
import frozen_effect_control as control


class OverlayOwnershipTests(unittest.TestCase):
    def test_overlay_removes_only_its_exact_file_and_leaves_executable_unchanged(self):
        with tempfile.TemporaryDirectory() as name:
            root=Path(name);install=root/'installed';install.mkdir();exe=install/'game.exe';exe.write_bytes(b'frozen executable')
            output=root/'output';output.mkdir()
            with control.overlay(exe,output,'ssil'):
                self.assertTrue((install/'override.cfg').is_file())
                self.assertEqual(exe.read_bytes(),b'frozen executable')
            self.assertFalse((install/'override.cfg').exists())
            self.assertEqual(exe.read_bytes(),b'frozen executable')

    def test_existing_override_is_protected_and_changed_ownership_blocks_removal(self):
        with tempfile.TemporaryDirectory() as name:
            root=Path(name);exe=root/'game.exe';exe.write_bytes(b'frozen');output=root/'output';output.mkdir()
            path=root/'override.cfg';path.write_text('existing')
            with self.assertRaises(ValueError):
                with control.overlay(exe,output,'ssil'): pass
            self.assertEqual(path.read_text(),'existing');path.unlink()
            with self.assertRaises(RuntimeError):
                with control.overlay(exe,output,'ssil'):path.write_text('other owner')
            self.assertEqual(path.read_text(),'other owner')


if __name__=='__main__':unittest.main()

"""Exercise the real fixture CLI-to-exporter handoff without compiling a game."""
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SPEC=importlib.util.spec_from_file_location('update_fixture',Path(__file__).with_name('package-update-fixture.py'))
fixture=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(fixture)


class ExportReached(Exception):pass


class FixtureEngineSelection(unittest.TestCase):
    def selected(self,target,environment,arguments=()):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary)
            source={'version':'0.45.0','build':'56'}
            original=b'<BlastVersion>0.45.0</BlastVersion><BlastBuild>56</BlastBuild>'
            (root/'Release.props').write_bytes(original)
            (root/'export_presets.cfg').write_bytes(b'original export settings')
            observed=[]
            def run(command,**kwargs):
                if command[:2]==['git','diff']:return subprocess.CompletedProcess(command,0)
                self.assertTrue(str(command[1]).endswith('tools/packaging/package-desktop.py'))
                observed.append(command[command.index('--godot')+1])
                raise ExportReached()
            with patch.object(fixture,'ROOT',root),patch.dict(os.environ,environment,clear=True),\
                    patch.object(sys,'argv',['package-update-fixture.py','--target',target,*arguments]),\
                    patch.object(fixture.release_identity,'check_exports',return_value=source),\
                    patch.object(fixture.signal,'signal'),patch.object(fixture.subprocess,'run',side_effect=run):
                with self.assertRaises(ExportReached):fixture.main()
            self.assertEqual((root/'Release.props').read_bytes(),original)
            self.assertEqual((root/'export_presets.cfg').read_bytes(),b'original export settings')
            return observed[0]

    def test_both_ci_targets_forward_the_installed_engine_even_with_spaces(self):
        for target in ('windows-x64','linux-x64'):
            with self.subTest(target=target):
                self.assertEqual(self.selected(target,{'GODOT':'/owned tool cache/Godot'}),'/owned tool cache/Godot')

    def test_explicit_engine_overrides_environment(self):
        self.assertEqual(self.selected('linux-x64',{'GODOT':'/env/Godot'},('--godot','/explicit/Godot')),'/explicit/Godot')

    def test_local_path_lookup_remains_the_default_without_environment(self):
        self.assertEqual(self.selected('linux-x64',{}),'godot')


if __name__=='__main__':unittest.main()

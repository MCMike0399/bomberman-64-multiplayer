"""Guest command, evidence and cleanup contracts, without allocating or qualifying a GPU."""
import json
from pathlib import Path
import tempfile
import time
import types
import unittest
from unittest.mock import patch

import gpu_campaign


class StageCaptureContracts(unittest.TestCase):
    def exercise(self, adapter='NVIDIA Tesla T4', repeat_code=0):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);output=root/'captures';exe=root/'installed/BlastCircuit.exe'
            exe.parent.mkdir();exe.write_bytes(b'fixture');(exe.parent/'unins000.exe').touch()
            commands=[]

            def launch(command,**kwargs):
                commands.append(command)
                if command[0].endswith('unins000.exe'):return types.SimpleNamespace(returncode=0)
                self.assertEqual(command[1:3],['--rendering-driver','vulkan'])
                self.assertNotIn('--headless',command)
                stage=next(arg.split('=',1)[1]for arg in command if arg.startswith('--parity-stage='))
                self.assertIn(stage,gpu_campaign.OTHER_STAGES)
                self.assertIn('--parity-provenance='+str(root/'provenance.json'),command)
                case=kwargs['cwd']
                (case/'summary.json').write_text(json.dumps({'release':{'revision':'frozen'},'assertions':15}))
                (case/'visual-parity-observations.json').write_text(json.dumps({'adapter':adapter,'driver':'vulkan','deviceType':'DiscreteGpu'}))
                return types.SimpleNamespace(returncode=0)

            with patch.object(gpu_campaign.crossplay_runtime,'install',return_value=exe), \
                 patch.object(gpu_campaign.crossplay_runtime,'validate_summary',return_value=[]), \
                 patch.object(gpu_campaign.subprocess,'run',side_effect=launch), \
                 patch.object(gpu_campaign.visual_parity,'pair',return_value=({},repeat_code)):
                result=gpu_campaign.collect_stage_captures(root,output,'windows-x64','frozen',root/'provenance.json',time.time()+3600)
            self.assertTrue(commands[-1][0].endswith('unins000.exe'))
            self.assertEqual(json.loads((output/'stage-captures.json').read_text()),result)
            return result

    def test_retains_all_scenes_and_fresh_processes_even_when_repeat_verdict_is_red(self):
        result=self.exercise(repeat_code=1)
        self.assertTrue(result['collectionComplete'])
        self.assertFalse(result['repeatsExact'])
        self.assertEqual(len(result['runs']),12)
        self.assertEqual(len(result['comparisons']),30)
        self.assertEqual({(r['stage'],r['index'])for r in result['runs']},
                         {(stage,index)for stage in gpu_campaign.OTHER_STAGES for index in (0,1)})

    def test_software_adapter_is_not_a_hardware_pass_and_owned_install_is_removed(self):
        result=self.exercise(adapter='Microsoft WARP')
        self.assertFalse(result['collectionComplete'])
        self.assertFalse(result['runs'][0]['passed'])
        self.assertTrue(result['errors'])

    def test_cleanup_headroom_prevents_installation(self):
        with tempfile.TemporaryDirectory() as temporary,patch.object(gpu_campaign.crossplay_runtime,'install')as install:
            root=Path(temporary)
            result=gpu_campaign.collect_stage_captures(root,root/'captures','windows-x64','frozen',root/'provenance.json',time.time()+1200)
            install.assert_not_called()
            self.assertFalse(result['collectionComplete'])


if __name__=='__main__':unittest.main()

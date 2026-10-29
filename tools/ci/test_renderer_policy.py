from pathlib import Path
import json
import tempfile
import unittest
import desktop_packages as desktop
import crossplay_runtime
from test_desktop_packages import write_desktop, SHA, RELEASE

ROOT=Path(__file__).resolve().parents[2]

class RendererPolicyTests(unittest.TestCase):
    def test_windows_default_and_consumer_contract_are_vulkan(self):
        self.assertIn('rendering_device/driver.windows="vulkan"',(ROOT/'project.godot').read_text())
        self.assertEqual(desktop.TARGETS['windows-x64']['driver'],'vulkan')
        self.assertEqual(crossplay_runtime.SYSTEMS['windows-x64'][2],'vulkan')

    def test_fallback_keeps_forward_plus_and_packages_d3d12(self):
        config=(ROOT/'project.godot').read_text()
        self.assertIn('rendering_device/fallback_to_d3d12=true',config)
        self.assertIn('rendering_device/fallback_to_opengl3=false',config)
        self.assertIn('application/export_d3d12=1',(ROOT/'export_presets.cfg').read_text())

    def test_stale_primary_metadata_is_rejected(self):
        with tempfile.TemporaryDirectory() as name:
            root=Path(name);write_desktop(root)
            p=root/desktop.names('windows-x64')['metadata'];data=json.loads(p.read_text());data['driver']='d3d12';p.write_text(json.dumps(data))
            with self.assertRaises(RuntimeError):desktop.inspect_archive(root,'windows-x64',SHA,RELEASE)

    def test_fallback_requires_the_requested_api_and_exact_reason(self):
        from desktop_rendering import driver_matches,WINDOWS_FALLBACK_REASON
        self.assertTrue(driver_matches('windows-x64','d3d12','vulkan',WINDOWS_FALLBACK_REASON))
        for requested,reason in [('vulkan',None),('d3d12',WINDOWS_FALLBACK_REASON),('vulkan','unknown')]:
            self.assertFalse(driver_matches('windows-x64','d3d12',requested,reason))
        self.assertFalse(driver_matches('linux-x64','d3d12','vulkan',WINDOWS_FALLBACK_REASON))
        self.assertFalse(driver_matches('windows-x64','opengl3','vulkan',WINDOWS_FALLBACK_REASON))

    def test_actual_update_health_never_silently_accepts_d3d12(self):
        import desktop_update_gates
        from desktop_rendering import WINDOWS_FALLBACK_REASON
        with tempfile.TemporaryDirectory()as name:
            root=Path(name);write_desktop(root);item=desktop.names('windows-x64')
            report=json.loads((root/item['updater_test']).read_text())
            for case in [report['cases'][0],report['cases'][2]]:case['health']['driver']='d3d12'
            with self.assertRaises(RuntimeError):desktop_update_gates.inspect(report,'windows-x64',SHA,RELEASE,desktop.digest(root/item['archive']))
            for case in [report['cases'][0],report['cases'][2]]:
                case['health'].update(requestedDriver='vulkan',fallbackReason=WINDOWS_FALLBACK_REASON)
            desktop_update_gates.inspect(report,'windows-x64',SHA,RELEASE,desktop.digest(root/item['archive']))

if __name__=='__main__':unittest.main()

class PackagedGuardEvidenceTests(unittest.TestCase):
    def test_crash_recovery_cannot_be_omitted_or_silently_relabelled(self):
        import copy
        import desktop_validation as validation
        with tempfile.TemporaryDirectory()as name:
            root=Path(name);write_desktop(root);item=desktop.names('windows-x64')
            report=json.loads((root/item['startup_guard']).read_text());digest=desktop.digest(root/item['archive'])
            validation.inspect_startup_guard(report,SHA,digest)
            for key,value in [('expectedCrashObserved',False),('pendingMarkers',[]),('exitCode',0)]:
                broken=copy.deepcopy(report);broken['runs'][0][key]=value
                with self.assertRaises(RuntimeError):validation.inspect_startup_guard(broken,SHA,digest)
            broken=copy.deepcopy(report);broken['runs'][1]['renderer']['fallbackReason']=None
            with self.assertRaises(RuntimeError):validation.inspect_startup_guard(broken,SHA,digest)

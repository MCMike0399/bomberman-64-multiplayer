import json
import hashlib
import binascii
import zlib
from pathlib import Path
import re
import struct
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from contracts import catalog, expanded, validate_summary, validate_benchmark, junit, ROOT, CATALOG

class PlaytestContracts(unittest.TestCase):
    def test_native_benchmark_cannot_use_fixed_step_vm_or_recording(self):
        from run import engine_timing_args, native_benchmark_error
        self.assertNotIn('--fixed-fps', engine_timing_args('graphics-benchmark'))
        self.assertEqual(['--max-fps', '0', '--disable-vsync'], engine_timing_args('graphics-benchmark'))
        self.assertIn('--fixed-fps', engine_timing_args('all'))
        self.assertIsNone(native_benchmark_error('graphics-benchmark', 'shared', False, False))
        for desktop, headless, record in [('isolated', False, False), ('shared', True, False), ('shared', False, True)]:
            self.assertIsNotNone(native_benchmark_error('graphics-benchmark', desktop, headless, record))
        self.assertIsNone(native_benchmark_error('all', 'isolated', False, False))

    def test_cold_effect_diagnostic_cannot_silently_disable_other_coverage(self):
        r=subprocess.run([sys.executable,str(ROOT/'tools/playtest/run.py'),'--scenario','all','--cold-effects'],capture_output=True,text=True)
        self.assertNotEqual(0,r.returncode)
        self.assertIn('negative first-use-rendering diagnostic',r.stderr)

    def test_native_benchmark_rejects_false_timing_evidence(self):
        sample = dict(samples=100, seconds=3, fps=33.3, frameMedianMs=30, frameP95Ms=31,
                      rootGpuMedianMs=None, rootGpuP95Ms=None, peakBombs=2, peakHazards=3, rootDrawCallsPeak=100)
        receipt = dict(schemaVersion=1, timingMode='realtime-uncapped', gpu='Apple M5 Pro',
                       measurements=[sample | dict(scene=scene, preset=preset)
                                     for preset in ('Low', 'Medium', 'High') for scene in ('menu', 'eight-player-temple')])
        self.assertEqual([], validate_benchmark(receipt))
        for change in [dict(gpu='Apple Paravirtual device'), dict(timingMode='fixed-step-30'), dict(measurements=[])]:
            self.assertTrue(validate_benchmark(receipt | change))
        for change in [dict(samples=0), dict(fps=float('nan')), dict(rootGpuMedianMs=0), dict(peakHazards=0), dict(rootDrawCallsPeak=0)]:
            modified=json.loads(json.dumps(receipt));modified['measurements'][1].update(change)
            self.assertTrue(validate_benchmark(modified))

    def test_native_benchmark_checks_requested_maps_pixels_and_quality(self):
        sample=dict(samples=100,seconds=10,fps=100,frameMedianMs=10,frameP95Ms=11,
                    rootGpuMedianMs=None,rootGpuP95Ms=None,peakBombs=2,peakHazards=2,rootDrawCallsPeak=100,
                    width=3024,height=1964,renderScale=1,preset='High')
        scenes=('menu','eight-player-temple','eight-player-rock-garden','eight-player-up-and-down',
                'eight-player-top-rules','eight-player-blizzard-battle','eight-player-greedy-trap')
        receipt=dict(schemaVersion=1,timingMode='realtime-uncapped',gpu='Apple M5 Pro',stage='all',
                     diagnosticOmission='none',fullQuality=True,requestedWidth=3024,requestedHeight=1964,
                     measurements=[sample|dict(scene=s) for s in scenes])
        options=dict(stage='all',presets=('High',),resolution=(3024,1964))
        self.assertEqual([],validate_benchmark(receipt,**options))
        for change in (dict(stage='sunken-temple'),dict(measurements=receipt['measurements'][:-1]),
                       dict(diagnosticOmission='ssil',fullQuality=False)):
            self.assertTrue(validate_benchmark(receipt|change,**options))
        self.assertEqual([],validate_benchmark(receipt|dict(diagnosticOmission='ssil',fullQuality=False),omission='ssil',**options))
        for change in (dict(width=1920),dict(renderScale=.85)):
            changed=receipt|dict(measurements=[s|change for s in receipt['measurements']])
            self.assertTrue(validate_benchmark(changed,**options))

    def receipt(self, **changes):
        return dict(schemaVersion=1, catalogSha256=hashlib.sha256(CATALOG.read_bytes()).hexdigest(), completedScenarios=['movement'], scenario='movement', passed=True, assertions=1,
                    checks=[{'description':'real check','passed':True}], seconds=1,
                    screenshots=[], screenshotMode='disabled-headless', **changes)

    def test_catalog_resolves_real_methods_and_complete_domains(self):
        scenarios=catalog()
        source='\n'.join(p.read_text() for p in (ROOT/'scripts/testing').rglob('*.cs'))
        methods=set(re.findall(r'private\s+async\s+Task\s+(\w+)\s*\(\s*\)',source))
        for item in scenarios.values():
            if 'method' in item:self.assertIn(item['method'],methods,item['id'])
        for id in expanded('core-headless',scenarios):self.assertTrue(scenarios[id]['headless'])
        self.assertTrue({'assets','models','animation','ui','physics','mechanics','ai','network','input'}<=set(scenarios['all']['domains']))
        self.assertNotIn('controller-observe',expanded('all',scenarios))

    def test_first_use_rendering_runs_before_other_visual_journeys(self):
        self.assertEqual('first-use-rendering', expanded('all', catalog())[0])

    def test_cycle_and_missing_suite_child_fail(self):
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/'catalog.json'
            for steps in [['a'],['missing']]:
                path.write_text(json.dumps({'schemaVersion':1,'scenarios':[dict(id='a',steps=steps,domains=['physics'],headless=True,timeout=1)]}))
                with self.assertRaises(ValueError):catalog(path)

    def test_false_pass_and_empty_assertions_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            valid=self.receipt();self.assertEqual([],validate_summary(valid,d,'movement',True))
            for change in [dict(checks=[]),dict(assertions=0),dict(checks=[{'passed':False}]),dict(scenario='wrong'),dict(schemaVersion=99),dict(catalogSha256='stale'),dict(completedScenarios=[]),dict(passed=False),dict(seconds=float('nan'))]:
                self.assertTrue(validate_summary(valid|change,d,'movement',True),change)

    def test_missing_invalid_and_escaped_capture_rejected(self):
        with tempfile.TemporaryDirectory() as d,tempfile.TemporaryDirectory() as outside:
            p=Path(d)/'capture.png';s=self.receipt()|{'screenshotMode':'graphical','screenshots':[str(p)]}
            self.assertTrue(validate_summary(s,d,'movement',False))
            p.write_bytes(b'not png');self.assertTrue(validate_summary(s,d,'movement',False))
            def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',binascii.crc32(kind+data))
            p.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',1280,800,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b'\0'*(800*(1+1280*3))))+chunk(b'IEND',b''))
            self.assertEqual([],validate_summary(s,d,'movement',False))
            escaped=Path(outside)/'capture.png';escaped.write_bytes(p.read_bytes());p.unlink();p.symlink_to(escaped)
            self.assertTrue(validate_summary(s,d,'movement',False))

    def test_runner_missing_executable_always_emits_failure_receipts(self):
        with tempfile.TemporaryDirectory() as d:
            r=subprocess.run([sys.executable,str(ROOT/'tools/playtest/run.py'),'--scenario','movement','--headless','--godot',str(Path(d)/'missing'),'--output',d],capture_output=True,text=True)
            self.assertNotEqual(0,r.returncode)
            folder=next(Path(d).glob('*-movement'))
            self.assertFalse(json.loads((folder/'runner-summary.json').read_text())['passed'])
            self.assertGreater(int(ET.parse(folder/'junit.xml').getroot().get('failures')),0)

    def test_cleanup_reaps_exited_child_without_masking_receipt(self):
        from run import stop
        process=subprocess.Popen([sys.executable,'-c','pass'],start_new_session=True)
        process.wait(timeout=5)
        stop(process)
        stop(process)
        self.assertEqual(0,process.returncode)

    def test_runner_timeout_cannot_be_success(self):
        with tempfile.TemporaryDirectory() as d:
            program=Path(d)/'fake-godot';program.write_text('#!'+sys.executable+'\nimport time\ntime.sleep(10)\n');program.chmod(0o755)
            r=subprocess.run([sys.executable,str(ROOT/'tools/playtest/run.py'),'--scenario','movement','--headless','--godot',str(program),'--timeout','.05','--output',str(Path(d)/'out')],capture_output=True,text=True,timeout=10)
            self.assertNotEqual(0,r.returncode)
            result=json.loads(next((Path(d)/'out').glob('*/runner-summary.json')).read_text())
            self.assertFalse(result['passed']);self.assertTrue(result['errors'])

if __name__=='__main__':unittest.main()

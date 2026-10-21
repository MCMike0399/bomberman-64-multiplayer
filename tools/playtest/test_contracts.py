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
from contracts import catalog, expanded, validate_summary, junit, ROOT, CATALOG

class PlaytestContracts(unittest.TestCase):
    def receipt(self, **changes):
        return dict(schemaVersion=1, catalogSha256=hashlib.sha256(CATALOG.read_bytes()).hexdigest(), completedScenarios=['movement'], scenario='movement', passed=True, assertions=1,
                    checks=[{'description':'real check','passed':True}], seconds=1,
                    screenshots=[], screenshotMode='disabled-headless', **changes)

    def test_catalog_resolves_real_methods_and_complete_domains(self):
        scenarios=catalog()
        source='\n'.join(p.read_text() for p in (ROOT/'scripts/Testing').glob('*.cs'))
        methods=set(re.findall(r'private\s+async\s+Task\s+(\w+)\s*\(\s*\)',source))
        for item in scenarios.values():
            if 'method' in item:self.assertIn(item['method'],methods,item['id'])
        for id in expanded('core-headless',scenarios):self.assertTrue(scenarios[id]['headless'])
        self.assertTrue({'assets','models','animation','ui','physics','mechanics','ai','network','input'}<=set(scenarios['all']['domains']))
        self.assertNotIn('controller-observe',expanded('all',scenarios))

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

import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SPEC=importlib.util.spec_from_file_location('package_download',Path(__file__).with_name('package-download.py'))
package=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(package)


class DmgCreation(unittest.TestCase):
    def test_busy_partial_is_never_published_and_one_retry_uses_fresh_output(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);destination=root/'final.dmg';calls=[]
            def run(command,**kwargs):
                calls.append(command);Path(command[-1]).write_bytes(b'partial'if len(calls)==1 else b'complete')
                return subprocess.CompletedProcess(command,1 if len(calls)==1 else 0,'','hdiutil: create failed - Resource busy'if len(calls)==1 else'')
            with patch.object(package.subprocess,'run',side_effect=run),patch.object(package.time,'sleep'):
                package.create_dmg(root,destination)
            self.assertEqual(destination.read_bytes(),b'complete');self.assertEqual(len(calls),2)
            self.assertNotEqual(calls[0][-1],calls[1][-1])

    def test_other_failures_and_exhausted_busy_never_reuse_prior_output(self):
        for message,count in [('permission denied',1),('Resource busy',2)]:
            with self.subTest(message=message),tempfile.TemporaryDirectory()as temporary:
                root=Path(temporary);destination=root/'final.dmg';destination.write_bytes(b'prior')
                result=subprocess.CompletedProcess([],1,'',message)
                with patch.object(package.subprocess,'run',return_value=result)as run,patch.object(package.time,'sleep'):
                    with self.assertRaises(subprocess.CalledProcessError):package.create_dmg(root,destination)
                self.assertEqual(run.call_count,count);self.assertEqual(destination.read_bytes(),b'prior')

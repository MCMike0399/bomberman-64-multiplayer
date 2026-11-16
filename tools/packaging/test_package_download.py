import importlib.util
import json
import hashlib
import contextlib
import io
import sys
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


class DmgVerification(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory();self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name);self.image=self.root/'new.dmg';self.image.write_bytes(b'new-image-identity')
        self.receipt=self.root/'qa/dmg-verification.json'

    def record(self):return json.loads(self.receipt.read_text())

    def unavailable(self):
        # Exact retained CI failure shape, with only its temporary path changed.
        return (f'hdiutil: verify: unable to recognize "{self.image}" as a disk image. (Resource temporarily unavailable)\n'
                'hdiutil: verify failed - Resource temporarily unavailable\n')

    def execute(self,results,sleep=None):
        calls=[]
        def run(command,**kwargs):
            calls.append((command,kwargs));result=results[len(calls)-1]
            if isinstance(result,Exception):raise result
            code,stdout,stderr=result
            return subprocess.CompletedProcess(command,code,stdout,stderr)
        with (patch.object(package.subprocess,'run',side_effect=run),patch.object(package.time,'sleep',side_effect=sleep)as pause,
              contextlib.redirect_stdout(io.StringIO()),contextlib.redirect_stderr(io.StringIO())):
            try:package.verify_dmg(self.image,self.receipt)
            finally:self.calls=calls;self.pauses=pause.call_count
        return self.record()

    def test_exact_transient_then_success_verifies_same_image_and_retains_both_results(self):
        before=self.image.stat();failure=self.unavailable()
        report=self.execute([(1,'first-output\n',failure),(0,'verified CRC32\n','')])
        self.assertTrue(report['passed']);self.assertEqual(report['outcome'],'verified')
        self.assertEqual([x['exitCode']for x in report['attempts']],[1,0])
        self.assertEqual(report['attempts'][0]['stderr'],failure)
        self.assertEqual(report['attempts'][0]['stdout'],'first-output\n')
        self.assertEqual(report['attempts'][1]['stdout'],'verified CRC32\n')
        self.assertEqual(self.pauses,1);self.assertEqual(self.image.stat().st_ino,before.st_ino)
        self.assertEqual(self.image.read_bytes(),b'new-image-identity')
        self.assertEqual(report['imageIdentity']['sha256'],hashlib.sha256(self.image.read_bytes()).hexdigest())
        for command,kwargs in self.calls:
            self.assertEqual(command,['hdiutil','verify',str(self.image)])
            self.assertEqual(kwargs,{'capture_output':True,'text':True,'timeout':120})

    def test_explicit_errno_name_is_the_only_other_retry_form(self):
        report=self.execute([(11,'','hdiutil: verify failed - EAGAIN (11)\n'),(0,'verified','')])
        self.assertTrue(report['passed']);self.assertEqual(len(self.calls),2)

    def test_successful_first_verification_never_retries(self):
        report=self.execute([(0,'verified CRC32','')])
        self.assertTrue(report['passed']);self.assertEqual(len(self.calls),1);self.assertEqual(self.pauses,0)

    def test_permanent_format_checksum_and_arbitrary_failures_never_retry(self):
        for code,message in [(1,'hdiutil: verify failed - checksum mismatch'),
                             (1,'hdiutil: verify failed - image format not recognized'),
                             (1,'hdiutil: verify failed - permission denied'),
                             (1,'hdiutil: verify failed - Resource busy'),
                             (1,'unrelated Resource temporarily unavailable'),
                             (1,'checksum INVALID\nhdiutil: verify failed - EAGAIN'),
                             (1,'CRC32 mismatch\nhdiutil: verify failed - EAGAIN'),
                             (1,'hdiutil: verify failed - image not recognized\nhdiutil: verify failed - EAGAIN'),
                             (1,'hdiutil: verify: unable to recognize "EAGAIN.dmg" as a disk image. (Invalid argument)'),
                             (-9,'hdiutil: verify failed - EAGAIN')]:
            with self.subTest(code=code,message=message),self.assertRaises(subprocess.CalledProcessError):
                self.execute([(code,'',message)])
            report=self.record();self.assertFalse(report['passed']);self.assertEqual(report['outcome'],'verify-failed')
            self.assertEqual(len(self.calls),1);self.assertEqual(self.pauses,0)
            self.assertEqual(report['attempts'][0]['stderr'],message)

    def test_final_transient_is_fatal_and_keeps_both_failed_attempts(self):
        failure=self.unavailable()
        with self.assertRaises(subprocess.CalledProcessError)as raised:
            self.execute([(1,'one',failure),(1,'two',failure)])
        report=self.record();self.assertFalse(report['passed']);self.assertEqual(report['outcome'],'eagain-exhausted')
        self.assertEqual(len(self.calls),2);self.assertEqual(self.pauses,1)
        self.assertEqual([x['stdout']for x in report['attempts']],['one','two'])
        self.assertEqual(raised.exception.stderr,failure)

    def test_transient_followed_by_checksum_failure_stays_fatal(self):
        with self.assertRaises(subprocess.CalledProcessError):
            self.execute([(1,'',self.unavailable()),(1,'','hdiutil: verify failed - checksum mismatch')])
        report=self.record();self.assertFalse(report['passed']);self.assertEqual(report['outcome'],'verify-failed')
        self.assertEqual(len(self.calls),2);self.assertEqual(self.pauses,1)

    def test_timeout_and_launch_eagain_are_not_verification_eagain(self):
        for failure in (subprocess.TimeoutExpired(['hdiutil','verify'],120,output=b'partial'),BlockingIOError(11,'Resource temporarily unavailable')):
            with self.subTest(failure=type(failure).__name__),self.assertRaises(type(failure)):
                self.execute([failure])
            report=self.record();self.assertFalse(report['passed']);self.assertEqual(len(self.calls),1)
            self.assertFalse(report['attempts'][0]['retryEligible']);self.assertEqual(self.pauses,0)

    def test_changed_image_cannot_turn_a_failed_verification_into_a_success(self):
        with self.assertRaisesRegex(RuntimeError,'different image'):
            self.execute([(1,'',self.unavailable()),(0,'must not execute','')],sleep=lambda _:self.image.write_bytes(b'different-image'))
        report=self.record();self.assertFalse(report['passed']);self.assertEqual(report['outcome'],'image-changed')
        self.assertEqual(len(self.calls),1)

    def test_existing_ci_report_allowlist_retains_failed_attempts_and_final_outcome(self):
        with self.assertRaises(subprocess.CalledProcessError):
            self.execute([(1,'',self.unavailable()),(1,'',self.unavailable())])
        (self.receipt.parent/'preferences.cfg').write_text('private fixture')
        destination=self.root/'staged'
        subprocess.run([sys.executable,str(package.ROOT/'tools/ci/stage_reports.py'),'--source',str(self.receipt.parent),
                        '--destination',str(destination)],check=True,capture_output=True,text=True)
        self.assertEqual(json.loads((destination/self.receipt.name).read_text()),self.record())
        self.assertEqual([p.name for p in destination.iterdir()],[self.receipt.name])

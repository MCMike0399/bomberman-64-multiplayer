"""Trusted-main Mac reference dispatch cannot inherit legacy write privileges."""
from pathlib import Path
import unittest
import hashlib,json,tempfile
import mac_reference
import download_formats
ROOT=Path(__file__).resolve().parents[2]

class MacReferenceWorkflow(unittest.TestCase):
    def test_dispatch_has_its_own_mac_only_read_only_job(self):
        source=(ROOT/'.github/workflows/visual-parity.yml').read_text()
        self.assertIn('  trusted-macos-reference:\n',source)
        job=source.split('  trusted-macos-reference:\n',1)[1]
        self.assertIn("github.event_name == 'workflow_dispatch' && github.ref == 'refs/heads/main'",job)
        self.assertIn('runs-on: macos-15',job)
        self.assertIn('contents: read',job);self.assertIn('actions: read',job)
        for forbidden in ('contents: write','id-token:','matrix.','environment:'):
            self.assertNotIn(forbidden,job)
        self.assertIn('mac_reference.py',job)

    def test_legacy_job_remains_byte_identical(self):
        source=(ROOT/'.github/workflows/visual-parity.yml').read_text()
        legacy=source.split('jobs:\n',1)[1].split('\n  trusted-macos-reference:',1)[0].rstrip()
        self.assertEqual(hashlib.sha256(legacy.encode()).hexdigest(),'0701f22205befec7bd365ab945ae43c2c4cf14055ae087261f7a1970aa53be61')

class MacReferencePayload(unittest.TestCase):
    def run_record(self):return {'head_repository':{'full_name':mac_reference.REPOSITORY},'head_sha':'a'*40,'head_branch':'main','path':'.github/workflows/ci.yml','event':'push','status':'completed','conclusion':'success'}
    def package(self,directory,source='a'*40):
        directory.mkdir(parents=True,exist_ok=True);archive=directory/'Blast-Circuit-macos-arm64.zip';archive.write_bytes(b'fixture archive')
        (directory/(archive.name+'.sha256')).write_text(hashlib.sha256(archive.read_bytes()).hexdigest()+'  '+archive.name+'\n')
        (directory/'Blast-Circuit-macos-arm64.dmg').write_bytes(b'fixture disk image')
        release={'version':'0.45.0','build':'56','protocolRevision':6}
        (directory/'build-metadata.json').write_text(json.dumps({'revision':source,'release':release}))
        download_formats.record(directory,'macos-arm64',source,release,archive.name)
    def test_pull_request_push_and_nonmain_never_reach_dispatch(self):
        for event,ref,repository in [('pull_request','refs/heads/main',mac_reference.REPOSITORY),('push','refs/heads/main',mac_reference.REPOSITORY),('push','refs/heads/other',mac_reference.REPOSITORY),('workflow_dispatch','refs/heads/other',mac_reference.REPOSITORY),('workflow_dispatch','refs/heads/main','fork/repo')]:
            with self.subTest(event=event,ref=ref,repository=repository):self.assertFalse(mac_reference.dispatch_allowed(event,ref,repository))
        self.assertTrue(mac_reference.dispatch_allowed('workflow_dispatch','refs/heads/main',mac_reference.REPOSITORY))
    def test_wrong_main_failed_pr_and_fork_verification_are_rejected(self):
        for change in ({'head_sha':'b'*40},{'head_branch':'feature'},{'event':'pull_request'},{'conclusion':'failure'},{'head_repository':{'full_name':'fork/repo'}}):
            with self.subTest(change=change),self.assertRaises(ValueError):mac_reference.validate_run({**self.run_record(),**change},'a'*40,'a'*40)
        with self.assertRaises(ValueError):mac_reference.validate_run(self.run_record(),'a'*40,'b'*40)
    def test_downloaded_source_is_checked_before_capture(self):
        with tempfile.TemporaryDirectory()as directory:
            root=Path(directory);self.package(root,'b'*40)
            with self.assertRaisesRegex(ValueError,'source differs'):mac_reference.validate_payload(root,'a'*40)
    def test_downloaded_archive_bytes_are_checked(self):
        with tempfile.TemporaryDirectory()as directory:
            root=Path(directory);self.package(root);(root/'Blast-Circuit-macos-arm64.zip').write_bytes(b'changed')
            with self.assertRaisesRegex(ValueError,'checksum differs'):mac_reference.validate_payload(root,'a'*40)
    def test_successful_preparation_downloads_only_mac_from_exact_run(self):
        with tempfile.TemporaryDirectory()as directory:
            root=Path(directory)/'artifacts';calls=[]
            def gh(*args):
                calls.append(args)
                if args[0]=='run':self.package(root);return ''
                if 'git/ref/heads/main'in args[1]:return json.dumps({'object':{'sha':'a'*40}})
                if '/artifacts?'in args[1]:return json.dumps([{'artifacts':[{'id':7,'name':mac_reference.ARTIFACT,'expired':False}]}])
                return json.dumps(self.run_record())
            report=mac_reference.prepare('123',root,'a'*40,event='workflow_dispatch',ref='refs/heads/main',repository=mac_reference.REPOSITORY,gh=gh)
            self.assertEqual(report['source'],'a'*40);self.assertEqual(report['target'],'macos-arm64')
            downloads=[c for c in calls if c[0]=='run'];self.assertEqual(len(downloads),1);self.assertIn(mac_reference.ARTIFACT,downloads[0])
    def test_expired_or_ambiguous_artifact_never_downloads(self):
        for artifacts in ([],[{'id':7,'name':mac_reference.ARTIFACT,'expired':True}],[{'id':7,'name':mac_reference.ARTIFACT},{'id':8,'name':mac_reference.ARTIFACT}]):
            with tempfile.TemporaryDirectory()as directory:
                def gh(*args):
                    self.assertNotEqual(args[0],'run')
                    if 'git/ref/heads/main'in args[1]:return json.dumps({'object':{'sha':'a'*40}})
                    if '/artifacts?'in args[1]:return json.dumps([{'artifacts':artifacts}])
                    return json.dumps(self.run_record())
                with self.assertRaises(ValueError):mac_reference.prepare('123',Path(directory)/'artifacts','a'*40,event='workflow_dispatch',ref='refs/heads/main',repository=mac_reference.REPOSITORY,gh=gh)

if __name__=='__main__':unittest.main()


class TrackedArtifactsDirectory(unittest.TestCase):
    def test_tracked_gdignore_directory_is_accepted_and_other_content_refused(self):
        import tempfile
        from pathlib import Path
        with tempfile.TemporaryDirectory() as root:
            directory = Path(root) / 'artifacts'; directory.mkdir(); (directory / '.gdignore').write_text('')
            mac_reference.prepare_directory(directory)
            (directory / 'stale.zip').write_text('x')
            with self.assertRaises(ValueError): mac_reference.prepare_directory(directory)
            mac_reference.prepare_directory(Path(root) / 'fresh')

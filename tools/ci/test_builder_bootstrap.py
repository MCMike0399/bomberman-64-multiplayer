import hashlib
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

import builder_bootstrap as bootstrap

RUN='sancus-builder-20261030-8762d11-1234567890abcdef'


class DisposableBootstrap(unittest.TestCase):
    def scripts(self):return bootstrap.userdata({'runId':RUN},'a'*64,'b'*64,
                                              {'url':'https://awscli.amazonaws.com/AWSCLIV2.msi','sha256':'c'*64})

    def test_all_object_calls_are_owner_bound_and_there_is_no_github_registration(self):
        texts=list(self.scripts().values())+[bootstrap.LINUX_SETUP,bootstrap.WINDOWS_SETUP]
        for text in texts:
            self.assertNotIn('GITHUB_TOKEN',text);self.assertNotIn('config.sh',text);self.assertNotIn('config.cmd',text)
            self.assertNotIn('api.github.com',text)
            for line in text.splitlines():
                if 's3api get-object'in line:
                    self.assertIn('--profile sancus --region us-east-1',line)
                    self.assertIn('--expected-bucket-owner 524329886851',line)

    def test_windows_has_limited_interactive_task_and_owned_sdk_prefix(self):
        self.assertIn('-LogonType Interactive -RunLevel Limited',bootstrap.WINDOWS_SETUP)
        self.assertIn('Start-ScheduledTask -TaskName sancus-builder-run',bootstrap.WINDOWS_DISPATCH)
        self.assertIn('$root\\dotnet',bootstrap.WINDOWS_SETUP)
        self.assertNotIn('Administrators',bootstrap.WINDOWS_SETUP)
        self.assertIn('[IO.FileMode]::CreateNew',bootstrap.WINDOWS_DISPATCH)
        initial=self.scripts()['Windows']
        self.assertNotIn('Invoke-WebRequest',initial)
        self.assertIn("$request.ExpectedBucketOwner='524329886851'",initial)
        self.assertIn("$request.Key='payload/bootstrap-awscli.msi'",initial)

    def test_linux_dispatches_as_standard_user_and_scripts_parse(self):
        self.assertIn('runuser -u ubuntu',bootstrap.LINUX_DISPATCH)
        self.assertNotIn('--preserve-environment',bootstrap.LINUX_DISPATCH)
        self.assertNotIn('/usr/share/dotnet',bootstrap.LINUX_SETUP)
        with tempfile.TemporaryDirectory()as root:
            for index,text in enumerate([self.scripts()['Linux'],bootstrap.LINUX_SETUP,bootstrap.LINUX_DISPATCH]):
                path=Path(root)/str(index);path.write_text(text)
                subprocess.run(['bash','-n',str(path)],check=True,capture_output=True)

    def test_bundle_is_hash_bound_and_has_only_the_fixed_helpers(self):
        with tempfile.TemporaryDirectory()as root:
            path=Path(root)/'bootstrap.zip';result=bootstrap.bundle(RUN,path)
            self.assertEqual(result['sha256'],hashlib.sha256(path.read_bytes()).hexdigest())
            with zipfile.ZipFile(path)as archive:
                self.assertEqual(set(archive.namelist()),set(bootstrap.MODULES)|{'setup.sh','dispatch','setup.ps1','dispatch.ps1','worker.ps1'})
                self.assertTrue(all('..'not in name and not name.startswith('/')for name in archive.namelist()))
            with self.assertRaises(FileExistsError):bootstrap.bundle(RUN,path)

    def test_unpinned_or_foreign_cli_bootstrap_refused(self):
        with self.assertRaises(ValueError):bootstrap.userdata({'runId':RUN},'a'*64,'b'*64,{'url':'https://untrusted.invalid/tool.msi','sha256':'c'*64})


if __name__=='__main__':unittest.main()

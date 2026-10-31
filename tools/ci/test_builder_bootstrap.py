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
        self.assertLess(bootstrap.WINDOWS_SETUP.index('Install-BlastAudio'),bootstrap.WINDOWS_SETUP.index('New-LocalUser'))
        self.assertIn('b950e39f01af1d04ea623c8f6d8eb9b6ea5c477c637295fabf20631c85116bfb',bootstrap.WINDOWS_SETUP)
        self.assertLess(bootstrap.WINDOWS_DISPATCH.index('Move-Item'),bootstrap.WINDOWS_DISPATCH.index('Start-ScheduledTask'))

    def test_windows_waits_through_403_for_objects_not_yet_staged(self):
        # Without s3:ListBucket a missing key is AccessDenied; run 41198567742
        # threw on it 23 s before the controller staged the CLI installer.
        initial=self.scripts()['Windows']
        self.assertIn("-notin 'NoSuchKey','AccessDenied'){throw}",initial)
        self.assertNotIn("-ne 'NoSuchKey'){throw}",initial)
        fetch=[line for line in initial.splitlines() if '--key payload/bootstrap.zip' in line]
        self.assertEqual(len(fetch),1)
        self.assertTrue(fetch[0].startswith(' try{& $aws') and fetch[0].rstrip().endswith('| Out-Null}catch{}'))
        self.assertIn("-ne '"+'a'*64+"'){throw 'Bootstrap bundle differs'}",initial)

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
                self.assertEqual(set(archive.namelist()),set(bootstrap.MODULES)|{'setup.sh','dispatch','setup.ps1','dispatch.ps1','worker.ps1','windows-audio.ps1'})
                self.assertEqual(archive.read('windows-audio.ps1'),bootstrap.WINDOWS_AUDIO.read_bytes())
                self.assertTrue(all('..'not in name and not name.startswith('/')for name in archive.namelist()))
            with self.assertRaises(FileExistsError):bootstrap.bundle(RUN,path)

    def test_unpinned_or_foreign_cli_bootstrap_refused(self):
        with self.assertRaises(ValueError):bootstrap.userdata({'runId':RUN},'a'*64,'b'*64,{'url':'https://untrusted.invalid/tool.msi','sha256':'c'*64})
        msi={'url':'https://awscli.amazonaws.com/AWSCLIV2.msi','sha256':'c'*64}
        for linux in ({'url':'https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip','sha256':'d'*64},  # unversioned
                      {'url':'https://untrusted.invalid/awscli-exe-linux-x86_64-2.38.5.zip','sha256':'d'*64},
                      {'url':bootstrap.LINUX_AWS_CLI['url'],'sha256':'D'*64},{'url':bootstrap.LINUX_AWS_CLI['url'],'sha256':''}):
            with self.subTest(linux=linux),self.assertRaises(ValueError):bootstrap.userdata({'runId':RUN},'a'*64,'b'*64,msi,linux)

    def test_linux_installs_pinned_cli_v2_not_the_missing_noble_package(self):
        script=self.scripts()['Linux']
        packages=bootstrap.linux_packages(script)
        self.assertNotIn('awscli',packages);self.assertTrue({'curl','ca-certificates','unzip'}<=set(packages))
        self.assertEqual(bootstrap.LINUX_AWS_CLI,{'url':'https://awscli.amazonaws.com/awscli-exe-linux-x86_64-2.38.5.zip',
                         'sha256':'5def7d1b7e55d892f9a381bb2cb2081a685712464be0d36d5d706af0ac48d0d3'})
        download=script.index(bootstrap.LINUX_AWS_CLI['url']);verify=script.index(bootstrap.LINUX_AWS_CLI['sha256']+"' \"$root/awscli-install/awscliv2.zip\" | sha256sum -c -")
        unzip=script.index('unzip -q "$root/awscli-install/awscliv2.zip"')
        self.assertLess(download,verify);self.assertLess(verify,unzip)
        self.assertLess(unzip,script.index('--install-dir "$root/aws-cli/dist" --bin-dir "$root/aws-cli/bin"'))
        self.assertIn('chown -R root:root "$root/aws-cli"',script)
        self.assertEqual(bootstrap.LINUX_AWS,'/opt/sancus-builder/aws-cli/bin/aws')
        self.assertIn('aws='+bootstrap.LINUX_AWS,script);self.assertIn('aws='+bootstrap.LINUX_AWS,bootstrap.LINUX_SETUP)
        # Every AWS call in user-data and the Linux setup uses that exact binary.
        for text in (script,bootstrap.LINUX_SETUP,bootstrap.LINUX_DISPATCH):
            for line in text.splitlines():
                if 's3api'in line or '--version'in line and 'aws'in line:
                    self.assertRegex(line,r'(^|\s)"\$aws" ',line)
                self.assertNotRegex(line,r'(^|[\s;(])aws\s',line)
        # Setup re-owns the CLI after the ubuntu chown.
        self.assertLess(bootstrap.LINUX_SETUP.index('chown -R ubuntu:ubuntu "$root"'),bootstrap.LINUX_SETUP.index('chown -R root:root "$root/aws-cli"'))
        import builder_guest,os
        if os.name!='nt':self.assertEqual(builder_guest.AWS_CLI,bootstrap.LINUX_AWS)

    def test_linux_failures_reach_the_serial_console_and_success_is_marked(self):
        script=self.scripts()['Linux']
        self.assertTrue(script.startswith('#!/bin/bash\nset -Eeuo pipefail\n'))
        self.assertIn("trap 'status=$?; trap - ERR; console \"SANCUS-BUILDER-BOOTSTRAP-FAILED",script)
        self.assertIn('tail -n 80 "$root/bootstrap.log"',script)
        self.assertIn('${SANCUS_BUILDER_CONSOLE:-/dev/console /dev/ttyS0}',script)
        self.assertLess(script.index('trap '),script.index('apt-get update'))
        self.assertTrue(script.rstrip().endswith('console "SANCUS-BUILDER-BOOTSTRAP-READY linux-x64"'))
        self.assertLess(script.index('bash "$root/bin/setup.sh"'),script.index('test -s "$root/bootstrap-ready.json"'))
        section=bootstrap.linux_package_section(script)
        self.assertNotIn('s3api',section);self.assertTrue(section.rstrip().endswith(bootstrap.PACKAGES_END))

    def test_windows_audio_gate_runs_as_system_before_readiness(self):
        setup=bootstrap.WINDOWS_SETUP;audio=bootstrap.WINDOWS_AUDIO.read_text()
        self.assertIn('Set-Service -Name $name -StartupType Automatic',audio)
        self.assertNotIn('StartupType Manual',audio)
        self.assertIn("$BlastAudioArchiveSha256 = 'b950e39f01af1d04ea623c8f6d8eb9b6ea5c477c637295fabf20631c85116bfb'",audio)
        for needle in ("-notmatch 'BUREL VINCENT'","-notmatch 'Microsoft Windows Hardware Compatibility Publisher'",
                       "$_.startType -eq 'Automatic'","$_.status -eq 'Running'",'virtualCableIsDefaultRender','GetDefaultAudioEndpoint(0, 0'):
            self.assertIn(needle,audio)
        self.assertNotIn('Kill($true)',audio)  # Windows PowerShell 5.1 has no Kill(bool).
        gate=setup.index('Assert-BlastAudio 90')
        self.assertLess(setup.index('Install-BlastAudio (InputFile'),gate)
        for later in ('New-LocalUser','bootstrap.status','Restart-Computer','SANCUS-BUILDER-BOOTSTRAP-SYSTEM-READY'):
            self.assertLess(gate,setup.index(later),later)
        worker=bootstrap.WINDOWS_WORKER
        self.assertLess(worker.index('Assert-BlastAudio 120'),worker.index('builder_guest_ready.py'))
        desktop=(bootstrap.WINDOWS_AUDIO.parent/'windows-test-desktop.ps1').read_text()
        self.assertIn("windows-audio.ps1'",desktop);self.assertIn('Assert-BlastAudio -Seconds 20',desktop)
        initial=self.scripts()['Windows']
        self.assertLess(initial.index('Start-Transcript'),initial.index('trap {'))
        self.assertIn('SANCUS-BUILDER-BOOTSTRAP-FAILED',initial);self.assertIn("SerialPort 'COM1'",initial)
        self.assertIn('Get-Content "$root\\bootstrap.log" -Tail 80',initial)


class FreePreflight(unittest.TestCase):
    def test_container_section_is_the_real_user_data_prefix_without_bucket_calls(self):
        import builder_bootstrap_preflight as preflight
        text,packages=preflight.section()
        script=bootstrap.userdata({'runId':preflight.RUN},'1'*64,'2'*64,preflight.MSI)['Linux']
        self.assertTrue(script.startswith(text.rstrip('\n')));self.assertNotIn('s3api',text)
        self.assertEqual(packages,bootstrap.linux_packages(script))
        missing,_=preflight.section('missing-package');self.assertIn(preflight.MISSING,missing)
        digest,_=preflight.section('digest-mismatch');self.assertNotIn(bootstrap.LINUX_AWS_CLI['sha256'],digest)
        runner=preflight.runner(packages)
        self.assertIn('SANCUS_BUILDER_CONSOLE=/preflight/console.txt',runner);self.assertIn(bootstrap.LINUX_AWS+' --version',runner)
        workflow=(Path(__file__).resolve().parents[2]/'.github/workflows/builder-bootstrap-preflight.yml').read_text()
        self.assertIn('builder_bootstrap_preflight.py --output',workflow);self.assertIn('windows-audio-preflight.ps1',workflow)
        self.assertNotIn('aws-actions',workflow);self.assertNotIn('id-token',workflow)


if __name__=='__main__':unittest.main()

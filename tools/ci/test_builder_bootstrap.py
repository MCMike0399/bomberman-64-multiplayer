import hashlib
import re
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
        texts=list(self.scripts().values())+[bootstrap.WINDOWS_SETUP,bootstrap.WINDOWS_DISPATCH,bootstrap.WINDOWS_WORKER]
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
        # No internet downloads: the only web requests are IMDSv2 on the link-local endpoint.
        requests=re.findall(r'Invoke-WebRequest[^\n]*?-Uri "([^"]*)"',initial)
        self.assertEqual(len(requests),initial.count('Invoke-WebRequest'))
        self.assertTrue(requests and all(uri.startswith('$imds/latest/')for uri in requests),requests)
        self.assertEqual(re.findall(r"\$imds='([^']*)'",initial),['http://169.254.169.254'])
        self.assertIn("$request.ExpectedBucketOwner='524329886851'",initial)
        self.assertIn("$request.Key='payload/bootstrap-awscli.msi'",initial)
        self.assertLess(bootstrap.WINDOWS_SETUP.index('Install-BlastAudio'),bootstrap.WINDOWS_SETUP.index('New-LocalUser'))
        self.assertIn('b950e39f01af1d04ea623c8f6d8eb9b6ea5c477c637295fabf20631c85116bfb',bootstrap.WINDOWS_SETUP)
        self.assertLess(bootstrap.WINDOWS_DISPATCH.index('Move-Item'),bootstrap.WINDOWS_DISPATCH.index('Start-ScheduledTask'))

    # Every guest read of an object the controller may not have staged yet, by site.
    SITES={'Windows':{'payload/bootstrap-awscli.msi':"Wait-StagedObject 'payload/bootstrap-awscli.msi' ([DateTime]::UtcNow.AddMinutes(5)) {",
                      'payload/bootstrap.zip':"Get-StagedObject 'payload/bootstrap.zip' \"$root\\bootstrap.zip\" 300",
                      'payload/manifest.json':"Get-StagedObject 'payload/manifest.json' \"$root\\manifest.json\" 60"}}

    def test_every_guest_object_read_goes_through_the_one_powershell_helper(self):
        import builder_s3
        self.assertEqual(set(bootstrap.STAGED_PENDING),set(builder_s3.GUEST_PENDING))
        self.assertIn('AccessDenied',bootstrap.STAGED_PENDING);self.assertIn('403',bootstrap.STAGED_PENDING)
        scripts=self.scripts()
        for osname,sites in self.SITES.items():
            for key,call in sites.items():self.assertEqual(scripts[osname].count(call),1,(osname,key))
        self.assertIn('Get-StagedObject $row.objectKey $file 60',bootstrap.WINDOWS_SETUP)
        # The only raw get-object left in each guest script is inside its helper.
        for text,helper in ((scripts['Windows'],bootstrap.PS_STAGED),(bootstrap.WINDOWS_SETUP,bootstrap.PS_STAGED)):
            self.assertEqual(text.count('s3api get-object'),1)
            self.assertNotIn('__STAGED_GET',text)
        self.assertEqual(scripts['Windows'].count('GetObjectAsync'),1)
        self.assertNotIn("-ne 'NoSuchKey'",scripts['Windows'])

    def fake_aws(self,root,replies):
        """An aws stand-in that answers each call with the next (exit, stderr) reply."""
        state=Path(root)/'replies';state.write_text('\n'.join(replies)+'\n')
        tool=Path(root)/'aws';tool.write_text('#!/bin/bash\nreply=$(head -n 1 "%s");sed -i.bak 1d "%s"\n'
            'if test "$reply" = ok; then for last; do :; done; echo payload > "$last"; exit 0; fi\n'
            'echo "An error occurred ($reply) when calling the GetObject operation: denied" >&2; exit 254\n'%(state,state))
        tool.chmod(0o755);return tool

    def pwsh(self):
        import shutil
        return shutil.which('pwsh')

    def test_windows_scripts_parse_and_powershell_helper_waits_through_403(self):
        pwsh=self.pwsh()
        if not pwsh:self.skipTest('pwsh unavailable')
        with tempfile.TemporaryDirectory()as root:
            for name,text in (('userdata.ps1',self.scripts()['Windows'].replace('<powershell>','').replace('</powershell>','')),
                              ('setup.ps1',bootstrap.WINDOWS_SETUP)):
                (Path(root)/name).write_text(text)
                check=subprocess.run([pwsh,'-NoProfile','-Command','$e=$null;[void][System.Management.Automation.Language.Parser]::ParseFile("%s",[ref]$null,[ref]$e);$e.Count'%(Path(root)/name)],
                                     capture_output=True,text=True,timeout=120)
                self.assertEqual(check.stdout.strip(),'0',(name,check.stdout,check.stderr))
            def run(replies,seconds):
                aws=self.fake_aws(root,replies)
                body=("$ErrorActionPreference='Stop'\n$root='%s'\n$aws='%s'\nfunction Start-Sleep{}\n"%(root,aws)+bootstrap.PS_STAGED.replace('__BUCKET__','b')+
                      "\nGet-StagedObject 'payload/x' '%s' %d\nGet-Content '%s'\n"%(Path(root)/'out',seconds,Path(root)/'out'))
                (Path(root)/'run.ps1').write_text(body);(Path(root)/'staged-get-samples.log').unlink(missing_ok=True)
                result=subprocess.run([pwsh,'-NoProfile','-File',str(Path(root)/'run.ps1')],capture_output=True,text=True,timeout=120)
                log=Path(root)/'staged-get-samples.log'
                return result,[line.split()[-1] for line in log.read_text().splitlines()]if log.exists()else[]
            result,samples=run(['AccessDenied','NoSuchKey','ok'],30)
            self.assertEqual(result.returncode,0,result.stdout+result.stderr);self.assertIn('payload',result.stdout)
            self.assertEqual(samples,['AccessDenied','NoSuchKey'])
            result,_=run(['InvalidAccessKeyId','ok'],30)
            self.assertNotEqual(result.returncode,0);self.assertIn('failed: InvalidAccessKeyId',result.stdout+result.stderr)
            # The pre-CLI SDK read: the exact user-data block against a fake S3 client whose
            # failures surface from .NET GetResult(), as AmazonS3Exception does.
            userdata=self.scripts()['Windows']
            block=userdata[userdata.index("Wait-StagedObject 'payload/bootstrap-awscli.msi'"):userdata.index('$client.Dispose()')]
            def sdk(codes):
                body=("$ErrorActionPreference='Stop'\n$root='%s'\nfunction Start-Sleep{}\n"%root+
                      "Add-Type 'public class FakeS3Error:System.Exception{public string ErrorCode;public FakeS3Error(string c){ErrorCode=c;}}'\n"
                      "$script:codes=[System.Collections.Queue]::new(@(%s))\n"%','.join("'"+c+"'" for c in codes)+
                      "$client=[pscustomobject]@{}\n$client|Add-Member ScriptMethod GetObjectAsync {param($r,$t)$c=$script:codes.Dequeue()\n"
                      " if($c -ne 'ok'){return [System.Threading.Tasks.Task]::FromException[object]([FakeS3Error]::new($c))}\n"
                      " $response=[pscustomobject]@{ResponseStream=[IO.MemoryStream]::new([byte[]](1,2,3))};$response|Add-Member ScriptMethod Dispose {}\n"
                      " [System.Threading.Tasks.Task]::FromResult([object]$response)}\n$request=$null\n"+
                      PS_HELPER+'\n'+block+"\n$msi=[IO.FileInfo]::new(\"$root\\aws.msi\");$msi.Length;$msi.Delete()\n")
                (Path(root)/'sdk.ps1').write_text(body);(Path(root)/'staged-get-samples.log').unlink(missing_ok=True)
                result=subprocess.run([pwsh,'-NoProfile','-File',str(Path(root)/'sdk.ps1')],capture_output=True,text=True,timeout=120)
                log=Path(root)/'staged-get-samples.log'
                return result,[line.split()[-1] for line in log.read_text().splitlines()]if log.exists()else[]
            PS_HELPER=bootstrap.PS_STAGED.replace('__BUCKET__','b')
            result,samples=sdk(['AccessDenied','NoSuchKey','ok'])
            self.assertEqual(result.returncode,0,result.stdout+result.stderr);self.assertTrue(result.stdout.strip().endswith('3'))
            self.assertEqual(samples,['AccessDenied','NoSuchKey'])
            result,_=sdk(['ExpiredToken','ok'])
            self.assertNotEqual(result.returncode,0);self.assertIn('failed: ExpiredToken',result.stdout+result.stderr)

    def fake_imds(self,script):
        """A local IMDSv2 stand-in that answers each request with the next scripted status."""
        import http.server,threading
        replies=list(script);seen=[]
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self,*args):pass
            def answer(self):
                seen.append((self.command,self.path,self.headers.get('X-aws-ec2-metadata-token')))
                status=replies.pop(0)if replies else 503
                if self.path=='/latest/api/token':body='TOKEN'
                elif self.path.endswith('/security-credentials/'):body='sancus-builder-agent\n'
                else:body='{"Code":"Success","AccessKeyId":"AKIA"}'if status==200 else'{"Code":"Pending"}'
                if status==201:status,body=200,'{"Code":"Pending"}'
                self.send_response(status);self.send_header('Content-Type','text/plain');self.end_headers();self.wfile.write(body.encode())
            do_GET=do_PUT=answer
        server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler)
        threading.Thread(target=server.serve_forever,daemon=True).start();self.addCleanup(server.server_close);self.addCleanup(server.shutdown)
        return 'http://127.0.0.1:%d'%server.server_address[1],seen

    # Token refused twice, then a role whose document lacks credentials (201 marker), then ready.
    IMDS_SCRIPT=[503,503,200,200,201,200,200,200]

    def test_windows_waits_for_imds_credentials_before_its_first_staged_read(self):
        scripts=self.scripts()
        self.assertLess(scripts['Windows'].index('Wait-InstanceCredentials 120'),scripts['Windows'].index("Wait-StagedObject 'payload/bootstrap-awscli.msi'"))
        pwsh=self.pwsh()
        if not pwsh:self.skipTest('pwsh unavailable')
        for script,seconds,ok in ((self.IMDS_SCRIPT,30,True),([],0,False)):
            url,seen=self.fake_imds(script)
            with tempfile.TemporaryDirectory()as root:
                body="$ErrorActionPreference='Stop'\n$root='%s'\n$imds='%s'\nfunction Start-Sleep{}\n"%(root,url)+bootstrap.PS_IMDS+'\nWait-InstanceCredentials %d\n'%seconds
                (Path(root)/'imds.ps1').write_text(body)
                result=subprocess.run([pwsh,'-NoProfile','-File',str(Path(root)/'imds.ps1')],capture_output=True,text=True,timeout=120)
                if ok:
                    self.assertEqual(result.returncode,0,result.stdout+result.stderr)
                    self.assertIn('imds-credentials ready-after-seconds',(Path(root)/'staged-get-samples.log').read_text())
                    self.assertEqual([row[0] for row in seen if row[1]=='/latest/api/token'],['PUT']*4)
                else:
                    self.assertNotEqual(result.returncode,0);self.assertIn('unavailable through IMDSv2 after 0 s',result.stdout+result.stderr)

    def test_only_the_windows_guest_remains(self):
        # 2026-11-13: the Linux AWS builder was removed; Linux qualifies on the owned workstation.
        self.assertEqual(set(self.scripts()),{'Windows'})
        for name in ('LINUX_SETUP','LINUX_DISPATCH','LINUX_AWS_CLI','LINUX_AWS','BASH_STAGED','BASH_IMDS',
                     'PACKAGES_BEGIN','linux_package_section','linux_packages'):
            self.assertFalse(hasattr(bootstrap,name),name)
        for text in (*self.scripts().values(),bootstrap.WINDOWS_SETUP,bootstrap.WINDOWS_DISPATCH,bootstrap.WINDOWS_WORKER):
            for needle in ('linux-x64','/opt/sancus-builder','runuser','apt-get','#!/bin/bash'):
                self.assertNotIn(needle,text)

    def test_bundle_is_hash_bound_and_has_only_the_fixed_helpers(self):
        with tempfile.TemporaryDirectory()as root:
            path=Path(root)/'bootstrap.zip';result=bootstrap.bundle(RUN,path)
            self.assertEqual(result['sha256'],hashlib.sha256(path.read_bytes()).hexdigest())
            with zipfile.ZipFile(path)as archive:
                self.assertEqual(set(archive.namelist()),set(bootstrap.MODULES)|{'setup.ps1','dispatch.ps1','worker.ps1','windows-audio.ps1'})
                self.assertEqual(archive.read('windows-audio.ps1'),bootstrap.WINDOWS_AUDIO.read_bytes())
                self.assertTrue(all('..'not in name and not name.startswith('/')for name in archive.namelist()))
            with self.assertRaises(FileExistsError):bootstrap.bundle(RUN,path)

    def test_unpinned_or_foreign_cli_bootstrap_refused(self):
        with self.assertRaises(ValueError):bootstrap.userdata({'runId':RUN},'a'*64,'b'*64,{'url':'https://untrusted.invalid/tool.msi','sha256':'c'*64})
        msi={'url':'https://awscli.amazonaws.com/AWSCLIV2.msi','sha256':'c'*64}
        for helper,manifest,cli in (('A'*64,'b'*64,msi),('a'*64,'',msi),('a'*64,'b'*64,{**msi,'sha256':'D'*64})):
            with self.subTest(helper=helper,manifest=manifest),self.assertRaises(ValueError):
                bootstrap.userdata({'runId':RUN},helper,manifest,cli)

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
    def test_hosted_preflight_keeps_only_the_windows_audio_gate(self):
        workflow=(Path(__file__).resolve().parents[2]/'.github/workflows/builder-bootstrap-preflight.yml').read_text()
        self.assertIn('windows-audio-preflight.ps1 -Output',workflow);self.assertIn('runs-on: windows-2025',workflow)
        self.assertNotIn('builder_bootstrap_preflight.py',workflow);self.assertNotIn('ubuntu',workflow)
        self.assertNotIn('aws-actions',workflow);self.assertNotIn('id-token',workflow)
        self.assertFalse((Path(__file__).resolve().parent/'builder_bootstrap_preflight.py').exists())


if __name__=='__main__':unittest.main()

"""Disposable Windows setup. All game/source/tool payloads come from the owned bucket.

Windows only since 2026-11-13: Linux real-GPU qualification runs on the owned workstation.
"""
import hashlib
import json
from pathlib import Path
import re
import zipfile

import builder_namespace as names
import builder_staging


MODULES=('builder_guest.py','builder_guest_ready.py','builder_guest_exit.py','builder_payload.py','builder_s3.py','builder_staging.py',
         'builder_namespace.py','native_execution.py')
# Shared with windows-test-desktop.ps1 -VerifyOnly and the hosted pre-flight.
WINDOWS_AUDIO=Path(__file__).resolve().parents[1]/'packaging/windows-audio.ps1'

# Guests have no s3:ListBucket, so S3 answers a key the controller has not staged
# yet with 403 AccessDenied, not 404 NoSuchKey (runs 41178169234, 41198567742).
# Every guest object read goes through one PowerShell helper: missing (404/403)
# and S3 throttling/timeout codes are pending until the caller's bounded deadline,
# each new code is sampled to staged-get-samples.log, and any other error fails at
# once. Pinned hashes and sizes still decide content after a successful read.
STAGED_PENDING=('NoSuchKey','404','NotFound','AccessDenied','403',
                'SlowDown','RequestTimeout','ServiceUnavailable','InternalError')
PS_STAGED=r'''function Wait-StagedObject([string]$Key,[DateTime]$Until,[scriptblock]$Fetch){
 $last=$null
 while($true){
  $code=& $Fetch
  if(-not $code){return}
  if($code -notin __PENDING_LIST__){throw "Staged object $Key failed: $code"}
  if($code -ne $last){Add-Content "$root\staged-get-samples.log" "$([DateTime]::UtcNow.ToString('o')) $Key $code";$last=$code}
  if([DateTime]::UtcNow -ge $Until){throw "Timed out waiting for staged object $Key; last $code"}
  Start-Sleep -Seconds 5
 }
}
function Get-StagedObject([string]$Key,[string]$File,[int]$Seconds){
 Wait-StagedObject $Key ([DateTime]::UtcNow.AddSeconds($Seconds)) {
  # Native stderr must not end the script under Stop; the exit code and code decide.
  $ErrorActionPreference='Continue'
  $text=& $aws --profile sancus --region us-east-1 --cli-connect-timeout 8 --cli-read-timeout 25 s3api get-object --bucket '__BUCKET__' --expected-bucket-owner 524329886851 --key $Key $File 2>&1 | Out-String
  if($LASTEXITCODE -eq 0){return $null}
  if($text -match 'An error occurred \(([^)]+)\)'){return $Matches[1]}
  return 'UnverifiedError'
 }
}'''.replace('__PENDING_LIST__',','.join("'"+c+"'" for c in STAGED_PENDING))

# Right after boot the instance-profile credentials can be briefly unavailable
# through IMDSv2. Before the first staged read the guest waits (bounded) until a token
# and the role's credentials are retrievable, records the wait, and only then lets
# the staged-object policy fail fast on non-pending errors.
IMDS_WAIT_SECONDS=120
PS_IMDS=r'''function Wait-InstanceCredentials([int]$Seconds){
 $started=[DateTime]::UtcNow;$until=$started.AddSeconds($Seconds)
 while($true){
  try{
   $token=(Invoke-WebRequest -UseBasicParsing -Method Put -Uri "$imds/latest/api/token" -Headers @{'X-aws-ec2-metadata-token-ttl-seconds'='60'} -TimeoutSec 2).Content
   $headers=@{'X-aws-ec2-metadata-token'=$token}
   $role=([string](Invoke-WebRequest -UseBasicParsing -Uri "$imds/latest/meta-data/iam/security-credentials/" -Headers $headers -TimeoutSec 2).Content).Split("`n")[0].Trim()
   $document=[string](Invoke-WebRequest -UseBasicParsing -Uri "$imds/latest/meta-data/iam/security-credentials/$role" -Headers $headers -TimeoutSec 2).Content
   if($role -and $document.Contains('"AccessKeyId"')){
    Add-Content "$root\staged-get-samples.log" "$([DateTime]::UtcNow.ToString('o')) imds-credentials ready-after-seconds $([int]([DateTime]::UtcNow-$started).TotalSeconds)";return
   }
  }catch{}
  if([DateTime]::UtcNow -ge $until){throw "Instance role credentials unavailable through IMDSv2 after $Seconds s"}
  Start-Sleep -Seconds 2
 }
}'''

WINDOWS_SETUP=r'''$ErrorActionPreference='Stop'
$root='C:\sancus-builder'
$env:AWS_CONFIG_FILE="$root\aws-config"
$aws='C:\Program Files\Amazon\AWSCLIV2\aws.exe'
__STAGED_GET_PS__
$manifest=Get-Content "$root\manifest.json" -Raw | ConvertFrom-Json
if($manifest.account -ne '524329886851' -or $manifest.region -ne 'us-east-1'){throw 'Wrong guest manifest account'}
$rows=@($manifest.files | Where-Object { $_.target -in 'all','windows-x64' })
$names=@{}
foreach($row in $rows){
 if($row.fileName -notmatch '^[A-Za-z0-9][A-Za-z0-9_. -]{0,180}$' -or $row.fileName.Contains('..') -or $row.objectKey -notmatch '^payload/input-[0-9]{3}\.bin$' -or $row.sha256 -notmatch '^[0-9a-f]{64}$'){throw 'Unsafe bootstrap input'}
 if($names.ContainsKey($row.fileName.ToLowerInvariant())){throw 'Repeated input name'}
 $names[$row.fileName.ToLowerInvariant()]=$true
 $file=Join-Path "$root\cache" $row.fileName
 Get-StagedObject $row.objectKey $file 60
 if((Get-Item $file).Length -ne $row.size -or (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant() -ne $row.sha256){throw 'Frozen bootstrap input differs'}
}
function InputFile($role){
 $values=@($rows | Where-Object { $_.role -eq $role })
 if($values.Count -ne 1){throw "One frozen input required for $role"}
 return Join-Path "$root\cache" $values[0].fileName
}
function Install($file,$arguments){
 $process=Start-Process -FilePath $file -ArgumentList $arguments -Wait -PassThru
 if($process.ExitCode -notin 0,3010){throw "Bootstrap installer failed: $($process.ExitCode)"}
}
Install (InputFile 'git') '/VERYSILENT /NORESTART /SP-'
$pwsh=InputFile 'pwsh';Install 'msiexec.exe' "/i `"$pwsh`" /qn /norestart"
$python=InputFile 'python'
Install $python "/quiet InstallAllUsers=1 TargetDir=`"$root\Python`" PrependPath=0 Include_test=0 Include_launcher=0"
Expand-Archive (InputFile 'dotnet') "$root\dotnet"
$driver=InputFile 'driver'
$signature=Get-AuthenticodeSignature $driver
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'NVIDIA'){throw 'NVIDIA driver signature invalid'}
Install $driver '-s -noreboot'
# Shared with windows-test-desktop.ps1 -VerifyOnly: services Automatic (Manual
# services stayed stopped after the reboot in run 41151908001), the reviewed
# archive hash, both signatures, then the exact default-endpoint gate.
. "$root\bin\windows-audio.ps1"
if($BlastAudioArchiveSha256 -ne 'b950e39f01af1d04ea623c8f6d8eb9b6ea5c477c637295fabf20631c85116bfb'){throw 'Reviewed virtual-audio pin differs'}
$audioInstall=Install-BlastAudio (InputFile 'audio') "$root\audio"
$audioInstall | ConvertTo-Json | Set-Content "$root\audio-install.json"
$audioGate=Assert-BlastAudio 90
$audioGate | ConvertTo-Json -Depth 5 | Set-Content "$root\audio-gate-system.json"
& "$root\Python\python.exe" -m venv "$root\venv"
if($LASTEXITCODE -ne 0){throw 'Owned Python venv failed'}
& "$root\venv\Scripts\python.exe" -m pip install --no-index --find-links "$root\cache" Pillow==13.0.0
if($LASTEXITCODE -ne 0){throw 'Offline Pillow install failed'}
$password=[Guid]::NewGuid().ToString('N')+'aA1!'
New-LocalUser -Name BlastQa -Password (ConvertTo-SecureString $password -AsPlainText -Force) -PasswordNeverExpires | Out-Null
Add-LocalGroupMember -SID 'S-1-5-32-545' -Member BlastQa
& icacls $root /grant "$env:COMPUTERNAME\BlastQa`:(OI)(CI)M" /T /C | Out-Null
if($LASTEXITCODE -ne 0){throw 'Owned standard-user directory permission failed'}
$logon='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
@{DefaultUserName='BlastQa';DefaultDomainName=$env:COMPUTERNAME;DefaultPassword=$password;AutoAdminLogon='1';ForceAutoLogon='1'}.GetEnumerator() | ForEach-Object {Set-ItemProperty $logon -Name $_.Key -Value $_.Value}
$password=$null
$action=New-ScheduledTaskAction -Execute 'C:\Program Files\PowerShell\7\pwsh.exe' -Argument '-NoProfile -File C:\sancus-builder\bin\worker.ps1'
$principal=New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\BlastQa" -LogonType Interactive -RunLevel Limited
$trigger=New-ScheduledTaskTrigger -AtLogOn -User "$env:COMPUTERNAME\BlastQa"
Register-ScheduledTask -TaskName sancus-builder-run -Action $action -Principal $principal -Trigger $trigger | Out-Null
Set-Content "$root\bootstrap.status" 'driver installed; rebooting into owned standard-user desktop; NOT graphics qualification'
if(-not(Write-BlastConsole @('SANCUS-BUILDER-BOOTSTRAP-SYSTEM-READY audio gate passed; rebooting into the standard-user desktop'))){Set-Content "$root\console-unavailable.txt" 'SYSTEM-READY marker not written'}
Restart-Computer -Force
'''
WINDOWS_SETUP=WINDOWS_SETUP.replace('__STAGED_GET_PS__',PS_STAGED)

WINDOWS_DISPATCH=r'''param([Parameter(Mandatory=$true)][string]$Descriptor)
$ErrorActionPreference='Stop'
$root='C:\sancus-builder'
if(Test-Path "$root\task.json"){throw 'Existing task: never blindly dispatch twice'}
if(-not(Test-Path "$root\bootstrap.status")){throw 'Bootstrap not ready'}
$bytes=[Convert]::FromBase64String($Descriptor)
$file=[IO.File]::Open("$root\task.pending",[IO.FileMode]::CreateNew)
try{$file.Write($bytes,0,$bytes.Length)}finally{$file.Dispose()}
Move-Item "$root\task.pending" "$root\task.json"
Start-ScheduledTask -TaskName sancus-builder-run
'''

WINDOWS_WORKER=r'''$ErrorActionPreference='Stop'
$root='C:\sancus-builder'
$env:AWS_CONFIG_FILE="$root\aws-config"
$env:DOTNET_ROOT="$root\dotnet"
$env:DOTNET_CLI_HOME="$root\dotnet-home"
$env:PATH="$root\venv\Scripts;$root\dotnet;C:\Program Files\Git\cmd;C:\Program Files\PowerShell\7;C:\Program Files\Amazon\AWSCLIV2;$env:PATH"
. "$root\bin\windows-audio.ps1"
Assert-BlastAudio 120 | ConvertTo-Json -Depth 5 | Set-Content "$root\audio-gate-user.json"
& "$root\venv\Scripts\python.exe" "$root\bin\builder_guest_ready.py" --manifest "$root\manifest.json" --target windows-x64 --output "$root\bootstrap-ready.json" *> "$root\readiness.log"
if($LASTEXITCODE -ne 0){throw 'Standard-user bootstrap readiness failed'}
$taskDeadline=[DateTime]::UtcNow.AddMinutes(15)
while(-not(Test-Path "$root\task.json")){
 if([DateTime]::UtcNow -ge $taskDeadline){throw 'No owned task arrived in the bounded window'}
 Start-Sleep -Seconds 2
}
try{
 & "$root\venv\Scripts\python.exe" "$root\bin\builder_guest.py" --task "$root\task.json" --manifest "$root\manifest.json" --cache "$root\cache" --work "$root\work" *> "$root\worker.log"
 $workerStatus=$LASTEXITCODE
}catch{$workerStatus=1;$_ | Out-File -Append "$root\worker.log"}
& "$root\venv\Scripts\python.exe" "$root\bin\builder_guest_exit.py" --manifest "$root\manifest.json" --target windows-x64 --code $workerStatus --log "$root\worker.log" --output "$root\worker-exit.json"
exit $workerStatus
'''


def bundle(run,output):
    bucket=builder_staging.bucket_name(run);output=Path(output)
    if output.exists():raise FileExistsError('Never replace a frozen bootstrap bundle')
    with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED)as archive:
        for name in MODULES:archive.write(Path(__file__).parent/name,name)
        archive.write(WINDOWS_AUDIO,'windows-audio.ps1')
        for name,text in {'setup.ps1':WINDOWS_SETUP,'dispatch.ps1':WINDOWS_DISPATCH,'worker.ps1':WINDOWS_WORKER}.items():
            archive.writestr(name,text.replace('__BUCKET__',bucket))
    return {'path':str(output),'sha256':hashlib.sha256(output.read_bytes()).hexdigest()}


def userdata(plan,helper_sha,manifest_sha,aws_cli):
    """Windows uses its AMI's AWS SDK to fetch the CLI from the owned bucket.

    Even that first GetObject request includes ExpectedBucketOwner. The SDK uses
    the disposable instance role; no operator credentials or hand-written signer.
    """
    for sha in (helper_sha,manifest_sha,aws_cli['sha256']):
        if not re.fullmatch(r'[0-9a-f]{64}',sha):raise ValueError('Pinned bootstrap digests required')
    if aws_cli['url']!='https://awscli.amazonaws.com/AWSCLIV2.msi':raise ValueError('Only the official AWS CLI bootstrap URL is allowed')
    bucket=builder_staging.bucket_name(plan['runId'])
    windows=r'''<powershell>
$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$root='C:\sancus-builder'
New-Item -ItemType Directory -Force "$root\bin","$root\cache" | Out-Null
Start-Transcript "$root\bootstrap.log"
# EC2 get-console-output survives termination: copy the transcript tail to COM1.
trap {
 $failure=[string]$_
 try{Stop-Transcript|Out-Null}catch{}
 $lines=@("SANCUS-BUILDER-BOOTSTRAP-FAILED $failure")+@(Get-Content "$root\bootstrap.log" -Tail 80 -ErrorAction SilentlyContinue|ForEach-Object{"SANCUS-BUILDER-BOOTSTRAP-FAILED| $_"})
 try{$port=New-Object IO.Ports.SerialPort 'COM1',115200,'None',8,'One';$port.WriteTimeout=5000;$port.Open();try{foreach($line in $lines){$port.WriteLine($line)}}finally{$port.Close()}}
 catch{Set-Content "$root\console-unavailable.txt" ([string]$_)}
 break
}
foreach($module in @('AWS.Tools.S3','AWSPowerShell.NetCore','AWSPowerShell')){
 if(Get-Module -ListAvailable $module){Import-Module $module;break}
}
$config=[Amazon.S3.AmazonS3Config]::new()
$config.RegionEndpoint=[Amazon.RegionEndpoint]::USEast1
$config.Timeout=[TimeSpan]::FromSeconds(25)
$config.MaxErrorRetry=1
$client=[Amazon.S3.AmazonS3Client]::new($config)
$request=[Amazon.S3.Model.GetObjectRequest]::new()
$request.BucketName='__BUCKET__';$request.Key='payload/bootstrap-awscli.msi'
$request.ExpectedBucketOwner='524329886851'
__STAGED_GET_PS__
__IMDS_WAIT_PS__
$imds='http://169.254.169.254'
Wait-InstanceCredentials __IMDS_SECONDS__
# Before the CLI exists the same policy reads through the preinstalled AWS SDK.
Wait-StagedObject 'payload/bootstrap-awscli.msi' ([DateTime]::UtcNow.AddMinutes(5)) {
 try{
  $response=$client.GetObjectAsync($request,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
  $file=[IO.File]::Create("$root\aws.msi")
  try{$response.ResponseStream.CopyTo($file)}finally{$file.Dispose();$response.Dispose()}
  return $null
 }catch{
  $code=$_.Exception.InnerException.ErrorCode
  if($code){return [string]$code}
  throw
 }
}
$client.Dispose()
if((Get-FileHash -Algorithm SHA256 "$root\aws.msi").Hash.ToLowerInvariant() -ne '__AWS_SHA__' -or (Get-AuthenticodeSignature "$root\aws.msi").Status -ne 'Valid'){throw 'Pinned signed AWS CLI bootstrap differs'}
$install=Start-Process msiexec.exe -ArgumentList "/i `"$root\aws.msi`" /qn /norestart" -PassThru -Wait
if($install.ExitCode -notin 0,3010){throw 'AWS CLI bootstrap failed'}
Set-Content "$root\aws-config" "[profile sancus]`nregion=us-east-1`n"
$env:AWS_CONFIG_FILE="$root\aws-config"
$aws='C:\Program Files\Amazon\AWSCLIV2\aws.exe'
Get-StagedObject 'payload/bootstrap.zip' "$root\bootstrap.zip" 300
if((Get-FileHash -Algorithm SHA256 "$root\bootstrap.zip").Hash.ToLowerInvariant() -ne '__HELPER_SHA__'){throw 'Bootstrap bundle differs'}
Get-StagedObject 'payload/manifest.json' "$root\manifest.json" 60
if((Get-FileHash -Algorithm SHA256 "$root\manifest.json").Hash.ToLowerInvariant() -ne '__MANIFEST_SHA__'){throw 'Frozen input manifest differs'}
Expand-Archive "$root\bootstrap.zip" "$root\bin"
& "$root\bin\setup.ps1"
</powershell>
'''
    replacements={'__STAGED_GET_PS__':PS_STAGED,'__IMDS_WAIT_PS__':PS_IMDS,
                  '__IMDS_SECONDS__':str(IMDS_WAIT_SECONDS),'__BUCKET__':bucket,'__HELPER_SHA__':helper_sha,'__MANIFEST_SHA__':manifest_sha,'__AWS_SHA__':aws_cli['sha256']}
    def replace(text):
        for key,value in replacements.items():text=text.replace(key,value)
        if len(text.encode())>16384:raise ValueError('EC2 raw UserData exceeds its fixed size limit')
        return text
    return {'Windows':replace(windows)}

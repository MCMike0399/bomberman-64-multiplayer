"""Disposable OS setup. All game/source/tool payloads come from the owned bucket."""
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
# Ubuntu 24.04 has no awscli apt package (campaign 2 attempt 2, run 41151908001).
# The official versioned AWS CLI v2 bundle is pinned by digest and verified before unzip.
LINUX_AWS_CLI={'url':'https://awscli.amazonaws.com/awscli-exe-linux-x86_64-2.38.5.zip',
               'sha256':'5def7d1b7e55d892f9a381bb2cb2081a685712464be0d36d5d706af0ac48d0d3'}
LINUX_AWS='/opt/sancus-builder/aws-cli/bin/aws'
PACKAGES_BEGIN='# >>> sancus-builder:packages'
PACKAGES_END='# <<< sancus-builder:packages'

# Guests have no s3:ListBucket, so S3 answers a key the controller has not staged
# yet with 403 AccessDenied, not 404 NoSuchKey (runs 41178169234, 41198567742).
# Every guest object read goes through one helper per language: missing (404/403)
# and S3 throttling/timeout codes are pending until the caller's bounded deadline,
# each new code is sampled to staged-get-samples.log, and any other error fails at
# once. Pinned hashes and sizes still decide content after a successful read.
STAGED_PENDING=('NoSuchKey','404','NotFound','AccessDenied','403',
                'SlowDown','RequestTimeout','ServiceUnavailable','InternalError')
BASH_STAGED=r'''staged_get(){
 local key=$1 file=$2 deadline=$((SECONDS+$3)) code='' last=''
 while :; do
  if "$aws" --profile sancus --region us-east-1 --cli-connect-timeout 8 --cli-read-timeout 25 s3api get-object --bucket '__BUCKET__' --expected-bucket-owner 524329886851 --key "$key" "$file" >/dev/null 2>"$root/staged-get.err"; then return 0; fi
  code=$(sed -n 's/.*An error occurred (\([^)]*\)).*/\1/p' "$root/staged-get.err" | head -n 1)
  case "$code" in
   __PENDING_CASE__) ;;
   *) cat "$root/staged-get.err" >&2; echo "Staged object $key failed: ${code:-UnverifiedError}" >&2; return 1;;
  esac
  if test "$code" != "$last"; then printf '%s %s %s\n' "$(date -u +%FT%TZ)" "$key" "$code" >> "$root/staged-get-samples.log"; last=$code; fi
  if ((SECONDS >= deadline)); then echo "Timed out waiting for staged object $key; last $code" >&2; return 1; fi
  sleep 5
 done
}'''.replace('__PENDING_CASE__','|'.join(STAGED_PENDING))
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

LINUX_SETUP=r'''#!/bin/bash
set -euo pipefail
root=/opt/sancus-builder
exec >> "$root/bootstrap.log" 2>&1
export AWS_CONFIG_FILE="$root/aws-config"
export AWS_EC2_METADATA_DISABLED=false
aws=/opt/sancus-builder/aws-cli/bin/aws
test -x "$aws"
__STAGED_GET__
python3 - "$root" <<'PY'
import json,sys
from pathlib import Path
root=Path(sys.argv[1]);sys.path.insert(0,str(root/'bin'))
import builder_guest
manifest=json.loads((root/'manifest.json').read_text())
descriptor={key:manifest[key]for key in ('runId','source','account','region')};descriptor['target']='linux-x64'
rows=builder_guest.validate_inputs(manifest,descriptor)
(root/'inputs.tsv').write_text(''.join('\t'.join(str(row[key])for key in ('fileName','objectKey','sha256','size'))+'\n'for row in rows))
PY
while IFS=$'\t' read -r file key sha size; do
 staged_get "$key" "$root/cache/$file" 60
 test "$(stat -c %s "$root/cache/$file")" = "$size"
 printf '%s  %s\n' "$sha" "$root/cache/$file" | sha256sum -c -
done < "$root/inputs.tsv"
sdk=$(python3 - "$root" <<'PY'
import json,sys
from pathlib import Path
root=Path(sys.argv[1]);rows=[r for r in json.loads((root/'manifest.json').read_text())['files']if r['role']=='dotnet'and r['target']in('all','linux-x64')]
assert len(rows)==1
print(root/'cache'/rows[0]['fileName'])
PY
)
mkdir -p "$root/dotnet"
tar -xzf "$sdk" -C "$root/dotnet"
python3 -m venv "$root/venv"
"$root/venv/bin/python" -m pip install --no-index --find-links "$root/cache" Pillow==13.0.0
icd=''
for candidate in /usr/share/vulkan/icd.d/nvidia_icd.json /etc/vulkan/icd.d/nvidia_icd.json; do
 if test -f "$candidate"; then icd="$candidate"; break; fi
done
test -n "$icd"
printf '%s' "$icd" > "$root/nvidia-icd.txt"
chown -R ubuntu:ubuntu "$root"
chown -R root:root "$root/aws-cli"
chmod -R go-w "$root/aws-cli"
printf 'bootstrap-ready; desktop and hardware NOT YET QUALIFIED\n' > "$root/bootstrap.status"
runuser -u ubuntu -- env AWS_CONFIG_FILE="$root/aws-config" "$root/venv/bin/python" "$root/bin/builder_guest_ready.py" --manifest "$root/manifest.json" --target linux-x64 --output "$root/bootstrap-ready.json"
'''
LINUX_SETUP=LINUX_SETUP.replace('__STAGED_GET__',BASH_STAGED)

LINUX_DISPATCH=r'''#!/bin/bash
set -euo pipefail
root=/opt/sancus-builder
test "$#" = 1
test ! -e "$root/task.json"
printf '%s' "$1" | base64 --decode > "$root/task.json"
chmod 644 "$root/task.json"
test -f "$root/bootstrap.status"
export AWS_CONFIG_FILE="$root/aws-config"
export DOTNET_ROOT="$root/dotnet"
export DOTNET_CLI_HOME="$root/dotnet-home"
export VK_DRIVER_FILES="$(cat "$root/nvidia-icd.txt")"
export PATH="$root/dotnet:$root/venv/bin:$PATH"
worker_status=0
runuser -u ubuntu -- env AWS_CONFIG_FILE="$root/aws-config" DOTNET_ROOT="$root/dotnet" DOTNET_CLI_HOME="$root/dotnet-home" VK_DRIVER_FILES="$VK_DRIVER_FILES" PATH="$root/dotnet:$root/venv/bin:/usr/local/bin:/usr/bin:/bin" "$root/venv/bin/python" "$root/bin/builder_guest.py" --task "$root/task.json" --manifest "$root/manifest.json" --cache "$root/cache" --work "$root/work" > "$root/worker.log" 2>&1 || worker_status=$?
"$root/venv/bin/python" "$root/bin/builder_guest_exit.py" --manifest "$root/manifest.json" --target linux-x64 --code "$worker_status" --log "$root/worker.log" --output "$root/worker-exit.json"
exit "$worker_status"
'''

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
        for name,text in {'setup.sh':LINUX_SETUP,'dispatch':LINUX_DISPATCH,'setup.ps1':WINDOWS_SETUP,
                          'dispatch.ps1':WINDOWS_DISPATCH,'worker.ps1':WINDOWS_WORKER}.items():
            archive.writestr(name,text.replace('__BUCKET__',bucket))
    return {'path':str(output),'sha256':hashlib.sha256(output.read_bytes()).hexdigest()}


def userdata(plan,helper_sha,manifest_sha,aws_cli,linux_cli=LINUX_AWS_CLI):
    """Windows uses its AMI's AWS SDK to fetch the CLI from the owned bucket.

    Even that first GetObject request includes ExpectedBucketOwner. The SDK uses
    the disposable instance role; no operator credentials or hand-written signer.
    Linux installs the pinned official AWS CLI v2 bundle (digest checked before
    unzip) into a root-owned directory and calls that exact binary.
    """
    for sha in (helper_sha,manifest_sha,aws_cli['sha256'],linux_cli['sha256']):
        if not re.fullmatch(r'[0-9a-f]{64}',sha):raise ValueError('Pinned bootstrap digests required')
    if aws_cli['url']!='https://awscli.amazonaws.com/AWSCLIV2.msi':raise ValueError('Only the official AWS CLI bootstrap URL is allowed')
    if not re.fullmatch(r'https://awscli\.amazonaws\.com/awscli-exe-linux-x86_64-2\.[0-9]+\.[0-9]+\.zip',linux_cli['url']):
        raise ValueError('Only a versioned official AWS CLI v2 Linux bundle is allowed')
    bucket=builder_staging.bucket_name(plan['runId'])
    linux=r'''#!/bin/bash
set -Eeuo pipefail
root=/opt/sancus-builder
mkdir -p "$root/bin" "$root/cache"
exec >> "$root/bootstrap.log" 2>&1
console(){
 for tty in ${SANCUS_BUILDER_CONSOLE:-/dev/console /dev/ttyS0}; do
  if test -w "$tty"; then
   { printf '%s\n' "$1"; if test -n "${2:-}"; then tail -n 80 "$root/bootstrap.log" | sed "s/^/$2| /"; fi; } | timeout 20 tee -a "$tty" >/dev/null || true
  fi
 done
}
trap 'status=$?; trap - ERR; console "SANCUS-BUILDER-BOOTSTRAP-FAILED status=$status line=$LINENO command=$BASH_COMMAND" SANCUS-BUILDER-BOOTSTRAP-FAILED; exit "$status"' ERR
__PACKAGES_BEGIN__
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y curl ca-certificates git unzip python3 python3-venv fuse3 squashfs-tools xvfb openbox pulseaudio vulkan-tools libxkbcommon0 libxcursor1 libxinerama1 libxi6 libxrandr2 libpulse0
install -d -m 700 -o root -g root "$root/awscli-install"
curl --fail --silent --show-error --location --proto '=https' --tlsv1.2 --retry 5 --retry-delay 3 --connect-timeout 15 --max-time 300 -o "$root/awscli-install/awscliv2.zip" '__AWS_LINUX_URL__'
printf '%s  %s\n' '__AWS_LINUX_SHA__' "$root/awscli-install/awscliv2.zip" | sha256sum -c -
unzip -q "$root/awscli-install/awscliv2.zip" -d "$root/awscli-install"
install -d -m 755 -o root -g root "$root/aws-cli"
"$root/awscli-install/aws/install" --install-dir "$root/aws-cli/dist" --bin-dir "$root/aws-cli/bin"
rm -rf "$root/awscli-install"
chown -R root:root "$root/aws-cli"
chmod -R go-w "$root/aws-cli"
aws=/opt/sancus-builder/aws-cli/bin/aws
"$aws" --version
__PACKAGES_END__
printf '[profile sancus]\nregion=us-east-1\n' > "$root/aws-config"
export AWS_CONFIG_FILE="$root/aws-config"
__STAGED_GET__
staged_get payload/bootstrap.zip "$root/bootstrap.zip" 300
printf '%s  %s\n' '__HELPER_SHA__' "$root/bootstrap.zip" | sha256sum -c -
staged_get payload/manifest.json "$root/manifest.json" 60
printf '%s  %s\n' '__MANIFEST_SHA__' "$root/manifest.json" | sha256sum -c -
unzip -q "$root/bootstrap.zip" -d "$root/bin"
chmod 700 "$root/bin/dispatch"
bash "$root/bin/setup.sh"
test -s "$root/bootstrap-ready.json"
console "SANCUS-BUILDER-BOOTSTRAP-READY linux-x64"
'''
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
    replacements={'__STAGED_GET__':BASH_STAGED,'__STAGED_GET_PS__':PS_STAGED,'__BUCKET__':bucket,'__HELPER_SHA__':helper_sha,'__MANIFEST_SHA__':manifest_sha,'__AWS_SHA__':aws_cli['sha256'],
                  '__AWS_LINUX_URL__':linux_cli['url'],'__AWS_LINUX_SHA__':linux_cli['sha256'],
                  '__PACKAGES_BEGIN__':PACKAGES_BEGIN,'__PACKAGES_END__':PACKAGES_END}
    def replace(text):
        for key,value in replacements.items():text=text.replace(key,value)
        if len(text.encode())>16384:raise ValueError('EC2 raw UserData exceeds its fixed size limit')
        return text
    return {'Linux':replace(linux),'Windows':replace(windows)}


def linux_package_section(script):
    """The user-data prologue through the package/AWS CLI install, for the free pre-flight."""
    if script.count(PACKAGES_BEGIN)!=1 or script.count(PACKAGES_END)!=1:raise ValueError('Package section markers missing')
    return script[:script.index(PACKAGES_END)+len(PACKAGES_END)]+'\n'


def linux_packages(script):
    lines=[line for line in script.splitlines()if line.startswith('apt-get install -y ')]
    if len(lines)!=1:raise ValueError('Exactly one apt install line expected')
    return lines[0].split()[3:]

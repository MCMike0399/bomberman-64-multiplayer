"""Disposable OS setup. All game/source/tool payloads come from the owned bucket."""
import hashlib
import json
from pathlib import Path
import re
import zipfile

import builder_namespace as names
import builder_staging


MODULES=('builder_guest.py','builder_payload.py','builder_s3.py','builder_staging.py',
         'builder_namespace.py','native_execution.py')

LINUX_SETUP=r'''#!/bin/bash
set -euo pipefail
root=/opt/sancus-builder
exec >> "$root/bootstrap.log" 2>&1
export AWS_CONFIG_FILE="$root/aws-config"
export AWS_EC2_METADATA_DISABLED=false
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
 aws --profile sancus --region us-east-1 --cli-connect-timeout 8 --cli-read-timeout 25 s3api get-object --bucket '__BUCKET__' --expected-bucket-owner 524329886851 --key "$key" "$root/cache/$file" >/dev/null
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
printf 'bootstrap-ready; desktop and hardware NOT YET QUALIFIED\n' > "$root/bootstrap.status"
'''

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
runuser -u ubuntu -- "$root/venv/bin/python" "$root/bin/builder_guest.py" --task "$root/task.json" --manifest "$root/manifest.json" --cache "$root/cache" --work "$root/work" > "$root/worker.log" 2>&1
'''

WINDOWS_SETUP=r'''$ErrorActionPreference='Stop'
$root='C:\sancus-builder'
$env:AWS_CONFIG_FILE="$root\aws-config"
$aws='C:\Program Files\Amazon\AWSCLIV2\aws.exe'
$manifest=Get-Content "$root\manifest.json" -Raw | ConvertFrom-Json
if($manifest.account -ne '524329886851' -or $manifest.region -ne 'us-east-1'){throw 'Wrong guest manifest account'}
$rows=@($manifest.files | Where-Object { $_.target -in 'all','windows-x64' })
$names=@{}
foreach($row in $rows){
 if($row.fileName -notmatch '^[A-Za-z0-9][A-Za-z0-9_. -]{0,180}$' -or $row.fileName.Contains('..') -or $row.objectKey -notmatch '^payload/input-[0-9]{3}\.bin$' -or $row.sha256 -notmatch '^[0-9a-f]{64}$'){throw 'Unsafe bootstrap input'}
 if($names.ContainsKey($row.fileName.ToLowerInvariant())){throw 'Repeated input name'}
 $names[$row.fileName.ToLowerInvariant()]=$true
 $file=Join-Path "$root\cache" $row.fileName
 & $aws --profile sancus --region us-east-1 --cli-connect-timeout 8 --cli-read-timeout 25 s3api get-object --bucket '__BUCKET__' --expected-bucket-owner 524329886851 --key $row.objectKey $file | Out-Null
 if($LASTEXITCODE -ne 0 -or (Get-Item $file).Length -ne $row.size -or (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant() -ne $row.sha256){throw 'Frozen bootstrap input differs'}
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
Register-ScheduledTask -TaskName sancus-builder-run -Action $action -Principal $principal | Out-Null
Set-Content "$root\bootstrap.status" 'driver installed; rebooting into owned standard-user desktop; NOT graphics qualification'
Restart-Computer -Force
'''

WINDOWS_DISPATCH=r'''param([Parameter(Mandatory=$true)][string]$Descriptor)
$ErrorActionPreference='Stop'
$root='C:\sancus-builder'
if(Test-Path "$root\task.json"){throw 'Existing task: never blindly dispatch twice'}
if(-not(Test-Path "$root\bootstrap.status")){throw 'Bootstrap not ready'}
$bytes=[Convert]::FromBase64String($Descriptor)
$file=[IO.File]::Open("$root\task.json",[IO.FileMode]::CreateNew)
try{$file.Write($bytes,0,$bytes.Length)}finally{$file.Dispose()}
Start-ScheduledTask -TaskName sancus-builder-run
'''

WINDOWS_WORKER=r'''$ErrorActionPreference='Stop'
$root='C:\sancus-builder'
$env:AWS_CONFIG_FILE="$root\aws-config"
$env:DOTNET_ROOT="$root\dotnet"
$env:DOTNET_CLI_HOME="$root\dotnet-home"
$env:PATH="$root\venv\Scripts;$root\dotnet;C:\Program Files\Git\cmd;C:\Program Files\PowerShell\7;C:\Program Files\Amazon\AWSCLIV2;$env:PATH"
& "$root\venv\Scripts\python.exe" "$root\bin\builder_guest.py" --task "$root\task.json" --manifest "$root\manifest.json" --cache "$root\cache" --work "$root\work" *> "$root\worker.log"
exit $LASTEXITCODE
'''


def bundle(run,output):
    bucket=builder_staging.bucket_name(run);output=Path(output)
    if output.exists():raise FileExistsError('Never replace a frozen bootstrap bundle')
    with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED)as archive:
        for name in MODULES:archive.write(Path(__file__).parent/name,name)
        for name,text in {'setup.sh':LINUX_SETUP,'dispatch':LINUX_DISPATCH,'setup.ps1':WINDOWS_SETUP,
                          'dispatch.ps1':WINDOWS_DISPATCH,'worker.ps1':WINDOWS_WORKER}.items():
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
    linux=r'''#!/bin/bash
set -euo pipefail
root=/opt/sancus-builder
mkdir -p "$root/bin" "$root/cache"
exec >> "$root/bootstrap.log" 2>&1
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y awscli git unzip python3 python3-venv fuse3 squashfs-tools xvfb openbox pulseaudio vulkan-tools libxkbcommon0 libxcursor1 libxinerama1 libxi6 libxrandr2 libpulse0
printf '[profile sancus]\nregion=us-east-1\n' > "$root/aws-config"
export AWS_CONFIG_FILE="$root/aws-config"
bootstrap_deadline=$((SECONDS+300))
while ((SECONDS < bootstrap_deadline)); do
 if aws --profile sancus --region us-east-1 --cli-connect-timeout 8 --cli-read-timeout 25 s3api get-object --bucket '__BUCKET__' --expected-bucket-owner 524329886851 --key payload/bootstrap.zip "$root/bootstrap.zip" >/dev/null; then break; fi
 sleep 5
done
printf '%s  %s\n' '__HELPER_SHA__' "$root/bootstrap.zip" | sha256sum -c -
aws --profile sancus --region us-east-1 --cli-connect-timeout 8 --cli-read-timeout 25 s3api get-object --bucket '__BUCKET__' --expected-bucket-owner 524329886851 --key payload/manifest.json "$root/manifest.json" >/dev/null
printf '%s  %s\n' '__MANIFEST_SHA__' "$root/manifest.json" | sha256sum -c -
unzip -q "$root/bootstrap.zip" -d "$root/bin"
chmod 700 "$root/bin/dispatch"
bash "$root/bin/setup.sh"
'''
    windows=r'''<powershell>
$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$root='C:\sancus-builder'
New-Item -ItemType Directory -Force "$root\bin","$root\cache" | Out-Null
Start-Transcript "$root\bootstrap.log"
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
$bootstrapDeadline=[DateTime]::UtcNow.AddMinutes(5)
while([DateTime]::UtcNow -lt $bootstrapDeadline){
 try{
  $response=$client.GetObjectAsync($request).GetAwaiter().GetResult()
  $file=[IO.File]::Create("$root\aws.msi")
  try{$response.ResponseStream.CopyTo($file)}finally{$file.Dispose();$response.Dispose()}
  break
 }catch{
  if($_.Exception.InnerException.ErrorCode -ne 'NoSuchKey'){throw}
  Start-Sleep -Seconds 5
 }
}
$client.Dispose()
if((Get-FileHash -Algorithm SHA256 "$root\aws.msi").Hash.ToLowerInvariant() -ne '__AWS_SHA__' -or (Get-AuthenticodeSignature "$root\aws.msi").Status -ne 'Valid'){throw 'Pinned signed AWS CLI bootstrap differs'}
$install=Start-Process msiexec.exe -ArgumentList "/i `"$root\aws.msi`" /qn /norestart" -PassThru -Wait
if($install.ExitCode -notin 0,3010){throw 'AWS CLI bootstrap failed'}
Set-Content "$root\aws-config" "[profile sancus]`nregion=us-east-1`n"
$env:AWS_CONFIG_FILE="$root\aws-config"
$aws='C:\Program Files\Amazon\AWSCLIV2\aws.exe'
$bootstrapDeadline=[DateTime]::UtcNow.AddMinutes(5)
while([DateTime]::UtcNow -lt $bootstrapDeadline){
 & $aws --profile sancus --region us-east-1 --cli-connect-timeout 8 --cli-read-timeout 25 s3api get-object --bucket '__BUCKET__' --expected-bucket-owner 524329886851 --key payload/bootstrap.zip "$root\bootstrap.zip" | Out-Null
 if($LASTEXITCODE -eq 0){break};Start-Sleep -Seconds 5
}
if((Get-FileHash -Algorithm SHA256 "$root\bootstrap.zip").Hash.ToLowerInvariant() -ne '__HELPER_SHA__'){throw 'Bootstrap bundle differs'}
& $aws --profile sancus --region us-east-1 --cli-connect-timeout 8 --cli-read-timeout 25 s3api get-object --bucket '__BUCKET__' --expected-bucket-owner 524329886851 --key payload/manifest.json "$root\manifest.json" | Out-Null
if($LASTEXITCODE -ne 0 -or (Get-FileHash -Algorithm SHA256 "$root\manifest.json").Hash.ToLowerInvariant() -ne '__MANIFEST_SHA__'){throw 'Frozen input manifest differs'}
Expand-Archive "$root\bootstrap.zip" "$root\bin"
& "$root\bin\setup.ps1"
</powershell>
'''
    replacements={'__BUCKET__':bucket,'__HELPER_SHA__':helper_sha,'__MANIFEST_SHA__':manifest_sha,'__AWS_SHA__':aws_cli['sha256']}
    def replace(text):
        for key,value in replacements.items():text=text.replace(key,value)
        if len(text.encode())>16384:raise ValueError('EC2 raw UserData exceeds its fixed size limit')
        return text
    return {'Linux':replace(linux),'Windows':replace(windows)}

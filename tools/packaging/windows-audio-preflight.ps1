# FREE hosted pre-flight of the builder's Windows virtual-audio bootstrap. No AWS.
# As the disposable runner administrator: the shared Install-BlastAudio section
# (services Automatic, pinned archive hash, both signatures, silent install) and
# the exact SYSTEM gate. Then the same gate as LocalSystem (session 0, like EC2
# user-data) and as a separate, newly created standard (non-admin) local user.
# A runner that cannot host the driver or endpoint fails here; it is never skipped.
param([Parameter(Mandatory = $true)][string]$Output)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $env:RUNNER_TEMP) { throw 'Pre-flight changes services/drivers/users: disposable hosted runner only' }
. (Join-Path $PSScriptRoot 'windows-audio.ps1')
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path
$receipt = [ordered]@{ schemaVersion = 1; host = $PSVersionTable.PSVersion.ToString(); errors = @() }
function Save { $receipt | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $Output 'preflight-windows.json') }

# One check script for every principal; it never installs anything.
$work = Join-Path $env:RUNNER_TEMP 'blast-audio-gate'
New-Item -ItemType Directory -Force -Path $work | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'windows-audio.ps1') (Join-Path $work 'windows-audio.ps1') -Force
@'
param([string]$Label)
$ErrorActionPreference = 'Stop'
$result = Join-Path $PSScriptRoot ("gate-" + $Label + ".json")
try {
    . (Join-Path $PSScriptRoot 'windows-audio.ps1')
    Assert-BlastAudio 60 | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $result
    exit 0
} catch {
    [string]$_ | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot ("gate-" + $Label + "-error.txt"))
    try { Get-BlastAudioState | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $result } catch {}
    exit 1
}
'@ | Set-Content -Encoding utf8 (Join-Path $work 'check.ps1')

function Read-Gate([string]$Label) {
    $path = Join-Path $work ("gate-" + $Label + ".json")
    $errorPath = Join-Path $work ("gate-" + $Label + "-error.txt")
    $value = [ordered]@{ label = $Label }
    if (Test-Path $path) { $value.state = Get-Content $path -Raw | ConvertFrom-Json }
    if (Test-Path $errorPath) { $value.error = Get-Content $errorPath -Raw }
    return $value
}

function Wait-Task([string]$Name, [int]$Seconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    Start-Sleep -Seconds 2
    while ([DateTime]::UtcNow -lt $deadline) {
        $info = Get-ScheduledTaskInfo -TaskName $Name
        # 267009: still running; 267011: not yet run.
        if ((Get-ScheduledTask -TaskName $Name).State -ne 'Running' -and $info.LastTaskResult -notin 267009, 267011) { return $info.LastTaskResult }
        Start-Sleep -Seconds 2
    }
    throw "Scheduled task $Name did not finish within $Seconds seconds"
}

try {
    $receipt.before = Get-BlastAudioState
    $archive = Join-Path $env:RUNNER_TEMP 'VBCABLE_Driver_Pack45.zip'
    Invoke-WebRequest $BlastAudioArchiveUrl -OutFile $archive -UseBasicParsing
    # Exactly the SYSTEM bootstrap's privileged section, then its gate.
    $receipt.install = Install-BlastAudio $archive (Join-Path $env:RUNNER_TEMP 'blast-audio-cable45')
    $receipt.administratorGate = Assert-BlastAudio 90
    Save

    # LocalSystem in session 0, as EC2Launch runs the bootstrap.
    $systemAction = New-ScheduledTaskAction -Execute "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$work\check.ps1`" -Label system"
    Register-ScheduledTask -TaskName blast-audio-gate-system -Action $systemAction -User 'NT AUTHORITY\SYSTEM' -RunLevel Highest -Force | Out-Null
    Start-ScheduledTask -TaskName blast-audio-gate-system
    $receipt.systemExit = Wait-Task blast-audio-gate-system 180
    $receipt.systemGate = Read-Gate system
    Save

    # A separate, newly created standard user. Never an administrator.
    $user = 'BlastPreflight'
    $password = [Guid]::NewGuid().ToString('N') + 'aA1!'
    $secure = ConvertTo-SecureString $password -AsPlainText -Force
    New-LocalUser -Name $user -Password $secure -PasswordNeverExpires | Out-Null
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $user
    $admins = @(Get-LocalGroupMember -SID 'S-1-5-32-544' | Where-Object { $_.Name -match "\\$user$" })
    if ($admins.Count) { throw 'Pre-flight user must not be an administrator' }
    & icacls $work /grant "$env:COMPUTERNAME\$user`:(OI)(CI)M" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot grant the standard user its gate directory' }
    $pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
    $credential = New-Object System.Management.Automation.PSCredential("$env:COMPUTERNAME\$user", $secure)
    try {
        # Same host as the builder's worker.ps1 (PowerShell 7), secondary logon in this session.
        $process = Start-Process -FilePath $pwsh -Credential $credential -LoadUserProfile -WorkingDirectory $work -PassThru -Wait `
            -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "$work\check.ps1", '-Label', 'standard-user'
        $receipt.standardUserMethod = 'Start-Process -Credential'
        $receipt.standardUserExit = $process.ExitCode
    } catch {
        $receipt.errors += 'Secondary logon unavailable: ' + [string]$_
        $userAction = New-ScheduledTaskAction -Execute $pwsh -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$work\check.ps1`" -Label standard-user"
        Register-ScheduledTask -TaskName blast-audio-gate-user -Action $userAction -User "$env:COMPUTERNAME\$user" -Password $password -RunLevel Limited -Force | Out-Null
        Start-ScheduledTask -TaskName blast-audio-gate-user
        $receipt.standardUserMethod = 'Scheduled task (password logon, limited)'
        $receipt.standardUserExit = Wait-Task blast-audio-gate-user 180
    }
    $password = $null
    $receipt.standardUserGate = Read-Gate standard-user
    $state = $receipt.standardUserGate.state
    if ($receipt.systemExit -ne 0 -or -not $receipt.systemGate.state -or $receipt.systemGate.state.passed -ne $true) {
        $receipt.errors += 'SYSTEM gate failed'
    }
    if ($receipt.standardUserExit -ne 0 -or -not $state -or $state.passed -ne $true) { $receipt.errors += 'Standard-user gate failed' }
    if ($state -and ($state.administrator -ne $false -or $state.user -notmatch "\\$user$")) { $receipt.errors += 'Gate did not run as the separate standard user' }
} catch {
    $receipt.errors += [string]$_
}
$receipt.passed = $receipt.errors.Count -eq 0
Copy-Item (Join-Path $work 'gate-*') $Output -ErrorAction SilentlyContinue
Save
Get-Content (Join-Path $Output 'preflight-windows.json')
if (-not $receipt.passed) {
    throw ('Windows virtual-audio pre-flight FAILED (this runner did not prove the builder audio gate): ' + ($receipt.errors -join '; '))
}

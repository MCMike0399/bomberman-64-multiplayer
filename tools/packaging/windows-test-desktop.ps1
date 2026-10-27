# Disposable runner setup only. The game retains its ordinary WASAPI backend.
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true') {
    throw 'Audio fixture setup requires the disposable Windows CI desktop'
}
$output = Join-Path $PSScriptRoot '../../artifacts/windows-audio'
New-Item -ItemType Directory -Force -Path $output | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BlastAudioEndpoint {
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class Enumerator {}
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevices {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IntPtr endpoint);
    }
    public static int Probe() {
        object instance = null;
        IntPtr endpoint = IntPtr.Zero;
        try {
            instance = new Enumerator();
            return ((IDevices)instance).GetDefaultAudioEndpoint(0, 0, out endpoint);
        } catch (COMException error) { return error.HResult; }
        finally {
            if (endpoint != IntPtr.Zero) Marshal.Release(endpoint);
            if (instance != null) Marshal.FinalReleaseComObject(instance);
        }
    }
}
'@
$before = @(Get-Service Audiosrv, AudioEndpointBuilder | Select-Object Name, Status, StartType)
$initial = [BlastAudioEndpoint]::Probe()
$beforeScreen = @([BlastAudioEndpoint]::GetSystemMetrics(0), [BlastAudioEndpoint]::GetSystemMetrics(1))
Set-DisplayResolution -Width 1920 -Height 1200 -Force
$screenClock = [System.Diagnostics.Stopwatch]::StartNew()
while (([BlastAudioEndpoint]::GetSystemMetrics(0) -lt 1920 -or [BlastAudioEndpoint]::GetSystemMetrics(1) -lt 1200) -and $screenClock.Elapsed.TotalSeconds -lt 20) {
    Start-Sleep -Milliseconds 200
}
if ([BlastAudioEndpoint]::GetSystemMetrics(0) -lt 1920 -or [BlastAudioEndpoint]::GetSystemMetrics(1) -lt 1200) {
    throw 'Disposable Windows desktop cannot provide the required 1920x1200 test surface'
}
foreach ($name in @('AudioEndpointBuilder', 'Audiosrv')) {
    if ((Get-Service $name).StartType -eq 'Disabled') { Set-Service -Name $name -StartupType Manual }
    Start-Service -Name $name
    (Get-Service $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
}
if ([BlastAudioEndpoint]::Probe() -ne 0) {
    # CI-only standalone driver, never included in the player installer.
    # Origin/licensing: https://vb-audio.com/Cable/ and /Services/licensing.htm
    $driverRoot = Join-Path $env:RUNNER_TEMP 'blast-audio-cable45'
    New-Item -ItemType Directory -Force -Path $driverRoot | Out-Null
    $archive = Join-Path $driverRoot 'VBCABLE_Driver_Pack45.zip'
    Invoke-WebRequest 'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip' -OutFile $archive
    $driverHash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($driverHash -ne 'b950e39f01af1d04ea623c8f6d8eb9b6ea5c477c637295fabf20631c85116bfb') {
        throw 'Official VB-CABLE 45 archive differs from its reviewed hash'
    }
    Expand-Archive $archive -DestinationPath $driverRoot -Force
    $installer = Join-Path $driverRoot 'VBCABLE_Setup_x64.exe'
    $catalog = Join-Path $driverRoot 'vbaudio_cable64_win10.cat'
    $setupSignature = Get-AuthenticodeSignature $installer
    $catalogSignature = Get-AuthenticodeSignature $catalog
    @{
        sha256 = $driverHash
        setupStatus = [string]$setupSignature.Status
        setupPublisher = $setupSignature.SignerCertificate.Subject
        catalogStatus = [string]$catalogSignature.Status
        catalogPublisher = $catalogSignature.SignerCertificate.Subject
    } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $output 'driver-signatures.json')
    if ($setupSignature.Status -ne 'Valid' -or $setupSignature.SignerCertificate.Subject -notmatch 'BUREL VINCENT' -or
        $catalogSignature.Status -ne 'Valid' -or $catalogSignature.SignerCertificate.Subject -notmatch 'Microsoft Windows Hardware Compatibility Publisher') {
        throw 'Virtual audio installer/catalog must pass ordinary Windows signature validation'
    }
    $driver = Start-Process -FilePath $installer -ArgumentList '-i', '-h' -WorkingDirectory $driverRoot -PassThru
    if (-not $driver.WaitForExit(90000)) {
        $driver.Kill($true)
        $driver.WaitForExit(10000) | Out-Null
        throw 'Virtual audio installation exceeded 90 seconds'
    }
    $driverExitCode = $driver.ExitCode
    Write-Output "Virtual audio installer exit: $driverExitCode; requiring a live WASAPI endpoint next"
}
$clock = [System.Diagnostics.Stopwatch]::StartNew()
do {
    $result = [BlastAudioEndpoint]::Probe()
    if ($result -eq 0) { break }
    Start-Sleep -Milliseconds 200
} while ($clock.Elapsed.TotalSeconds -lt 20)
$report = @{
    beforeServices = $before
    beforeScreen = $beforeScreen
    virtualDriverInstallerExit = $driverExitCode
    beforeEndpoint = ('0x{0:X8}' -f $initial)
    afterServices = @(Get-Service Audiosrv, AudioEndpointBuilder | Select-Object Name, Status, StartType)
    afterEndpoint = ('0x{0:X8}' -f $result)
    devices = @(Get-PnpDevice -Class AudioEndpoint, Media -ErrorAction SilentlyContinue | Select-Object Class, FriendlyName, Status)
    display = @(Get-CimInstance Win32_VideoController | Select-Object Name, CurrentHorizontalResolution, CurrentVerticalResolution)
    screenWidth = [BlastAudioEndpoint]::GetSystemMetrics(0)
    screenHeight = [BlastAudioEndpoint]::GetSystemMetrics(1)
    hasDisplayResolutionCommand = [bool](Get-Command Set-DisplayResolution -ErrorAction SilentlyContinue)
    passed = ($result -eq 0)
}
$json = $report | ConvertTo-Json -Depth 5
$json | Set-Content -Encoding utf8 -Path (Join-Path $output 'environment.json')
Write-Output $json
if ($result -ne 0) { throw 'Windows runner has no usable default WASAPI render endpoint after service setup' }

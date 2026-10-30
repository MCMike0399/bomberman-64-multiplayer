# Disposable runner setup only. The game retains its ordinary WASAPI backend.
param([switch]$OwnedSsm,[switch]$VerifyOnly,[string]$ExpectedRevision)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or ($env:GITHUB_ACTIONS -ne 'true' -and -not $OwnedSsm)) {
    throw 'Audio fixture setup requires the disposable Windows CI desktop'
}
$output = Join-Path $PSScriptRoot '../../artifacts/windows-audio'
New-Item -ItemType Directory -Force -Path $output | Out-Null
if($OwnedSsm){
    if(-not $VerifyOnly -or -not $ExpectedRevision){throw 'Owned standard-user setup requires verification-only mode and frozen source'}
    $context=& python (Join-Path $PSScriptRoot '../ci/native_execution.py') --revision $ExpectedRevision --target windows-x64 --require-ssm
    if($LASTEXITCODE -ne 0){throw 'Actual owned SSM context validation failed'}
    $context | Set-Content -Encoding utf8 (Join-Path $output 'native-context.json')
}
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class BlastAudioEndpoint {
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [StructLayout(LayoutKind.Explicit, Size=220)] private struct Mode {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(72)] public uint Fields;
        [FieldOffset(168)] public uint Bits;
        [FieldOffset(172)] public uint Width;
        [FieldOffset(176)] public uint Height;
        [FieldOffset(184)] public uint Frequency;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string device, uint index, ref Mode mode);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int ChangeDisplaySettingsExW(string device, ref Mode mode, IntPtr window, uint flags, IntPtr parameter);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern bool SystemParametersInfoW(uint action, uint parameter, ref Rect area, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowW(string name, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    public static string[] Modes() {
        var modes=new HashSet<string>();
        for (uint i=0;i<512;i++) {
            var mode=new Mode {Size=220};
            if (!EnumDisplaySettingsW(null,i,ref mode)) break;
            if (mode.Bits==32) modes.Add(mode.Width+"x"+mode.Height);
        }
        var result=new string[modes.Count];modes.CopyTo(result);return result;
    }
    public static int SelectMode(int width,int height) {
        for (uint i=0;i<512;i++) {
            var mode=new Mode {Size=220};
            if (!EnumDisplaySettingsW(null,i,ref mode)) break;
            if (mode.Bits==32 && mode.Width==width && mode.Height==height) {
                mode.Fields=0x00040000|0x00080000|0x00100000|0x00400000;
                return ChangeDisplaySettingsExW(null,ref mode,IntPtr.Zero,0,IntPtr.Zero);
            }
        }
        return -2;
    }
    public static int[] WorkArea() {
        var area=new Rect();
        if (!SystemParametersInfoW(48,0,ref area,0)) throw new InvalidOperationException("Cannot query actual desktop work area");
        return new int[] {area.Right-area.Left,area.Bottom-area.Top};
    }
    public static void UsePhysicalScreen() {
        // Only an actual physical-size working area: never invent pixels beyond
        // the selected OS mode. Hide the CI taskbar to fit the unchanged tests.
        var taskbar=FindWindowW("Shell_TrayWnd",null);
        if (taskbar!=IntPtr.Zero) ShowWindow(taskbar,0);
        var area=new Rect {Right=GetSystemMetrics(0),Bottom=GetSystemMetrics(1)};
        if (!SystemParametersInfoW(47,0,ref area,0)) throw new InvalidOperationException("Cannot set CI work area");
    }
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
$modes = @([BlastAudioEndpoint]::Modes())
$modes | Set-Content -Encoding utf8 (Join-Path $output 'display-modes.txt')
$candidates = @($modes | ForEach-Object {
    $size = $_ -split 'x'
    @{Width=[int]$size[0];Height=[int]$size[1];Area=([int]$size[0]*[int]$size[1])}
} | Where-Object { $_.Width -ge 1648 -and $_.Height -ge 1080 } | Sort-Object Area)
$modeResults = @()
foreach ($mode in $candidates) {
    $code = [BlastAudioEndpoint]::SelectMode($mode.Width,$mode.Height)
    $modeResults += @{width=$mode.Width;height=$mode.Height;result=$code}
    if ($code -ne 0) { continue }
    Start-Sleep -Milliseconds 200
    if ([BlastAudioEndpoint]::GetSystemMetrics(0) -ge 1648 -and [BlastAudioEndpoint]::GetSystemMetrics(1) -ge 1080) {
        [BlastAudioEndpoint]::UsePhysicalScreen()
        $area = [BlastAudioEndpoint]::WorkArea()
        if ($area[0] -ge 1648 -and $area[1] -ge 1080) { break }
    }
}
$area = [BlastAudioEndpoint]::WorkArea()
$screenReady = $area[0] -ge 1648 -and $area[1] -ge 1080
@{modes=$modes;attempts=$modeResults;workArea=$area;ready=$screenReady;
  width=[BlastAudioEndpoint]::GetSystemMetrics(0);height=[BlastAudioEndpoint]::GetSystemMetrics(1)} |
    ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $output 'display.json')
if(-not $VerifyOnly){
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
    displayModes = $modes
    modeResults = $modeResults
    workArea = $area
    screenReady = $screenReady
    virtualDriverInstallerExit = $driverExitCode
    beforeEndpoint = ('0x{0:X8}' -f $initial)
    afterServices = @(Get-Service Audiosrv, AudioEndpointBuilder | Select-Object Name, Status, StartType)
    afterEndpoint = ('0x{0:X8}' -f $result)
    devices = @(Get-PnpDevice -Class AudioEndpoint, Media -ErrorAction SilentlyContinue | Select-Object Class, FriendlyName, Status)
    display = @(Get-CimInstance Win32_VideoController | Select-Object Name, CurrentHorizontalResolution, CurrentVerticalResolution)
    screenWidth = [BlastAudioEndpoint]::GetSystemMetrics(0)
    screenHeight = [BlastAudioEndpoint]::GetSystemMetrics(1)
    hasDisplayResolutionCommand = [bool](Get-Command Set-DisplayResolution -ErrorAction SilentlyContinue)
    passed = ($result -eq 0 -and $screenReady)
}
$json = $report | ConvertTo-Json -Depth 5
$json | Set-Content -Encoding utf8 -Path (Join-Path $output 'environment.json')
Write-Output $json
if ($result -ne 0) { throw 'Windows runner has no usable default WASAPI render endpoint after service setup' }
if (-not $screenReady) { throw 'Windows runner cannot fit the unchanged 1600x1000 client test window' }

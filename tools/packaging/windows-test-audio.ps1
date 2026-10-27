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
foreach ($name in @('AudioEndpointBuilder', 'Audiosrv')) {
    Set-Service -Name $name -StartupType Manual
    Start-Service -Name $name
    (Get-Service $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
}
$clock = [System.Diagnostics.Stopwatch]::StartNew()
do {
    $result = [BlastAudioEndpoint]::Probe()
    if ($result -eq 0) { break }
    Start-Sleep -Milliseconds 200
} while ($clock.Elapsed.TotalSeconds -lt 20)
$report = @{
    beforeServices = $before
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

# Shared disposable-QA virtual audio setup and gate. Never part of a player installer.
# Dot-sourced by the builder SYSTEM bootstrap (Windows PowerShell 5.1), its
# standard-user worker and windows-test-desktop.ps1 -VerifyOnly (PowerShell 7),
# and the free hosted pre-flight. Keep it compatible with both PowerShell versions.
# Origin/licensing of the pinned driver: https://vb-audio.com/Cable/ and /Services/licensing.htm
$BlastAudioArchiveUrl = 'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip'
$BlastAudioArchiveSha256 = 'b950e39f01af1d04ea623c8f6d8eb9b6ea5c477c637295fabf20631c85116bfb'
$BlastAudioServices = @('AudioEndpointBuilder', 'Audiosrv')

if (-not ('BlastAudioState' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BlastAudioState {
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class Enumerator {}
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevices {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice endpoint);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, out IntPtr result);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }
    // HRESULT of the default eRender/eConsole endpoint (the one WASAPI opens)
    // and, on success, its MMDevice ID.
    public static int DefaultRender(out string id) {
        id = null;
        object instance = null;
        IDevice endpoint = null;
        try {
            instance = new Enumerator();
            int result = ((IDevices)instance).GetDefaultAudioEndpoint(0, 0, out endpoint);
            if (result != 0) return result;
            if (endpoint == null) return unchecked((int)0x80070490);
            return endpoint.GetId(out id);
        } catch (COMException error) { return error.HResult; }
        finally {
            if (endpoint != null) Marshal.FinalReleaseComObject(endpoint);
            if (instance != null) Marshal.FinalReleaseComObject(instance);
        }
    }
}
'@
}

# Pinned inside the reviewed archive ($BlastAudioArchiveSha256): INF vbMmeCable64_win10.inf,
# DriverVer 3.3.1.7, CatalogFile vbaudio_cable64_win10.cat with this SHA-256.
$BlastAudioInfName = 'vbMmeCable64_win10.inf'
$BlastAudioCatalogName = 'vbaudio_cable64_win10.cat'
$BlastAudioCatalogSha256 = '0a921ebadbe39cf3fa7c14ff37d1ca22565a9651244bd3ac177dc810bc99072e'
$BlastAudioDriverVersion = '3.3.1.7'

function Get-BlastAudioDriver([string]$InstanceId) {
    $row = [ordered]@{ instanceId = $InstanceId; pinned = $false }
    try {
        $properties = @(Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName 'DEVPKEY_Device_DriverInfPath','DEVPKEY_Device_DriverVersion','DEVPKEY_Device_DriverProvider' -ErrorAction Stop)
        $value = { param($key) ($properties | Where-Object { $_.KeyName -eq $key } | Select-Object -First 1).Data }
        $row.inf = [string](& $value 'DEVPKEY_Device_DriverInfPath')
        $row.version = [string](& $value 'DEVPKEY_Device_DriverVersion')
        $row.provider = [string](& $value 'DEVPKEY_Device_DriverProvider')
        $installed = Join-Path $env:windir ('INF\' + $row.inf)
        $installedHash = (Get-FileHash -Algorithm SHA256 $installed -ErrorAction Stop).Hash
        $repository = Join-Path $env:windir 'System32\DriverStore\FileRepository'
        $package = @(Get-ChildItem $repository -Directory -Filter ($BlastAudioInfName.ToLowerInvariant() + '_*') -ErrorAction Stop |
            Where-Object { (Test-Path (Join-Path $_.FullName $BlastAudioInfName)) -and
                (Get-FileHash -Algorithm SHA256 (Join-Path $_.FullName $BlastAudioInfName)).Hash -eq $installedHash })
        $row.packages = @($package | ForEach-Object { $_.Name })
        if ($package.Count -eq 1) {
            $catalog = Join-Path $package[0].FullName $BlastAudioCatalogName
            $row.catalogSha256 = (Get-FileHash -Algorithm SHA256 $catalog -ErrorAction Stop).Hash.ToLowerInvariant()
            $signature = Get-AuthenticodeSignature $catalog
            $row.catalogSignature = [string]$signature.Status
            $row.catalogPublisher = [string]$signature.SignerCertificate.Subject
            $row.pinned = ($row.catalogSha256 -eq $BlastAudioCatalogSha256 -and $row.catalogSignature -eq 'Valid' -and
                $row.catalogPublisher -match 'Microsoft Windows Hardware Compatibility Publisher' -and $row.version -eq $BlastAudioDriverVersion)
        }
    } catch { $row.error = [string]$_.Exception.Message }
    $row
}

function Get-BlastAudioState {
    $services = @(foreach ($name in $BlastAudioServices) {
        $service = Get-Service -Name $name
        [ordered]@{ name = $name; status = [string]$service.Status; startType = [string]$service.StartType }
    })
    $id = $null
    $result = [BlastAudioState]::DefaultRender([ref]$id)
    $devices = @(Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue |
        Where-Object { $_.PNPClass -eq 'AudioEndpoint' -or $_.PNPClass -eq 'MEDIA' } |
        ForEach-Object { [ordered]@{ class = [string]$_.PNPClass; name = [string]$_.Name; deviceId = [string]$_.DeviceID; status = [string]$_.Status } })
    # VB-CABLE 4.5 names its device 'VB-Audio VirtualCable' (pre-flight run 41157011245).
    $driverDevices = @($devices | Where-Object { $_.class -eq 'MEDIA' -and $_.name -match 'VB-Audio\s*Virtual\s*Cable' -and $_.status -eq 'OK' })
    # The driver is identified from that device, never by name: its installed INF must be the
    # pinned package's vbMmeCable64_win10.inf and its catalog the pinned, Microsoft-signed catalog.
    $drivers = @(foreach ($device in $driverDevices) { Get-BlastAudioDriver $device.deviceId })
    # Render endpoints are SWD\MMDEVAPI\{0.0.0.00000000}.{GUID}; capture uses {0.0.1...}. VB-CABLE 4.5
    # exposes its render endpoints as 'Speakers (VB-Audio Virtual Cable)' and 'CABLE In 16 Ch (...)'.
    $cable = @($devices | Where-Object { $_.class -eq 'AudioEndpoint' -and $_.name -match '\(VB-Audio Virtual Cable\)$' -and $_.status -eq 'OK' -and
        $_.deviceId -match '^SWD\\MMDEVAPI\\\{0\.0\.0\.00000000\}\.' })
    $isDefault = $false
    if ($result -eq 0 -and $id) {
        $isDefault = @($cable | Where-Object { $_.deviceId.EndsWith('\' + $id, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 1
    }
    $servicesReady = @($services | Where-Object { $_.status -eq 'Running' -and $_.startType -eq 'Automatic' }).Count -eq $BlastAudioServices.Count
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    [ordered]@{
        user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        administrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
        services = $services
        servicesAutomaticAndRunning = $servicesReady
        virtualCableDriverInstalled = ($driverDevices.Count -ge 1 -and @($drivers | Where-Object { $_.pinned }).Count -ge 1)
        virtualCableDrivers = $drivers
        virtualCableRenderEndpoints = $cable
        defaultRenderResult = ('0x{0:X8}' -f $result)
        defaultRenderId = $id
        virtualCableIsDefaultRender = $isDefault
        devices = $devices
        passed = ($servicesReady -and $driverDevices.Count -ge 1 -and @($drivers | Where-Object { $_.pinned }).Count -ge 1 -and $cable.Count -ge 1 -and $isDefault)
    }
}

# The single gate used as SYSTEM before readiness, by the standard user after
# the reboot and by -VerifyOnly: both services Automatic and Running, the pinned
# VB-CABLE driver installed, and its render endpoint the default WASAPI device.
function Assert-BlastAudio([int]$Seconds = 60) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $state = Get-BlastAudioState
        if ($state.passed) { return $state }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    throw ('Virtual audio gate failed: ' + ($state | ConvertTo-Json -Depth 5 -Compress))
}

# Privileged setup only (SYSTEM bootstrap or a disposable hosted runner admin).
function Install-BlastAudio([string]$Archive, [string]$Destination) {
    foreach ($name in $BlastAudioServices) {
        Set-Service -Name $name -StartupType Automatic
        Start-Service -Name $name
        (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    }
    $hash = (Get-FileHash -Algorithm SHA256 $Archive).Hash.ToLowerInvariant()
    if ($hash -ne $BlastAudioArchiveSha256) { throw 'Reviewed virtual-audio archive differs' }
    Expand-Archive $Archive -DestinationPath $Destination -Force
    $installer = Join-Path $Destination 'VBCABLE_Setup_x64.exe'
    $catalog = Join-Path $Destination 'vbaudio_cable64_win10.cat'
    $setupSignature = Get-AuthenticodeSignature $installer
    $catalogSignature = Get-AuthenticodeSignature $catalog
    $signatures = [ordered]@{
        sha256 = $hash
        setupStatus = [string]$setupSignature.Status
        setupPublisher = $setupSignature.SignerCertificate.Subject
        catalogStatus = [string]$catalogSignature.Status
        catalogPublisher = $catalogSignature.SignerCertificate.Subject
    }
    if ($setupSignature.Status -ne 'Valid' -or $setupSignature.SignerCertificate.Subject -notmatch 'BUREL VINCENT' -or
        $catalogSignature.Status -ne 'Valid' -or $catalogSignature.SignerCertificate.Subject -notmatch 'Microsoft Windows Hardware Compatibility Publisher') {
        throw 'Pinned virtual-audio signatures invalid'
    }
    $process = Start-Process -FilePath $installer -ArgumentList '-i', '-h' -WorkingDirectory $Destination -PassThru
    if (-not $process.WaitForExit(90000)) {
        $process.Kill()  # No Kill(bool) overload in Windows PowerShell 5.1.
        throw 'Owned audio installation exceeded 90 seconds'
    }
    $signatures['installerExit'] = $process.ExitCode
    return $signatures
}

# EC2 get-console-output reads the serial console (COM1). Best effort: a busy or
# absent port is recorded by the caller and never treated as success.
function Write-BlastConsole([string[]]$Lines) {
    try {
        $port = New-Object System.IO.Ports.SerialPort 'COM1', 115200, 'None', 8, 'One'
        $port.WriteTimeout = 5000
        $port.Open()
        try { foreach ($line in $Lines) { $port.WriteLine($line) } } finally { $port.Close() }
        return $true
    } catch {
        Write-Warning ('Serial console unavailable: ' + $_)
        return $false
    }
}

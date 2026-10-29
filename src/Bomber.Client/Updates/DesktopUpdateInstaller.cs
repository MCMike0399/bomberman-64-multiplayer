using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Bomber.Client.Updates;

/// <summary>Authenticated Windows directory and Linux AppImage staging with an independent helper.</summary>
public static class DesktopUpdateInstaller
{
    public static void CleanupCompleted(string target, string currentVersion)
    {
        string parent = Path.GetDirectoryName(Path.GetFullPath(target))!;
        if (File.Exists(Path.Combine(parent, ".blast-update-lock"))) return;
        foreach (string stage in Directory.EnumerateDirectories(parent, ".blast-update-*"))
        {
            if (!Guid.TryParseExact(Path.GetFileName(stage)[14..], "N", out _) || new DirectoryInfo(stage).LinkTarget is not null) continue;
            try
            {
                string path = Path.Combine(stage, "request.json");
                if (!File.Exists(path)) continue;
                var request = JsonSerializer.Deserialize<DesktopUpdateRequest>(File.ReadAllText(path));
                if (request is null || request.Target != target || request.Version != currentVersion ||
                    !File.Exists(Path.Combine(stage, "healthy")) ||
                    File.ReadAllText(Path.Combine(stage, "healthy")).Trim() != currentVersion) continue;
                // A Windows helper can still have its own file open briefly after
                // releasing the transaction lock; leave that one for the next launch.
                Directory.Delete(stage, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
    }

    public static string? InstalledTarget(string executable)
    {
        if (OperatingSystem.IsWindows() && Path.GetFileName(executable) is "BlastCircuit.exe" or "BlastCircuit.Player.exe")
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(executable))!;
            return File.Exists(Path.Combine(directory, "release.json")) ? directory : null;
        }
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image)
            return File.Exists(image) ? Path.GetFullPath(image) : null;
        return null;
    }

    public static void CheckWriteAccess(string target,bool appImage)
    {
        string parent=Path.GetDirectoryName(Path.GetFullPath(target))!;
        string probe=Path.Combine(parent,".blast-write-probe-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var file=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose);
            file.WriteByte(0);file.Flush(true);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException)
        {
            throw new IOException(appImage?"This AppImage cannot update in this folder. Move it into a folder you own, then try again.":"This installation cannot be updated here. Check the folder's write permissions or reinstall the latest release.",error);
        }
    }

    public static async Task<PreparedUpdate> Prepare(UpdateService service, UpdateRelease release, string target,
        string currentVersion, string installedExecutable, string historyDirectory, CancellationToken ct)
    {
        if (UpdateRelease.ParseVersion(release.Version) <= UpdateRelease.ParseVersion(currentVersion))
            throw new InvalidDataException("Update must be newer than the installed game.");
        target = Path.GetFullPath(target);
        bool image = OperatingSystem.IsLinux();
        if ((!image && !OperatingSystem.IsWindows()) ||
            (image ? new FileInfo(target).LinkTarget : new DirectoryInfo(target).LinkTarget) is not null)
            throw new InvalidOperationException("Update requires a writable installed Windows game or Linux AppImage.");
        CheckWriteAccess(target,image);
        string token = Guid.NewGuid().ToString("N");
        string stage = Path.Combine(Path.GetDirectoryName(target)!, ".blast-update-" + token);
        Directory.CreateDirectory(stage);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string download = Path.Combine(stage, image ? "next.AppImage" : "update.zip");
            await service.Download(release, download, ct);
            string payload;
            if (image)
            {
                File.SetUnixFileMode(download, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var extract = new ProcessStartInfo(download) { UseShellExecute = false, WorkingDirectory = stage,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                extract.ArgumentList.Add("--appimage-extract");
                using var process = Process.Start(extract) ?? throw new IOException("Could not inspect downloaded AppImage.");
                try
                {
                    var output = process.StandardOutput.ReadToEndAsync(ct);
                    var error = process.StandardError.ReadToEndAsync(ct);
                    await process.WaitForExitAsync(ct); await output; await error;
                    if (process.ExitCode != 0) throw new InvalidDataException("Downloaded AppImage could not be extracted.");
                }
                catch { if (!process.HasExited) process.Kill(true); throw; }
                payload = Path.Combine(stage, "squashfs-root", "usr", "lib", "blast-circuit");
            }
            else
            {
                ValidateWindowsArchive(download);
                ZipFile.ExtractToDirectory(download, Path.Combine(stage, "extracted"));
                payload = Path.Combine(stage, "extracted", "Blast-Circuit-windows-x64");
            }
            using var identity = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(payload, "release.json"), ct));
            if (identity.RootElement.GetProperty("release").GetProperty("version").GetString() != release.Version ||
                identity.RootElement.GetProperty("target").GetString() != (image ? "linux-x64" : "windows-x64"))
                throw new InvalidDataException("Downloaded game identity differs from its signed update.");
            if (!image) Directory.Move(payload, Path.Combine(stage, "next"));
            // Copy the currently installed, trusted helper out of the directory/mount being replaced.
            string helperName = image ? "BlastUpdater" : "BlastUpdater.exe";
            File.Copy(Path.Combine(Path.GetDirectoryName(installedExecutable)!, "updater", helperName), Path.Combine(stage, helperName));
            if (image) File.SetUnixFileMode(Path.Combine(stage, helperName), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.CreateDirectory(historyDirectory);
            return new(target, stage, release.Version, token, Path.Combine(historyDirectory, "last-update.txt"));
        }
        catch { Directory.Delete(stage, true); throw; }
    }

    public static void ValidateWindowsArchive(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        long size = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            string path = entry.FullName;
            if (!path.StartsWith("Blast-Circuit-windows-x64/", StringComparison.Ordinal) || path.Contains('\\') || path.Contains(':') ||
                path.Split('/').Any(p => p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')) || !seen.Add(path) ||
                ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException("Unsafe or duplicate Windows update archive path.");
            size += entry.Length;
            if (zip.Entries.Count > 10000 || size > 1024L * 1024 * 1024)
                throw new InvalidDataException("Windows update archive expands beyond its limit.");
        }
        foreach (string name in new[] { "BlastCircuit.exe", "BlastCircuit.pck", "release.json", "updater/BlastUpdater.exe" })
            if (zip.GetEntry("Blast-Circuit-windows-x64/" + name) is null)
                throw new InvalidDataException("Windows update archive is incomplete.");
    }

    public static async Task Schedule(PreparedUpdate update, int currentPid, CancellationToken ct, bool windowed = false)
    {
        ct.ThrowIfCancellationRequested();
        using var parent = Process.GetProcessById(currentPid);
        var request = new DesktopUpdateRequest(update.Target, update.Stage, update.Version, update.Token,
            update.History, currentPid, parent.StartTime.ToUniversalTime().Ticks, windowed, OperatingSystem.IsLinux(),
            DesktopProcessIdentity.StartToken(parent));
        string path = Path.Combine(update.Stage, "request.json");
        File.WriteAllText(path, JsonSerializer.Serialize(request));
        string helper = Path.Combine(update.Stage, OperatingSystem.IsWindows() ? "BlastUpdater.exe" : "BlastUpdater");
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = update.Stage };
        start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new IOException("Could not start update helper.");
        DesktopProcessIdentity.WriteMarker(Path.Combine(update.Stage, "helperpid"), process);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string ready = Path.Combine(update.Stage, "ready");
        string? token;
        while ((token = DesktopUpdateTransaction.ReadyToken(ready, update.Token)) is null)
        {
            if (process.HasExited) throw new IOException("Update helper could not acquire the installation lock.");
            await Task.Delay(50, timeout.Token);
        }
        if (token != update.Token)
            throw new InvalidDataException("Update helper readiness does not match this transaction.");
    }
}

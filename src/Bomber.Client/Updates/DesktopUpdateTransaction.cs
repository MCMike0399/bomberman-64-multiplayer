using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bomber.Client.Updates;

/// <summary>Replace only after the old process exits; restore the previous payload if health never arrives.</summary>
public static class DesktopUpdateTransaction
{
    public static async Task Run(string requestPath, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<DesktopUpdateRequest>(await File.ReadAllTextAsync(requestPath, ct))
            ?? throw new InvalidDataException("Missing update request.");
        string target = Path.GetFullPath(request.Target), stage = Path.GetFullPath(request.Stage);
        string parent = Path.GetDirectoryName(target)!;
        if (string.IsNullOrEmpty(request.ParentIdentity) || !Guid.TryParseExact(request.Token, "N", out _) ||
            stage != Path.Combine(parent, ".blast-update-" + request.Token) ||
            Path.GetFullPath(requestPath) != Path.Combine(stage, "request.json") ||
            new DirectoryInfo(stage).LinkTarget is not null || new DirectoryInfo(parent).LinkTarget is not null)
            throw new InvalidDataException("Invalid update transaction paths.");
        string next = Path.Combine(stage, request.AppImage ? "next.AppImage" : "next");
        string previous = Path.Combine(stage, request.AppImage ? "previous.AppImage" : "previous");
        string lockPath = Path.Combine(parent, ".blast-update-lock");
        // DeleteOnClose releases the lock after a normal exit or a helper crash.
        using var transaction = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        if (!(request.AppImage ? File.Exists(next) : Directory.Exists(next)))
            throw new InvalidDataException("Staged replacement is missing.");
        // Publish readiness atomically; the waiting game polls for this file.
        string readyTemporary = Path.Combine(stage, ".ready.tmp");
        await File.WriteAllTextAsync(readyTemporary, request.Token, ct);
        File.Move(readyTemporary, Path.Combine(stage, "ready"), overwrite: true);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var old = Process.GetProcessById(request.ParentPid);
            if (DesktopProcessIdentity.Matches(old, request.ParentIdentity))
                await old.WaitForExitAsync(deadline.Token);
        }
        catch (ArgumentException) { /* The original game has already exited. */ }
        bool replaced = false;
        string failureKind = "install";
        Process? launched = null;
        try
        {
            if (!request.AppImage)
            {
                // Inno's uninstall payload belongs to this installation. Portable
                // update ZIPs intentionally do not contain machine-owned uninstall data.
                foreach (string file in Directory.EnumerateFiles(target, "unins*"))
                {
                    if (!Regex.IsMatch(Path.GetFileName(file), @"^unins\d{3}\.(exe|dat|msg)$", RegexOptions.CultureInvariant)) continue;
                    if (new FileInfo(file).LinkTarget is not null) throw new IOException("Uninstall payload must not be a symbolic link.");
                    File.Copy(file, Path.Combine(next, Path.GetFileName(file)), overwrite: true);
                }
            }
            await Move(target, previous, request.AppImage, ct);
            replaced = true;
            await Move(next, target, request.AppImage, ct);
            failureKind = "launch";
            launched = Launch(target, request.AppImage, request.Windowed, request.Token);
            failureKind = "health";
            string healthy = Path.Combine(stage, "healthy");
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(180))
            {
                ct.ThrowIfCancellationRequested();
                if (launched.HasExited) throw new IOException("Updated game exited before reporting health.");
                if (File.Exists(healthy) && (await File.ReadAllTextAsync(healthy, ct)).Trim() == request.Version)
                {
                    failureKind = "finalize";
                    WriteHistory(request.History, "installed " + request.Version);
                    if (OperatingSystem.IsWindows())
                    {
                        using var registration = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\io.github.mcmike0399.blastcircuit_is1", writable: true);
                        if (string.Equals((registration?.GetValue("InstallLocation") as string)?.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                            registration!.SetValue("DisplayVersion", request.Version);
                    }
                    // Cleanup must never turn a completed transaction into a rollback.
                    try { if (request.AppImage) File.Delete(previous); else Directory.Delete(previous, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    return;
                }
                await Task.Delay(100, ct);
            }
            throw new IOException("Updated game did not report health within 180 seconds.");
        }
        catch
        {
            if (launched is not null && !launched.HasExited)
            {
                try { launched.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* Candidate exited between observation and kill. */ }
                catch (System.ComponentModel.Win32Exception) when (launched.HasExited) { }
                await launched.WaitForExitAsync(CancellationToken.None);
            }
            // Release the terminated image's process handle before renaming its
            // installation. Windows may retain image resources while it is open.
            launched?.Dispose(); launched = null;
            string restoreTarget=target;
            if (replaced)
            {
                try
                {
                    if (request.AppImage ? File.Exists(target) : Directory.Exists(target))
                        await Move(target, next, request.AppImage, CancellationToken.None);
                    await Move(previous, target, request.AppImage, CancellationToken.None);
                }
                catch(Exception recoveryError)when(recoveryError is IOException or UnauthorizedAccessException)
                {
                    if(!(request.AppImage?File.Exists(previous):Directory.Exists(previous)))throw;
                    // Keep the preserved old payload playable even if an AV lock
                    // prevents restoring its directory. Never delete unrelated files.
                    restoreTarget=previous;Console.Error.WriteLine("BLAST_UPDATE_RECOVERY_RUNNING_PREVIOUS");
                }
            }
            // The player already exited after readiness. Even a first rename
            // failure must reopen the still-installed previous game.
            using var restored = Launch(restoreTarget, request.AppImage, request.Windowed, null,restoreTarget!=target);
            DesktopProcessIdentity.WriteMarker(Path.Combine(stage, "restoredpid"), restored);
            bool quarantine=failureKind is "launch" or "health";
            WriteHistory(request.History, quarantine?"failed "+request.Version+" launch-rollback "+failureKind:"retry "+request.Version+" install-rollback");
            throw;
        }
        finally { launched?.Dispose(); }
    }

    private static Process Launch(string target, bool appImage, bool windowed, string? token,bool recovery=false)
    {
        var start = new ProcessStartInfo(appImage ? target : Path.Combine(target, "BlastCircuit.exe"))
        { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
        // Do not inherit the previous AppImage's temporary mount environment.
        foreach (string name in new[] { "APPIMAGE", "APPDIR", "OWD", "ARGV0" }) start.Environment.Remove(name);
        if (windowed) start.ArgumentList.Add("--windowed");
        start.ArgumentList.Add("--");
        if (windowed) start.ArgumentList.Add("--display-windowed");
        if (token is not null) start.ArgumentList.Add("--update-token=" + token);
        if(recovery)start.ArgumentList.Add("--update-recovery");
        return Process.Start(start) ?? throw new IOException("Could not launch installed game.");
    }

    private static async Task Move(string from, string to, bool file, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (file) File.Move(from, to); else Directory.Move(from, to);
                return;
            }
            catch (Exception error) when (OperatingSystem.IsWindows() &&
                error is IOException or UnauthorizedAccessException &&
                (error.HResult & 0xffff) is 5 or 32 or 33 && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                // Windows may retain mapped files briefly after process exit.
                // Actual installed-game handoff also reports access denied after
                // process exit. Persistent ACL denials still fail at the bound;
                // no permissions are changed, and other I/O errors fail immediately.
                await Task.Delay(100, ct);
            }
        }
    }
    private static void WriteHistory(string path, string result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", result + "\n");
        File.Move(path + ".tmp", path, overwrite: true);
    }

    /// <summary>The helper's complete readiness token, or null while it is not ready.</summary>
    /// <remarks>Helpers from older releases create the marker before writing it. A
    /// missing, unreadable, empty or partial token is therefore not ready yet;
    /// any other complete token is returned so the caller can reject it.</remarks>
    public static string? ReadyToken(string path, string expected)
    {
        string text;
        try { text = File.ReadAllText(path).Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        return text.Length == 0 || text != expected && expected.StartsWith(text, StringComparison.Ordinal) ? null : text;
    }
}

using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Bomber.Client.Updates;

if (args.Contains("--fixture"))
{
    MarkStarted();
    string? token = args.FirstOrDefault(a => a.StartsWith("--update-token="))?.Split('=', 2)[1];
    if (token is not null)
    {
        string root = Path.GetDirectoryName(Path.GetDirectoryName(Environment.ProcessPath!))!;
        if (File.Exists(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "fail-launch"))) return 23;
        File.WriteAllText(Path.Combine(root, ".blast-update-" + token, "healthy"), "99.0.0");
    }
    await Task.Delay(token is null ? 1500 : 10000);
    return 0;
}
// A renamed apphost still loads its original DLL beside it. Its child mode is
// deliberately independent of Godot; archived-game transactions are a separate OS gate.
if (args.Contains("--") || args.Any(a => a.StartsWith("--update-token=")))
{
    MarkStarted();
    if (File.Exists(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "fail-launch")))
    {
        if (OperatingSystem.IsWindows())
        {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(Environment.ProcessPath!))!;
            File.WriteAllText(Path.Combine(root, "rollback-ready"), "ready");
            using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(Path.Combine(root, "rollback-locked"))) await Task.Delay(10, ready.Token);
        }
        return 23;
    }
    string? token = args.FirstOrDefault(a => a.StartsWith("--update-token="))?.Split('=', 2)[1];
    if (token is not null)
    {
        string root = Path.GetDirectoryName(Path.GetDirectoryName(Environment.ProcessPath!))!;
        File.WriteAllText(Path.Combine(root, ".blast-update-" + token, "healthy"), "99.0.0");
    }
    await Task.Delay(10000);
    return 0;
}
int checks = 0;
void Check(bool value, string description)
{ if (!value) throw new Exception(description); checks++; }
using (var self = Process.GetCurrentProcess())
{
    string token = DesktopProcessIdentity.StartToken(self);
    if (OperatingSystem.IsLinux())
        Check(token.StartsWith("linux:" + File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim() + ":") &&
            ulong.TryParse(token.Split(':')[2], out ulong counter) && counter > 0,
            "Linux identity contains the current boot and a positive kernel birth counter");
    Check(DesktopProcessIdentity.Matches(self, token), "Independent process queries agree on the exact birth identity");
    Check(!DesktopProcessIdentity.Matches(self, token + "-different"), "A different process birth identity never matches a live PID");
    string marker = Path.GetTempFileName();
    try
    {
        DesktopProcessIdentity.WriteMarker(marker, self);
        using var observed = DesktopProcessIdentity.OpenMarker(marker);
        Check(observed.Id == self.Id, "Atomic marker opens the exact live process");
        File.WriteAllText(marker, self.Id + " " + token + "-different");
        try { using var wrong = DesktopProcessIdentity.OpenMarker(marker); Check(false, "Wrong birth identity must fail"); }
        catch (IOException) { checks++; }
    }
    finally { File.Delete(marker); }
}
if (OperatingSystem.IsWindows())
{
    string probe = Path.Combine(Path.GetTempPath(), "blast-lock-probe-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(probe);
    try
    {
        using var handle = DirectoryHandle.Open(probe);
        bool blocked = false;
        try { Directory.Move(probe, probe + "-moved"); }
        catch (IOException error) when ((error.HResult & 0xffff) == 32) { blocked = true; }
        Check(blocked, "The Windows sharing probe must actually block a directory rename");
    }
    finally
    {
        if (Directory.Exists(probe)) Directory.Delete(probe);
        if (Directory.Exists(probe + "-moved")) Directory.Delete(probe + "-moved");
    }
}
foreach (bool failedLaunch in new[] { false, true })
{
    string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "blast-update-transaction-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    string token = Guid.NewGuid().ToString("N"), target = Path.Combine(root, "Installed game with spaces");
    string stage = Path.Combine(root, ".blast-update-" + token), next = Path.Combine(stage, "next");
    CopyFixture(target); CopyFixture(next);
    File.WriteAllText(Path.Combine(target, "old-marker"), "old");
    File.WriteAllText(Path.Combine(target, "unins000.dat"), "installed uninstall data");
    if (failedLaunch) File.WriteAllText(Path.Combine(next, "fail-launch"), "fail");
    var children = new List<Process>();
    Task? sharing = null;
    try
    {
        var start = new ProcessStartInfo(Path.Combine(target, "BlastCircuit.exe")) { UseShellExecute = false };
        start.ArgumentList.Add("--fixture");
        using var old = Process.Start(start)!;
        children.Add(old);
        var request = new DesktopUpdateRequest(target, stage, "99.0.0", token, Path.Combine(root, "history/result.txt"),
            old.Id, old.StartTime.ToUniversalTime().Ticks, true, false, DesktopProcessIdentity.StartToken(old));
        string path = Path.Combine(stage, "request.json"); File.WriteAllText(path, JsonSerializer.Serialize(request));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        if (OperatingSystem.IsWindows())
        {
            // Hold a real non-delete-sharing directory handle after process exit.
            // The same boundary occurs when Windows still holds mapped game files.
            var handle = DirectoryHandle.Open(target);
            sharing = Task.Run(async () =>
            {
                using (handle)
                {
                    await old.WaitForExitAsync(timeout.Token);
                    await Task.Delay(500, timeout.Token);
                }
                if (!failedLaunch) return;
                while (!File.Exists(Path.Combine(root, "rollback-ready"))) await Task.Delay(10, timeout.Token);
                using var rollbackHandle = DirectoryHandle.Open(target);
                File.WriteAllText(Path.Combine(root, "rollback-locked"), "locked");
                await Task.Delay(500, timeout.Token);
            });
        }
        var transaction = DesktopUpdateTransaction.Run(path, timeout.Token);
        while (!File.Exists(Path.Combine(stage, "ready"))) await Task.Delay(20, timeout.Token);
        Check(File.Exists(Path.Combine(target, "old-marker")), "A running game must remain installed during handoff");
        try { await transaction; Check(!failedLaunch, "Failed launch must report failure"); }
        catch (IOException) when (failedLaunch) { checks++; }
        if (sharing is not null) await sharing;
        Check(File.Exists(Path.Combine(target, "old-marker")) == failedLaunch, "Installed bytes must match success or rollback");
        Check(File.ReadAllText(Path.Combine(target, "unins000.dat")) == "installed uninstall data", "Installed uninstall data must survive update and rollback");
        Check(File.ReadAllText(request.History).Trim() == (failedLaunch ? "failed 99.0.0 launch-rollback" : "installed 99.0.0"), "History must report actual outcome");
        if (failedLaunch)
        {
            using var restored = DesktopProcessIdentity.OpenMarker(Path.Combine(stage, "restoredpid"));
            Check(!restored.HasExited, "Rollback marker identifies the exact live restored process");
        }
        while (Directory.GetFiles(root, "fixture-pid-*").Length < (failedLaunch ? 3 : 2))
            await Task.Delay(20, timeout.Token);
        Check(true, "Replacement or restored executable actually started before cleanup");
        Check(!File.Exists(Path.Combine(root, ".blast-update-lock")), "Completed helper must release its lock");
        Check(failedLaunch || !Directory.Exists(Path.Combine(stage, "previous")), "Successful update must remove previous payload");
        Console.WriteLine("PASS transaction " + (failedLaunch ? "rollback" : "replacement") + ": complete inventory, uninstall data, history, relaunch and lock release");
    }
    catch (Exception error)
    {
        Console.Error.WriteLine("TRANSACTION_FAILURE " + error);
        throw;
    }
    finally
    {
        if (sharing is not null) await sharing;
        foreach (string marker in Directory.GetFiles(root, "fixture-pid-*"))
        {
            try
            {
                using var process = DesktopProcessIdentity.OpenMarker(marker);
                if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            }
            catch (ArgumentException) { }
            catch (IOException) { }
        }
        // A terminated Windows image may still be mapped briefly after WaitForExit.
        // Require actual removal, but allow the owned fixture's image to unload.
        var cleanup = Stopwatch.StartNew();
        while (Directory.Exists(root))
        {
            try { Directory.Delete(root, true); }
            catch (Exception error) when (OperatingSystem.IsWindows() &&
                error is IOException or UnauthorizedAccessException &&
                (error.HResult & 0xffff) is 5 or 32 or 33 && cleanup.Elapsed < TimeSpan.FromSeconds(5))
            { await Task.Delay(100); }
        }
    }
}
Console.WriteLine($"PASS {checks} desktop updater handoff, replacement and rollback checks");
return 0;

void CopyFixture(string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory))
        File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    File.Copy(Environment.ProcessPath!, Path.Combine(destination, "BlastCircuit.exe"));
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(destination, "BlastCircuit.exe"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}

void MarkStarted()
{
    string root = Path.GetDirectoryName(Path.GetDirectoryName(Environment.ProcessPath!))!;
    using var process = Process.GetCurrentProcess();
    DesktopProcessIdentity.WriteMarker(Path.Combine(root, "fixture-pid-" + Environment.ProcessId), process);
}

static class DirectoryHandle
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    public static SafeFileHandle Open(string path)
    {
        // FILE_SHARE_READ|WRITE, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS.
        var handle = CreateFileW(path, 0x80000000, 3, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return handle;
    }
}

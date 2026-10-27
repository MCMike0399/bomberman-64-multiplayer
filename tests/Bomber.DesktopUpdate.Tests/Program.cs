using System.Diagnostics;
using System.Text.Json;
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
    if (File.Exists(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "fail-launch"))) return 23;
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
foreach (bool failedLaunch in new[] { false, true })
{
    string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "blast-update-transaction-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    string token = Guid.NewGuid().ToString("N"), target = Path.Combine(root, "Installed game with spaces");
    string stage = Path.Combine(root, ".blast-update-" + token), next = Path.Combine(stage, "next");
    CopyFixture(target); CopyFixture(next);
    File.WriteAllText(Path.Combine(target, "old-marker"), "old");
    if (failedLaunch) File.WriteAllText(Path.Combine(next, "fail-launch"), "fail");
    var children = new List<Process>();
    try
    {
        var start = new ProcessStartInfo(Path.Combine(target, "BlastCircuit.exe")) { UseShellExecute = false };
        start.ArgumentList.Add("--fixture");
        using var old = Process.Start(start)!;
        children.Add(old);
        var request = new DesktopUpdateRequest(target, stage, "99.0.0", token, Path.Combine(root, "history/result.txt"),
            old.Id, old.StartTime.ToUniversalTime().Ticks, true, false);
        string path = Path.Combine(stage, "request.json"); File.WriteAllText(path, JsonSerializer.Serialize(request));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var transaction = DesktopUpdateTransaction.Run(path, timeout.Token);
        while (!File.Exists(Path.Combine(stage, "ready"))) await Task.Delay(20, timeout.Token);
        Check(File.Exists(Path.Combine(target, "old-marker")), "A running game must remain installed during handoff");
        try { await transaction; Check(!failedLaunch, "Failed launch must report failure"); }
        catch (IOException) when (failedLaunch) { checks++; }
        Check(File.Exists(Path.Combine(target, "old-marker")) == failedLaunch, "Installed bytes must match success or rollback");
        Check(File.ReadAllText(request.History).Trim() == (failedLaunch ? "failed 99.0.0 launch-rollback" : "installed 99.0.0"), "History must report actual outcome");
        while (Directory.GetFiles(root, "fixture-pid-*").Length < (failedLaunch ? 3 : 2))
            await Task.Delay(20, timeout.Token);
        Check(true, "Replacement or restored executable actually started before cleanup");
        Check(!File.Exists(Path.Combine(root, ".blast-update-lock")), "Completed helper must release its lock");
        Check(failedLaunch || !Directory.Exists(Path.Combine(stage, "previous")), "Successful update must remove previous payload");
    }
    finally
    {
        foreach (string marker in Directory.GetFiles(root, "fixture-pid-*"))
        {
            try
            {
                using var process = Process.GetProcessById(int.Parse(Path.GetFileName(marker)[12..]));
                if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            }
            catch (ArgumentException) { }
        }
        Directory.Delete(root, true);
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
    File.WriteAllText(Path.Combine(root, "fixture-pid-" + Environment.ProcessId), "started");
}

using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Bomber.Client.Updates;

public sealed record PreparedUpdate(string Target, string Stage, string Version, string Token, string History);

public static class MacUpdateInstaller
{
    public const string BundleId = "io.github.mcmike0399.blastcircuit";
    public static string? BundleForExecutable(string executable)
    {
        var path = new FileInfo(executable).Directory?.Parent?.Parent;
        return path?.Extension == ".app" ? path.FullName : null;
    }
    public static async Task<string> PlistValue(string app, string key, CancellationToken ct = default) =>
        (await Tool("/usr/libexec/PlistBuddy", ["-c", "Print :" + key, Path.Combine(app, "Contents/Info.plist")], ct)).Trim();

    public static async Task<PreparedUpdate> Prepare(UpdateService service, UpdateRelease release, string target, string historyDirectory, CancellationToken ct)
    {
        target = Path.GetFullPath(target);
        if (!OperatingSystem.IsMacOS() || !target.EndsWith(".app", StringComparison.Ordinal) || target.Contains("/AppTranslocation/") || new DirectoryInfo(target).LinkTarget is not null)
            throw new InvalidOperationException("Move Blast Circuit into your Applications folder before updating.");
        if (await PlistValue(target, "CFBundleIdentifier", ct) != BundleId) throw new InvalidDataException("Installed app identity is incorrect.");
        var current = await PlistValue(target, "CFBundleShortVersionString", ct);
        if (UpdateRelease.ParseVersion(release.Version) <= UpdateRelease.ParseVersion(current)) throw new InvalidDataException("Update must be newer than the installed app.");
        string token = Guid.NewGuid().ToString("N");
        string stage = Path.Combine(Path.GetDirectoryName(target)!, ".blast-update-" + token);
        Directory.CreateDirectory(stage);File.SetUnixFileMode(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string archive = Path.Combine(stage, "update.zip");await service.Download(release, archive, ct);
            ValidateArchive(archive);
            string extracted = Path.Combine(stage, "extracted");Directory.CreateDirectory(extracted);
            await Tool("/usr/bin/ditto", ["-x", "-k", archive, extracted], ct);
            string app = Path.Combine(extracted, "Blast Circuit.app");
            if (await PlistValue(app, "CFBundleIdentifier", ct) != BundleId || await PlistValue(app, "CFBundleShortVersionString", ct) != release.Version || await PlistValue(app, "CFBundleExecutable", ct) != "Blast Circuit")
                throw new InvalidDataException("Downloaded app identity or version does not match the signed release.");
            await Tool("/usr/bin/codesign", ["--verify", "--deep", "--strict", app], ct);
            using var resource = typeof(MacUpdateInstaller).Assembly.GetManifestResourceStream("BlastCircuit.UpdateInstaller")!;
            using var reader = new StreamReader(resource);
            await File.WriteAllTextAsync(Path.Combine(stage, "install.sh"), await reader.ReadToEndAsync(ct), ct);
            Directory.CreateDirectory(historyDirectory);
            ct.ThrowIfCancellationRequested();
            return new(target, stage, release.Version, token, Path.Combine(historyDirectory, "last-update.txt"));
        }
        catch { Directory.Delete(stage, true);throw; }
    }
    public static void ValidateArchive(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);long total = 0;var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (zip.Entries.Count > 10000) throw new InvalidDataException("Too many update archive entries.");
        foreach (var entry in zip.Entries)
        {
            string path = entry.FullName;
            if ((!path.StartsWith("Blast Circuit.app/", StringComparison.Ordinal) && !path.StartsWith("__MACOSX/", StringComparison.Ordinal)) || path.Contains('\\') || path.Split('/').Any(part => part is ".." or ".") || !paths.Add(path))
                throw new InvalidDataException("Unsafe or duplicate update archive path.");
            if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidDataException("Update archive contains a symbolic link.");
            total += entry.Length;if (total > 1024L * 1024 * 1024) throw new InvalidDataException("Update archive expands beyond its limit.");
        }
        if (zip.GetEntry("Blast Circuit.app/Contents/Info.plist") is null) throw new InvalidDataException("Update archive has no app bundle.");
    }
    public static Task Schedule(PreparedUpdate update, int currentPid, CancellationToken ct, bool windowed = false)
    {
        ct.ThrowIfCancellationRequested();
        // An independent helper outlives a normal game quit; Launch Services owns the new app.
        var start = new ProcessStartInfo("/usr/bin/nohup") { UseShellExecute = false, CreateNoWindow = true };
        foreach(var argument in new[]{"/bin/bash",Path.Combine(update.Stage,"install.sh"),update.Target,update.Stage,update.Version,update.Token,currentPid.ToString(),update.History,windowed?"windowed":"normal"})
            start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("Could not start update installer.");
        return Task.CompletedTask;
    }
    private static async Task<string> Tool(string file, string[] arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start update helper.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(ct);var error = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);var text = await output;var details = await error;
            if (process.ExitCode != 0) throw new IOException(Path.GetFileName(file) + " failed: " + details.Trim());
            return text;
        }
        catch { if (!process.HasExited) process.Kill(true);throw; }
    }
}

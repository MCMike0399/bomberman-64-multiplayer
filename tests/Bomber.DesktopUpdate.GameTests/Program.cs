using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Bomber.Client.Updates;

if (args.Length is < 3 or > 4 || !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) ||
    args.Length == 4 && args[3] != "--disposable-desktop")
    throw new ArgumentException("Run on Windows/Linux: <candidate assets> <newer fixture assets> <fresh output directory> [--disposable-desktop]");
if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" && !args.Contains("--disposable-desktop"))
    throw new InvalidOperationException("Actual installer/update tests require a disposable VM or user profile; the Windows installer changes its AppId registration. Use --disposable-desktop only there.");
string assets = Path.GetFullPath(args[0]), fixture = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) throw new IOException("Use a fresh updater evidence directory.");
Directory.CreateDirectory(output);
bool image = OperatingSystem.IsLinux();
string platform = image ? "linux-x64" : "windows-x64", stem = "Blast-Circuit-" + platform;
using var oldMetadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, stem + "-metadata.json")));
using var newMetadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, stem + "-metadata.json")));
string oldVersion = oldMetadata.RootElement.GetProperty("release").GetProperty("version").GetString()!;
string version = newMetadata.RootElement.GetProperty("release").GetProperty("version").GetString()!;
if (UpdateRelease.ParseVersion(version) <= UpdateRelease.ParseVersion(oldVersion)) throw new InvalidDataException("Fixture is not newer.");
byte[] archive = await File.ReadAllBytesAsync(Path.Combine(fixture, UpdateRelease.ArchiveName));
var cases = new List<object>();
foreach (var testCase in new[] { (Name:"successful-install",Interrupt:false,High:false),
    (Name:"failed-launch-rollback",Interrupt:true,High:false), (Name:"saved-high-fullscreen",Interrupt:false,High:true) })
{
    bool interrupt=testCase.Interrupt;
    JsonElement? healthReport = null;
    string folder = Path.Combine(output,testCase.Name);
    Directory.CreateDirectory(folder);
    // Keep the installed runtime below classic Windows path limits. Evidence
    // remains in the requested directory; a long CI artifact path is not a user install path.
    string runtimeFolder=Path.Combine(Path.GetTempPath(),"bcu-"+Guid.NewGuid().ToString("N")[..10]);
    Directory.CreateDirectory(runtimeFolder);
    string target, executable;
    if (image)
    {
        target = Path.Combine(runtimeFolder, "Game with spaces.AppImage");
        File.Copy(Path.Combine(assets, stem + ".AppImage"), target);
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await Tool(target, runtimeFolder, "--appimage-extract");
        executable = Path.Combine(runtimeFolder, "squashfs-root/usr/lib/blast-circuit/BlastCircuit.x86_64");
    }
    else
    {
        target = Path.Combine(runtimeFolder, "Game with spaces");
        await Tool(Path.Combine(assets, stem + "-Setup.exe"), folder, "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CURRENTUSER", "/DIR=" + target,"/LOG="+Path.Combine(folder,"setup.log"));
        executable = Path.Combine(target, "BlastCircuit.exe");
    }
    string history = Path.Combine(folder, "History");
    var initialInventory = Inventory(target, image);
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, version, sha256 = Convert.ToHexStringLower(SHA256.HashData(archive)), size = archive.LongLength });
    byte[] signature = System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)));
    string Url(string name) => $"https://github.com/{UpdateRelease.Repository}/releases/download/v{version}/{name}";
    object Asset(string name) => new { name, browser_download_url = Url(name) };
    byte[] feed = JsonSerializer.SerializeToUtf8Bytes(new { draft = false, prerelease = false, tag_name = "v" + version,
        assets = new[] { Asset(UpdateRelease.ArchiveName), Asset(UpdateRelease.ManifestName), Asset(UpdateRelease.ManifestName + ".sig") } });
    using var service = new UpdateService(new FixtureHttp(new() { [UpdateRelease.Feed.AbsoluteUri] = feed, [Url(UpdateRelease.ArchiveName)] = archive,
        [Url(UpdateRelease.ManifestName)] = manifest, [Url(UpdateRelease.ManifestName + ".sig")] = signature }), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(300));
    var release = await service.Check(oldVersion, timeout.Token) ?? throw new Exception("No authenticated fixture release.");
    var prepared = await DesktopUpdateInstaller.Prepare(service, release, target, oldVersion, executable, history, timeout.Token);
    string preferences = Path.Combine(runtimeFolder, "profile"); Directory.CreateDirectory(preferences);
    Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(preferences, "data"));
    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(preferences, "config"));
    Environment.SetEnvironmentVariable("APPDATA", Path.Combine(preferences, "AppData"));
    if (image) Environment.SetEnvironmentVariable("APPIMAGE_EXTRACT_AND_RUN", null);
    string userData=Path.Combine(image?Environment.GetEnvironmentVariable("XDG_DATA_HOME")!:Environment.GetEnvironmentVariable("APPDATA")!,
        image?"godot":"Godot","app_userdata","Blast Circuit");
    Directory.CreateDirectory(userData);
    File.WriteAllText(Path.Combine(userData,"preferences.cfg"),"[player]\nautomaticUpdates=false\n[video]\ngraphicsPreset=\""+
        (testCase.High?"high":"medium")+"\"\nfullscreen="+(testCase.High?"true":"false")+"\nresolution=Vector2i(0, 0)\n");
    var start = new ProcessStartInfo(image ? target : executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in new[] { "--headless", "--", "--smoke", "--smoke-seconds=6" }) start.ArgumentList.Add(argument);
    using var old = Process.Start(start)!;
    bool oldReleased = false;
    var stdout = old.StandardOutput.ReadToEndAsync(); var stderr = old.StandardError.ReadToEndAsync();
    var clock = Stopwatch.StartNew();
    try
    {
        await DesktopUpdateInstaller.Schedule(prepared, old.Id, timeout.Token, windowed:!testCase.High);
        await Task.Delay(300, timeout.Token);
        if (old.HasExited) throw new Exception("Old game exited before the handoff witness.");
        await old.WaitForExitAsync(timeout.Token);
        string oldLog = await stdout + await stderr; File.WriteAllText(Path.Combine(folder, "old-launch.log"), oldLog);
        if (old.ExitCode != 0 || !oldLog.Contains("GODOT_SMOKE_OK")) throw new Exception("Old game did not complete real gameplay.");
        old.Dispose(); oldReleased = true;
        string pidPath = Path.Combine(prepared.Stage, "newpid");
        while (!File.Exists(pidPath))
        {
            string failure = Path.Combine(prepared.Stage, "helper-error.txt");
            if (File.Exists(failure)) throw new IOException("Update helper failed before candidate startup: " + await File.ReadAllTextAsync(failure, timeout.Token));
            await Task.Delay(20, timeout.Token);
        }
        if (interrupt)
        {
            if (File.Exists(Path.Combine(prepared.Stage, "healthy"))) throw new Exception("Candidate already reported health before the deliberate failure.");
            using var candidate = DesktopProcessIdentity.OpenMarker(pidPath);
            candidate.Kill(true); await candidate.WaitForExitAsync(timeout.Token);
        }
        string receipt = Path.Combine(history, "last-update.txt");
        while (!File.Exists(receipt)) await Task.Delay(100, timeout.Token);
        string expected = interrupt ? "failed " + version + " launch-rollback" : "installed " + version;
        if ((await File.ReadAllTextAsync(receipt, timeout.Token)).Trim() != (interrupt?expected+" health":expected)) throw new Exception("Incorrect updater outcome.");
        while (File.Exists(Path.Combine(runtimeFolder, ".blast-update-lock"))) await Task.Delay(100, timeout.Token);
        if (!interrupt)
        {
            using var health = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(prepared.Stage, "health.json"), timeout.Token));
            var report = health.RootElement;
            healthReport = report.Clone();
            if (report.GetProperty("version").GetString() != version ||
                !(report.GetProperty("driver").GetString()=="vulkan"||!image&&Bomber.Client.Startup.WindowsLaunchGuard.RendererEvidenceAllowed(report.GetProperty("driver").GetString(),report.TryGetProperty("requestedDriver",out var requested)?requested.GetString():null,report.TryGetProperty("fallbackReason",out var fallback)?fallback.GetString():null)) ||
                report.GetProperty("method").GetString() != "forward_plus" || string.IsNullOrWhiteSpace(report.GetProperty("adapter").GetString()))
                throw new Exception("Updated game did not use the intended platform renderer.");
            if(testCase.High&&(report.GetProperty("graphics").GetString()!="high"||report.GetProperty("window").GetString()!="fullscreen"))
                throw new Exception("Saved High/fullscreen update did not restore both applied settings.");
            using var running = DesktopProcessIdentity.OpenMarker(pidPath);
            if (running.HasExited || (await File.ReadAllTextAsync(Path.Combine(prepared.Stage, "healthy"), timeout.Token)).Trim() != version)
                throw new Exception("Installed game did not remain healthy after helper exit.");
            running.Kill(true); await running.WaitForExitAsync(timeout.Token);
            var expectedInventory = image
                ? new SortedDictionary<string, string> { ["AppImage"] = Convert.ToHexStringLower(SHA256.HashData(archive)) }
                : new SortedDictionary<string, string>(newMetadata.RootElement.GetProperty("files").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!));
            if (!image)
                foreach (var entry in initialInventory.Where(p => Path.GetFileName(p.Key).StartsWith("unins", StringComparison.Ordinal))) expectedInventory[entry.Key] = entry.Value;
            if (!Inventory(target, image).SequenceEqual(expectedInventory)) throw new Exception("Installed payload differs from the complete future fixture inventory.");
        }
        else
        {
            if (!Inventory(target, image).SequenceEqual(initialInventory)) throw new Exception("Rollback did not restore the complete original game inventory.");
            await Task.Delay(1500, timeout.Token);
            using var restored = DesktopProcessIdentity.OpenMarker(Path.Combine(prepared.Stage, "restoredpid"));
            if (restored.HasExited) throw new Exception("Restored game did not remain running.");
            restored.Kill(true); await restored.WaitForExitAsync(timeout.Token);
            string restoredLog = await Tool(image ? target : executable, folder, "--headless", "--", "--smoke", "--smoke-seconds=2");
            File.WriteAllText(Path.Combine(folder, "restored-gameplay.log"), restoredLog);
            if (!restoredLog.Contains("GODOT_SMOKE_OK") || restoredLog.Contains("ERROR:")) throw new Exception("Restored game did not complete error-free real gameplay.");
        }
        if (!image)
        {
            var installedFiles = Inventory(target, false).Keys.Where(name => !Path.GetFileName(name).StartsWith("unins", StringComparison.Ordinal)).ToArray();
            string note = Path.Combine(target, "unrelated-user-note.txt");
            File.WriteAllText(note, "Keep this unrelated file.");
            await Tool(Path.Combine(target, "unins000.exe"), folder, "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART");
            if (installedFiles.Any(name => File.Exists(Path.Combine(target, name))) ||
                File.ReadAllText(note) != "Keep this unrelated file.")
                throw new Exception("Uninstall must remove every current product file and preserve unrelated files.");
        }
        cases.Add(new { name=testCase.Name, result = expected,
            fromVersion = oldVersion, toVersion = version, seconds = clock.Elapsed.TotalSeconds,
            oldGameplay = true, inventoryMatched = true, restoredGameplay = interrupt,
            uninstallVerified = !image, health = healthReport });
        Console.WriteLine("PASS actual " + platform + " updater: " + expected);
    }
    finally
    {
        // Stop the helper first so terminating the old game cannot release it
        // to create a new, untracked process while this fixture is unwinding.
        foreach (string marker in new[] { "helperpid", "newpid", "restoredpid" })
        {
            string path = Path.Combine(prepared.Stage, marker);
            if (!File.Exists(path)) continue;
            try
            {
                using var process = DesktopProcessIdentity.OpenMarker(path);
                if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (IOException) { /* Exited, malformed or reused identity: never kill it. */ }
        }
        if (!oldReleased && !old.HasExited) { old.Kill(true); await old.WaitForExitAsync(); }
        foreach(string name in new[]{"helper-error.txt","health.json"})
            if(File.Exists(Path.Combine(prepared.Stage,name)))File.Copy(Path.Combine(prepared.Stage,name),Path.Combine(folder,name),overwrite:true);
        if(!File.Exists(Path.Combine(runtimeFolder,".blast-update-lock")))
        {
            // Native Windows can retain a terminated helper image briefly.
            // Require real cleanup; never turn a persistent failure into a pass.
            var cleanup=Stopwatch.StartNew();
            while(Directory.Exists(runtimeFolder))
            {
                try{Directory.Delete(runtimeFolder,true);}
                catch(Exception error) when(OperatingSystem.IsWindows()&&
                    error is IOException or UnauthorizedAccessException&&
                    (error.HResult&0xffff) is 5 or 32 or 33&&cleanup.Elapsed<TimeSpan.FromSeconds(5))
                {await Task.Delay(100);}
            }
        }
    }
}
File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { schemaVersion = 1, passed = true,
    platform, candidate = new { release = oldMetadata.RootElement.GetProperty("release").Clone(), revision = oldMetadata.RootElement.GetProperty("revision").GetString() },
    fixture = new { release = newMetadata.RootElement.GetProperty("release").Clone(), revision = newMetadata.RootElement.GetProperty("revision").GetString() },
    candidateArchiveSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(assets, stem + ".zip")))), cases }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PASS actual desktop game updater: authenticated feed/download, running-app handoff, healthy relaunch and rollback");

static async Task<string> Tool(string file, string directory, params string[] arguments)
{
    var start = new ProcessStartInfo(file) { UseShellExecute = false, WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
    try
    {
        await process.WaitForExitAsync(timeout.Token);
        string text = await stdout + await stderr;
        if (process.ExitCode != 0) throw new IOException("Fixture tool failed ("+Path.GetFileName(file)+", exit "+process.ExitCode+"): " + text);
        return text;
    }
    catch { if (!process.HasExited) process.Kill(true); throw; }
}

static SortedDictionary<string, string> Inventory(string target, bool image)
{
    var result = new SortedDictionary<string, string>();
    if (image) result["AppImage"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(target)));
    else foreach (string file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        result[Path.GetRelativePath(target, file).Replace('\\', '/')] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
    return result;
}

sealed class FixtureHttp(Dictionary<string, byte[]> files) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(
        files.TryGetValue(request.RequestUri!.AbsoluteUri, out var bytes) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : new HttpResponseMessage(HttpStatusCode.NotFound));
}

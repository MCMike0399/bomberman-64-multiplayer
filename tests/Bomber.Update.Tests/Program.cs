using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Bomber.Client.Updates;

if(!OperatingSystem.IsMacOS()||args.Length is <3 or >4)throw new ArgumentException("Run on macOS: <new app> <previous app> <fresh output directory> [--high-fullscreen]");
bool highFullscreen=args.Length==4&&args[3]=="--high-fullscreen";
if(args.Length==4&&!highFullscreen)throw new ArgumentException("Unknown fixture option.");
if(highFullscreen&&!(await Tool("/usr/sbin/sysctl","-n","hw.model")).Trim().StartsWith("VirtualMac",StringComparison.Ordinal))
    throw new InvalidOperationException("The saved High/fullscreen fixture requires an isolated macOS VM; it changes guest preferences.");
string source=Path.GetFullPath(args[0]),previous=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
if(Directory.Exists(output))throw new IOException("Use a fresh output directory.");Directory.CreateDirectory(output);
string version=await MacUpdateInstaller.PlistValue(source,"CFBundleShortVersionString");
string oldVersion=await MacUpdateInstaller.PlistValue(previous,"CFBundleShortVersionString");
if(UpdateRelease.ParseVersion(version)<=UpdateRelease.ParseVersion(oldVersion))throw new Exception("Fixtures need a newer app.");
var results=new List<object>();
string goodArchive=Path.Combine(output,"good.zip"),badArchive=Path.Combine(output,"bad.zip");
await Tool("/usr/bin/ditto","-c","-k","--sequesterRsrc","--keepParent",source,goodArchive);
string badRoot=Path.Combine(output,"crash-fixture"),badApp=Path.Combine(badRoot,"Blast Circuit.app");Directory.CreateDirectory(badRoot);
try
{
    await Tool("/usr/bin/ditto",source,badApp);
    string cfile=Path.Combine(output,"exit-immediately.c");await File.WriteAllTextAsync(cfile,"int main(void) { return 23; }\n");
    await Tool("/usr/bin/clang",cfile,"-o",Path.Combine(badApp,"Contents/MacOS/Blast Circuit"));
    await Tool("/usr/bin/codesign","--force","--deep","--sign","-",badApp);
    await Tool("/usr/bin/codesign","--verify","--deep","--strict",badApp);
    await Tool("/usr/bin/ditto","-c","-k","--sequesterRsrc","--keepParent",badApp,badArchive);
}
finally{if(Directory.Exists(badRoot))Directory.Delete(badRoot,true);}
await RunCase("successful-install",goodArchive,true);
await RunCase("failed-launch-rollback",badArchive,false);
if(highFullscreen)await RunCase("successful-install-high-fullscreen",goodArchive,true,highStartup:true);
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,cases=results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("PASS actual macOS updater: signed feed/download, running-app handoff, verified new launch and failed-launch rollback");

async Task RunCase(string name,string archive,bool success,bool highStartup=false)
{
    string folder=Path.Combine(output,name),installRoot=Path.Combine(folder,"Installed"),target=Path.Combine(installRoot,"Blast Circuit.app"),history=Path.Combine(folder,"History");
    Directory.CreateDirectory(installRoot);
    await Tool("/usr/bin/ditto",previous,target);
    using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
    byte[] archiveBytes=File.ReadAllBytes(archive);
    byte[] manifest=JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,version,sha256=Convert.ToHexStringLower(SHA256.HashData(archiveBytes)),size=archiveBytes.LongLength});
    byte[] signature=System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(manifest,HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence)));
    string Base(string file)=>$"https://github.com/{UpdateRelease.Repository}/releases/download/v{version}/{file}";
    object Asset(string file)=>new{name=file,browser_download_url=Base(file)};
    byte[] feed=JsonSerializer.SerializeToUtf8Bytes(new{draft=false,prerelease=false,tag_name="v"+version,assets=new[]{Asset(UpdateRelease.ArchiveName),Asset(UpdateRelease.ManifestName),Asset(UpdateRelease.ManifestName+".sig")}});
    using var service=new UpdateService(new FixtureHttp(new(){[UpdateRelease.Feed.AbsoluteUri]=feed,[Base(UpdateRelease.ArchiveName)]=archiveBytes,[Base(UpdateRelease.ManifestName)]=manifest,[Base(UpdateRelease.ManifestName+".sig")]=signature}),Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    // Fixture budget only; the installer keeps its own 60 s handoff and 45 s health deadlines.
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(180));
    string? preferences=null;byte[]? savedPreferences=null;
    try
    {
        var release=await service.Check(oldVersion,timeout.Token)??throw new Exception("Missing fixture release");
        var prepared=await MacUpdateInstaller.Prepare(service,release,target,history,timeout.Token);
        var launch=new ProcessStartInfo(Path.Combine(target,"Contents/MacOS/Blast Circuit")){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        // While the helper waits (60 s) for this running app, run it headless: first-use pipeline
        // compilation on a slow CI GPU must not decide the handoff. The relaunch always renders.
        foreach(var arg in highStartup?new[]{"--windowed","--","--display-windowed","--smoke","--smoke-seconds=4"}:new[]{"--headless","--","--smoke","--smoke-seconds=4"})
            launch.ArgumentList.Add(arg);
        using var old=Process.Start(launch)!;
        var stdout=old.StandardOutput.ReadToEndAsync();var stderr=old.StandardError.ReadToEndAsync();
        if(highStartup)
        {
            // The old build renders with settled/default settings first. The new
            // process must then load High before any menu layout, as real updates do.
            await old.WaitForExitAsync(timeout.Token);
            preferences=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Library/Application Support/Godot/app_userdata/Blast Circuit/preferences.cfg");
            if(File.Exists(preferences))savedPreferences=File.ReadAllBytes(preferences);
            Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
            File.WriteAllText(preferences,"[video]\ngraphicsPreset=\"high\"\nfullscreen=true\nresolution=Vector2i(0, 0)\n");
        }
        await MacUpdateInstaller.Schedule(prepared,old.Id,timeout.Token,windowed:!highStartup);
        if(!highStartup)
        {
            await Task.Delay(800,timeout.Token);
            if(await MacUpdateInstaller.PlistValue(target,"CFBundleShortVersionString")!=oldVersion)throw new Exception("Updater replaced a running app before it exited.");
        }
        await old.WaitForExitAsync(timeout.Token);
        var oldLog=await stdout;File.WriteAllText(Path.Combine(folder,"old-launch.log"),oldLog+await stderr);
        if(old.ExitCode!=0||!oldLog.Contains("GODOT_SMOKE_OK"))throw new Exception("Old app did not complete real gameplay before update.");
        string receipt=Path.Combine(history,"last-update.txt");
        while(!File.Exists(receipt))await Task.Delay(250,timeout.Token);
        string result=File.ReadAllText(receipt).Trim();
        string expected=success?"installed "+version:"failed "+version+" launch-rollback";
        if(result!=expected)throw new Exception(name+": "+result);
        if(await MacUpdateInstaller.PlistValue(target,"CFBundleShortVersionString")!=(success?version:oldVersion))throw new Exception("Installed version does not match transaction result.");
        await Tool("/usr/bin/codesign","--verify","--deep","--strict",target);
        // The relaunched app must outlive the installer, not disappear with its process group.
        await Task.Delay(2500,timeout.Token);
        var active=await OwnedPids(target);
        if(active.Length!=1)throw new Exception("Expected exactly one running installed app after helper exit.");
        results.Add(new{name,result,installedVersion=success?version:oldVersion,running=true,savedHighFullscreen=highStartup});
        Console.WriteLine("PASS "+name+": "+result);
    }
    finally
    {
        foreach(int pid in await OwnedPids(target))
        {try{using var process=Process.GetProcessById(pid);process.Kill();await process.WaitForExitAsync();}catch(ArgumentException){}}
        if(preferences is not null)
        {if(savedPreferences is not null)File.WriteAllBytes(preferences,savedPreferences);else File.Delete(preferences);}
        // These are isolated test bundles; preserve logs, never leave a second launchable app.
        if(Directory.Exists(installRoot))Directory.Delete(installRoot,true);
    }
}
static async Task<int[]> OwnedPids(string app)
{
    string command=Path.Combine(app,"Contents/MacOS/Blast Circuit");
    // Launch Services canonicalizes /var to /private/var in guest temporary
    // directories. Match the physical bundle path as well as the launch argv.
    string physical=(await Tool("/bin/sh","-c","cd \"$1\" && /bin/pwd -P","owned-update-app",app)).Trim();
    string canonical=Path.Combine(physical,"Contents/MacOS/Blast Circuit");
    var text=await Tool("/bin/ps","-axo","pid=,command=");
    return text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>line.Trim().Split(' ',2,StringSplitOptions.RemoveEmptyEntries))
        .Where(parts=>parts.Length==2&&new[]{command,canonical}.Any(path=>parts[1]==path||parts[1].StartsWith(path+" ",StringComparison.Ordinal)))
        .Select(parts=>int.Parse(parts[0])).ToArray();
}
static async Task<string> Tool(string file,params string[] args)
{
    var start=new ProcessStartInfo(file){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)start.ArgumentList.Add(arg);
    using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
    string text=await output,details=await error;if(process.ExitCode!=0)throw new Exception(file+": "+details);return text;
}
sealed class FixtureHttp(Dictionary<string,byte[]> data):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(
        data.TryGetValue(request.RequestUri!.AbsoluteUri,out var bytes)?new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)}:new HttpResponseMessage(HttpStatusCode.NotFound));
}

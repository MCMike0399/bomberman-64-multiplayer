using System.Diagnostics;
using System.Text.Json;
using Bomber.Client.Updates;
namespace Bomber.Client.Startup;

/// <summary>Protects renderer initialization before Godot or its managed code runs.</summary>
public sealed class WindowsLaunchGuard(string stateDirectory)
{
    public const string PreviousVulkanFailure="previous-vulkan-startup-incomplete";
    public const string VulkanUnavailable="vulkan-initialization-unavailable";
    public static string DefaultDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Godot","app_userdata","Blast Circuit","startup");
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private readonly string _directory=Path.GetFullPath(stateDirectory);
    public sealed record Preference(string Renderer="auto",string? Reason=null,string? RequestedDriver=null);
    private sealed record Marker(string Token,string Driver,int LauncherPid,string LauncherIdentity,DateTime CreatedUtc,int EnginePid=0,string? EngineIdentity=null);
    private sealed record Ready(string Token,int EnginePid,string Driver,string? FallbackReason);

    public Preference ReadPreference()
    {
        try
        {
            var value=JsonSerializer.Deserialize<Preference>(File.ReadAllText(Path.Combine(_directory,"renderer.json")),Json);
            return value is not null&&value.Renderer is "auto" or "vulkan" or "d3d12"?value:new();
        }
        catch(IOException){return new();}catch(JsonException){return new();}
    }
    public void SelectRenderer(string renderer)
    {
        if(renderer is not ("auto" or "vulkan" or "d3d12"))throw new ArgumentException("Unknown renderer.",nameof(renderer));
        Directory.CreateDirectory(_directory);Atomic(Path.Combine(_directory,"renderer.json"),new Preference(renderer));
    }
    public static bool RendererEvidenceAllowed(string? driver,string? requested,string? reason)=>driver=="vulkan"||
        driver=="d3d12"&&(requested=="vulkan"&&reason is VulkanUnavailable or PreviousVulkanFailure||requested=="d3d12"&&reason=="directx12-explicit-choice");
    public static string? Argument(IEnumerable<string> args,string name)=>args.FirstOrDefault(a=>a.StartsWith(name+"=",StringComparison.Ordinal)) is {} value?value[(name.Length+1)..]:null;

    public async Task<int> RunAsync(string engine,IReadOnlyList<string> arguments,CancellationToken ct)
    {
        engine=Path.GetFullPath(engine);if(!File.Exists(engine))throw new FileNotFoundException("The installed game is missing.",engine);
        Directory.CreateDirectory(_directory);
        var preference=ReadPreference();var before=new List<string>();var user=new List<string>();bool userArgs=false;string? explicitDriver=null;
        for(int i=0;i<arguments.Count;i++)
        {
            string arg=arguments[i];if(arg=="--"){userArgs=true;continue;}
            if(!userArgs&&arg=="--rendering-driver"&&i+1<arguments.Count){explicitDriver=arguments[++i];continue;}
            if(!userArgs&&arg.StartsWith("--rendering-driver=",StringComparison.Ordinal)){explicitDriver=arg[19..];continue;}
            (userArgs?user:before).Add(arg);
        }
        string selected=explicitDriver??(preference.Renderer=="auto"?"vulkan":preference.Renderer);
        string requested=explicitDriver??preference.RequestedDriver??selected;
        string? reason=explicitDriver is null?preference.Reason:null;
        var stale=new List<string>();
        foreach(string file in Directory.EnumerateFiles(_directory,"boot-*.json"))
        {
            Marker? value=ReadMarker(file);
            if(value is null||value.Driver!="vulkan")continue;
            bool alive=value.EnginePid>0?Alive(value.EnginePid,value.EngineIdentity):
                Alive(value.LauncherPid,value.LauncherIdentity)&&DateTime.UtcNow-value.CreatedUtc<TimeSpan.FromMinutes(1);
            if(!alive)stale.Add(file);
        }
        if(stale.Count>0&&selected=="vulkan")
        {
            selected="d3d12";requested="vulkan";reason=PreviousVulkanFailure;
            Atomic(Path.Combine(_directory,"renderer.json"),new Preference(selected,reason,requested));
            foreach(string file in stale)File.Delete(file);
        }
        bool guarded=selected=="vulkan"&&!before.Contains("--headless");
        string token=Guid.NewGuid().ToString("N"),markerPath=Path.Combine(_directory,"boot-"+token+".json"),readyPath=Path.Combine(_directory,"boot-"+token+".healthy");
        using var self=Process.GetCurrentProcess();string selfIdentity=DesktopProcessIdentity.StartToken(self);
        var marker=new Marker(token,selected,self.Id,selfIdentity,DateTime.UtcNow);
        if(guarded)Atomic(markerPath,marker); // Durable BEFORE Process.Start can enter a Vulkan layer.
        var start=new ProcessStartInfo(engine){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(engine)!,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("--rendering-driver");start.ArgumentList.Add(selected);
        foreach(string arg in before)start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--");foreach(string arg in user)start.ArgumentList.Add(arg);
        foreach(string arg in new[]{"--launcher-state-dir="+_directory,"--launcher-parent-pid="+self.Id,"--launcher-parent-identity="+selfIdentity,"--launcher-requested-driver="+requested})start.ArgumentList.Add(arg);
        if(reason is not null)start.ArgumentList.Add("--launcher-fallback-reason="+reason);
        if(guarded)foreach(string arg in new[]{"--launcher-marker="+markerPath,"--launcher-ready="+readyPath,"--launcher-token="+token})start.ArgumentList.Add(arg);
        using var child=Process.Start(start)??throw new IOException("Engine did not start.");
        if(guarded)Atomic(markerPath,marker with{EnginePid=child.Id,EngineIdentity=DesktopProcessIdentity.StartToken(child)});
        Task output=child.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput(),ct),error=child.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError(),ct);
        bool healthy=false;
        try
        {
            var exited=child.WaitForExitAsync(ct);
            while(!exited.IsCompleted)
            {
                if(guarded&&!healthy&&ReadReady(readyPath,token,child.Id)is {} ready)
                {
                    healthy=true;File.Delete(markerPath);File.Delete(readyPath);
                    if(ready.Driver=="d3d12"&&ReadPreference().Renderer=="auto")
                        Atomic(Path.Combine(_directory,"renderer.json"),new Preference("d3d12",VulkanUnavailable,"vulkan"));
                }
                await Task.WhenAny(exited,Task.Delay(50,ct));ct.ThrowIfCancellationRequested();
            }
            await exited;await Task.WhenAll(output,error);
            // A clean early user close is not a renderer crash.
            if(guarded&&(healthy||child.ExitCode==0)){File.Delete(markerPath);File.Delete(readyPath);}
            return child.ExitCode;
        }
        catch
        {
            if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync(CancellationToken.None);}
            throw;
        }
    }

    public static bool ReportHealthy(IEnumerable<string> args,string driver,string? fallbackReason)
    {
        string? file=Argument(args,"--launcher-marker"),ready=Argument(args,"--launcher-ready"),token=Argument(args,"--launcher-token");
        if(file is null&&ready is null&&token is null)return true;
        if(token is null||!Guid.TryParseExact(token,"N",out _)||file is null||ready is null||
            Path.GetFileName(file)!="boot-"+token+".json"||Path.ChangeExtension(file,".healthy")!=ready)return false;
        Marker? marker=ReadMarker(file);if(marker is null||marker.Token!=token||marker.EnginePid!=Environment.ProcessId)return false;
        Atomic(ready,new Ready(token,Environment.ProcessId,driver,fallbackReason));return true;
    }
    public static int? VerifiedLauncherPid(IEnumerable<string> args,string installedExecutable)
    {
        if(!int.TryParse(Argument(args,"--launcher-parent-pid"),out int pid)||Argument(args,"--launcher-parent-identity")is not {} identity)return null;
        try
        {
            using var parent=Process.GetProcessById(pid);
            return DesktopProcessIdentity.Matches(parent,identity)&&string.Equals(Path.GetFullPath(parent.MainModule!.FileName!),Path.GetFullPath(installedExecutable),StringComparison.OrdinalIgnoreCase)?pid:null;
        }
        catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}catch(System.ComponentModel.Win32Exception){return null;}
    }
    private static Marker? ReadMarker(string path)
    {
        try
        {
            var marker=JsonSerializer.Deserialize<Marker>(File.ReadAllText(path),Json);
            return marker is not null&&Guid.TryParseExact(marker.Token,"N",out _)&&Path.GetFileName(path)=="boot-"+marker.Token+".json"?marker:null;
        }
        catch(IOException){return null;}catch(JsonException){return null;}
    }
    private static Ready? ReadReady(string path,string token,int pid)
    {
        try {var value=JsonSerializer.Deserialize<Ready>(File.ReadAllText(path),Json);return value is not null&&value.Token==token&&value.EnginePid==pid&&value.Driver is "vulkan" or "d3d12"?value:null;}
        catch(IOException){return null;}catch(JsonException){return null;}
    }
    private static bool Alive(int pid,string? identity)
    {
        if(identity is null)return false;
        try {using var process=Process.GetProcessById(pid);return DesktopProcessIdentity.Matches(process,identity)&&!process.HasExited;}
        catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(System.ComponentModel.Win32Exception){return false;}
    }
    private static void Atomic<T>(string path,T value)
    {
        string temp=path+".tmp-"+Guid.NewGuid().ToString("N");
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {byte[] data=JsonSerializer.SerializeToUtf8Bytes(value,Json);stream.Write(data);stream.Flush(true);}
            File.Move(temp,path,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

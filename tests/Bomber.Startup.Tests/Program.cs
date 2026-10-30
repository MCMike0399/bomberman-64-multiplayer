using System.Diagnostics;
using Bomber.Client.Startup;

if(args.Contains("--fixture"))
{
    string output=args.First(a=>a.StartsWith("--fixture-output="))[17..];
    int driverIndex=Array.IndexOf(args,"--rendering-driver");string driver=args[driverIndex+1];
    string? marker=args.FirstOrDefault(a=>a.StartsWith("--launcher-marker="))?[18..];
    File.AppendAllText(output,driver+" marker="+(marker is not null&&File.Exists(marker))+"\n");
    if(WindowsLaunchGuard.Argument(args,"--launcher-engine-driver")!=driver)return 25;
    if(args.Contains("--fixture-healthy"))
    {
        // The parent records the child identity just after Process.Start.
        var deadline=Stopwatch.StartNew();
        while(!WindowsLaunchGuard.ReportHealthy(args,driver,null))
        {
            if(deadline.Elapsed>TimeSpan.FromSeconds(3))return 24;
            await Task.Delay(10);
        }
        await Task.Delay(200); // Let the launcher consume the healthy acknowledgement.
    }
    return driver=="vulkan"?23:0; // Abnormal first-frame exit, before any healthy acknowledgement.
}
string root=Path.Combine(Path.GetTempPath(),"blast-startup-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
int checks=0;
void Check(bool value,string text){if(!value)throw new Exception(text);checks++;}
try
{
    string engine=Path.Combine(root,"BlastCircuit.Player.exe");
    foreach(string file in Directory.GetFiles(AppContext.BaseDirectory))File.Copy(file,Path.Combine(root,Path.GetFileName(file)));
    File.Copy(Environment.ProcessPath!,engine);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(engine,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
    string output=Path.Combine(root,"engines.txt");var guard=new WindowsLaunchGuard(Path.Combine(root,"state"));
    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
    string[] childArgs=["--fixture","--fixture-output="+output];
    Check(await guard.RunAsync(engine,childArgs,deadline.Token)!=0,"Injected first-frame crash must be retained");
    Check(File.ReadAllLines(output)[0]=="vulkan marker=True","Crash guard marker exists before renderer process code executes");
    Check(await guard.RunAsync(engine,childArgs,deadline.Token)==0,"Next launch recovers through DirectX12");
    Check(File.ReadAllLines(output)[1].StartsWith("d3d12 ",StringComparison.Ordinal),"Recovered renderer is actually DirectX12");
    Check(await guard.RunAsync(engine,childArgs,deadline.Token)==0,"Recovered choice is remembered on later launch");
    Check(File.ReadAllLines(output)[2].StartsWith("d3d12 ",StringComparison.Ordinal),"Remembered choice remains DirectX12");
    guard.SelectRenderer("auto");
    Check(await guard.RunAsync(engine,[..childArgs,"--fixture-healthy"],deadline.Token)==23,"A post-health crash remains a failed process");
    Check(!Directory.EnumerateFiles(Path.Combine(root,"state"),"boot-*.json").Any(),"Healthy renderer acknowledgement clears the startup marker even if a later game crash occurs");
    Check(await guard.RunAsync(engine,[..childArgs,"--fixture-healthy"],deadline.Token)==23,"A later gameplay crash does not silently change the renderer");
    Check(File.ReadAllLines(output)[4].StartsWith("vulkan ",StringComparison.Ordinal),"Healthy startup retains Vulkan on next launch");
    Check(await guard.RunAsync(engine,["--headless",..childArgs],deadline.Token)==23,"Headless child exit is preserved");
    Check(File.ReadAllLines(output)[5]=="vulkan marker=False","Headless execution does not create a graphical startup crash marker");
    bool restored=false;
    Check(await guard.RunAsync(engine,["--headless",..childArgs],deadline.Token,_=>new EngineScope(()=>restored=true))==23,"The owned engine may fail after creating its lifetime guard");
    Check(restored,"Launcher-owned state is restored after an abnormal engine exit");
    WindowsHotkeyChecks.Run(Check,args);
    Console.WriteLine($"PASS {checks} crash-safe renderer startup checks");
}
finally{Directory.Delete(root,true);}
return 0;

sealed class EngineScope(Action restore):IDisposable
{
    public void Dispose()=>restore();
}

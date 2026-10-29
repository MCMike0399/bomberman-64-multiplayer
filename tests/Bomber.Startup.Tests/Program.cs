using System.Diagnostics;
using Bomber.Client.Startup;

if(args.Contains("--fixture"))
{
    string output=args.First(a=>a.StartsWith("--fixture-output="))[17..];
    int driverIndex=Array.IndexOf(args,"--rendering-driver");string driver=args[driverIndex+1];
    string? marker=args.FirstOrDefault(a=>a.StartsWith("--launcher-marker="))?[18..];
    File.AppendAllText(output,driver+" marker="+(marker is not null&&File.Exists(marker))+"\n");
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
    Console.WriteLine($"PASS {checks} crash-safe renderer startup checks");
}
finally{Directory.Delete(root,true);}
return 0;

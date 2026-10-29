using Bomber.DesktopLauncher;
using Bomber.Client.Startup;

try
{
    string folder=WindowsLaunchGuard.Argument(args,"--launcher-state-dir")??WindowsLaunchGuard.DefaultDirectory;
    string engine=Path.Combine(AppContext.BaseDirectory,"BlastCircuit.Player.exe");
    return await new WindowsLaunchGuard(folder).RunAsync(engine,args,CancellationToken.None,child=>WindowsAccessibilityHotkeys.Track(child.Id));
}
catch(Exception error)
{
    string text="Blast Circuit could not start.\n\n"+error.Message+"\n\nTry the DirectX 12 Start-menu shortcut or reinstall from the official release page.";
    if(OperatingSystem.IsWindows())Native.MessageBox(IntPtr.Zero,text,"Blast Circuit",0x10);
    else Console.Error.WriteLine(text);
    return 1;
}

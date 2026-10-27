using Godot;
using Bomber.Client.Updates;
using Bomber.Game.Online;
namespace BlastCircuit.Updates;

/// <summary>Checks in the background and offers a verified update only at a safe menu boundary.</summary>
public partial class GameUpdater : Node
{
    private readonly CancellationTokenSource _stop = new();
    private readonly UpdateService _service = new();
    private string? _bundle, _healthPath;
    private PreparedUpdate? _ready;
    private string _historyDirectory="",_failedVersion="";
    private bool _busy,_checked,_initialized,_installing;
    private double _uptime;
    public string Version {get;private set;}="Development build";
    public string Status {get;private set;}="Updates are available in the installed Mac app.";
    public bool UpdateReady=>_ready!=null;
    public bool Busy=>_busy;
    public override async void _Ready()
    {
        if(!OperatingSystem.IsMacOS())
        {
            Version=OnlineProtocol.ReleaseVersion;
            Status="Download the latest release, close the game, then extract it to a new folder. Your settings are kept.";
            return;
        }
        if(!OperatingSystem.IsMacOS()||OS.HasFeature("editor")||OS.GetCmdlineUserArgs().Any(a=>a.StartsWith("--playtest=")))return;
        try
        {
            _bundle=MacUpdateInstaller.BundleForExecutable(OS.GetExecutablePath());if(_bundle==null)return;
            _historyDirectory=Path.Combine(OS.GetUserDataDir(),"updates");Directory.CreateDirectory(_historyDirectory);
            var tokenArg=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--update-token="));
            if(tokenArg is not null&&Guid.TryParseExact(tokenArg.Split('=')[1],"N",out var token))
            {
                string stage=Path.Combine(Path.GetDirectoryName(_bundle)!,".blast-update-"+token.ToString("N"));
                if(Directory.Exists(stage)&&new DirectoryInfo(stage).LinkTarget is null)
                { _healthPath=Path.Combine(stage,"healthy");File.WriteAllText(Path.Combine(stage,"newpid"),OS.GetProcessId().ToString()); }
            }
            Version=await MacUpdateInstaller.PlistValue(_bundle,"CFBundleShortVersionString",_stop.Token);
            Status="Updates are verified before installation.";
            string history=Path.Combine(_historyDirectory,"last-update.txt");
            if(File.Exists(history))
            {
                var parts=File.ReadAllText(history).Trim().Split(' ');
                if(parts.Length>=2&&parts[0]=="failed"){_failedVersion=parts[1];Status=$"The previous app was restored after v{_failedVersion} could not be installed.";}
            }
            _initialized=true;
        }
        catch(Exception error){Status="Updates unavailable: "+error.Message;}
    }
    public void Tick(double delta,bool automatic,bool idle)
    {
        _uptime+=delta;
        if(_healthPath!=null&&_initialized&&_uptime>=3&&Engine.GetFramesDrawn()>2)
        {
            File.WriteAllText(_healthPath,Version);_healthPath=null;
            GD.Print("BLAST_UPDATE_HEALTHY "+Version);
        }
        if(_initialized&&automatic&&idle&&!_checked&&_uptime>=5&&!_busy&&!UpdateReady)
        {_checked=true;_ = Check();}
    }
    public async Task Check()
    {
        if(!OperatingSystem.IsMacOS())
        {
            OS.ShellOpen($"https://github.com/{UpdateRelease.Repository}/releases/latest");
            return;
        }
        if(!_initialized||_bundle==null||_busy||UpdateReady)return;
        _busy=true;Status="Checking for updates…";
        try
        {
            var release=await _service.Check(Version,_stop.Token);
            if(release==null){Status="You have the latest published version.";return;}
            if(release.Version==_failedVersion){Status=$"v{_failedVersion} was rolled back. Waiting for a newer release.";return;}
            Status=$"Downloading and verifying v{release.Version}…";
            _ready=await MacUpdateInstaller.Prepare(_service,release,_bundle,_historyDirectory,_stop.Token);
            Status=$"v{release.Version} is ready. Restart to update.";
        }
        catch(OperationCanceledException){Status="Update check cancelled.";}
        catch(Exception error){Status="Could not update: "+error.Message;}
        finally {_busy=false;}
    }
    public async Task<bool> Install()
    {
        if(_ready is not {} ready||_busy)return false;
        _busy=true;Status="Restarting to install the update…";
        try
        {
            await MacUpdateInstaller.Schedule(ready,(int)OS.GetProcessId(),_stop.Token,OS.GetCmdlineUserArgs().Contains("--display-windowed"));
            _installing=true;return true;
        }
        catch(Exception error){_busy=false;Status="Could not restart: "+error.Message;return false;}
    }
    public override void _ExitTree()
    {
        _stop.Cancel();_service.Dispose();
        // Uninstalled staging is recreated after the next signed-feed check; no stray launchable copies.
        if(!_installing&&_ready is {} ready&&Directory.Exists(ready.Stage))Directory.Delete(ready.Stage,true);
    }
}

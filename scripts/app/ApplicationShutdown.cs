using System.Text.Json;
using BlastCircuit.Arena.Effects;
using Bomber.Game.Online;
using Godot;
namespace BlastCircuit.App;

/// <summary>Own the save/producer-stop/drain sequence before native engine cleanup.</summary>
public partial class ApplicationShutdown : Node
{
    public static ApplicationShutdown Current {get;private set;}=null!;
    private Func<bool>? _save;
    private string? _cleanup;
    private string _report="";
    private Window _window=null!;
    private bool _attempted,_saved;
    private int _cancelledDraws;
    private ShutdownDrain.Result? _drain;
    private ManagedFinalizerDrain.Result? _finalizers;
    public override void _Ready()
    {
        Current=this;GetTree().AutoAcceptQuit=false;_window=GetWindow();_window.CloseRequested+=CloseRequested;
        var args=OS.GetCmdlineUserArgs();
        string? output=args.FirstOrDefault(a=>a.StartsWith("--smoke-output=")||a.StartsWith("--playtest-output="))?.Split('=',2)[1];
        _report=Path.Combine(output is not null&&Path.IsPathFullyQualified(output)?output:OS.GetUserDataDir(),"shutdown.json");
    }
    public void ConfigureSave(Func<bool> save,string? cleanup){_save=save;_cleanup=cleanup;}
    private void CloseRequested()=>RequestQuit(0);
    public void RequestQuit(int exitCode=0)
    {
        if(!_attempted)Prepare();
        GetTree().Quit(_saved&&_finalizers?.Completed==true&&_drain?.Completed==true?exitCode:1);
    }
    private void Prepare()
    {
        _attempted=true;string? error=null;
        try{_saved=_save?.Invoke()??true;}
        catch(Exception failure){error=failure.Message;}
        if(!_saved)
        {
            error??="Saved state could not be flushed";
            GD.PushError("BLAST_SHUTDOWN_SAVE_FAILED: "+error);
        }
        // A failed preferences write must never make the application unclosable.
        // Keep draining and exit unsuccessfully; QA retains the save failure.
        try
        {
            _cancelledDraws=EffectWarmup.CancelPending();
            RenderingServer.RenderLoopEnabled=false;
            RenderingServer.ForceSync();
            _finalizers=ManagedFinalizerDrain.Run(TimeSpan.FromSeconds(5));
            if(!_finalizers.Completed)GD.PushError("BLAST_SHUTDOWN_FINALIZERS_FAILED: "+_finalizers.Detail);
            // Resource destructors may enqueue render/worker cleanup. Drain that
            // work after finalizers, before the engine detaches C# bindings.
            RenderingServer.ForceSync();
            _drain=ShutdownDrain.Run(TimeSpan.FromSeconds(15));
            if(!_drain.Completed)GD.PushError("BLAST_SHUTDOWN_DRAIN_FAILED: "+_drain.Detail);
        }
        catch(Exception failure){error=error is null?failure.Message:error+"; "+failure.Message;GD.PushError("BLAST_SHUTDOWN_DRAIN_FAILED: "+failure.Message);}
        finally
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_report)!);
                File.WriteAllText(_report,JsonSerializer.Serialize(new {schemaVersion=2,sourceRevision=OnlineProtocol.SourceRevision,savedStateFlushed=_saved,
                    watchdogEnabled=false,watchdogFired=false,cancelledPreparationCallbacks=_cancelledDraws,managedFinalizers=_finalizers,drain=_drain,error},
                    new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase})+"\n");
            }
            catch(Exception failure){GD.PushError("BLAST_SHUTDOWN_REPORT_FAILED: "+failure.Message);}
        }
    }
    public override void _ExitTree()
    {
        if(GodotObject.IsInstanceValid(_window))_window.CloseRequested-=CloseRequested;
        if(!_attempted)Prepare();
        if(_cleanup is not null)File.Delete(_cleanup);
    }
}

using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Online;
using System.Diagnostics;
using System.Text.Json;
using BlastCircuit.App;
using BlastCircuit.Arena.Models;
using BlastCircuit.Audio;
using BlastCircuit.Rendering;
using BlastCircuit.UI;
using BlastCircuit.UI.Hud;
using BlastCircuit.UI.Menu;

namespace BlastCircuit.Testing;

/// <summary>Runs real game input through GameRoot; profile inspection uses a temporary camera without editing simulation.</summary>
public partial class PlaytestDriver : Node
{
    private bool _adaptiveSlowPolling;
    private GameRoot _root = null!;
    private string _scenario = "all", _output = "", _server = "http://127.0.0.1:18496";
    private StreamWriter _trace = null!;
    private int _frame, _lastSnapshot = -1, _captureFallbacks;
    private readonly List<object> _checks = [];
    private readonly List<string> _screenshots = [];
    private readonly List<object> _graphicsPreparations = [];
    private readonly Dictionary<int, PlayerInput> _inputs = [];
    private readonly Stopwatch _clock = new();
    private bool _headless, _windowedRequested;
    private bool _displayRestartRequested;
    private bool _slowMovementFeedback,_delayedPeerFeedback;
    // QA-only: block the engine thread each frame to reproduce software-renderer
    // frame times headlessly. Recorded in the summary; release receipts reject it.
    private int _frameStallMs;
    public override void _Process(double delta){if(_frameStallMs>0)System.Threading.Thread.Sleep(_frameStallMs);}

    public static bool TryAttach(GameRoot root)
    {
        var args = OS.GetCmdlineUserArgs();
        if (!args.Any(a => a.StartsWith("--playtest="))) return false;
        GodotPeerLink.CaptureSignalDiagnostics=true;
        var driver = new PlaytestDriver { _root = root };
        root.AddChild(driver);
        return true;
    }

    public override async void _Ready()
    {
        _root.LocalMatchSeedOverride=64;
        var args=OS.GetCmdlineUserArgs();
        string Arg(string key,string fallback) => args.FirstOrDefault(a => a.StartsWith(key+"="))?[(key.Length+1)..] ?? fallback;
        _scenario=Arg("--playtest","all");
        _output=Path.GetFullPath(Arg("--playtest-output",ProjectSettings.GlobalizePath("res://artifacts/playtest")));
        _server=Arg("--playtest-server",_server);
        _frameStallMs=Math.Clamp(int.TryParse(Arg("--playtest-frame-stall-ms","0"),out int stall)?stall:0,0,5000);
        Directory.CreateDirectory(_output);
        _trace=new StreamWriter(Path.Combine(_output,"trace.jsonl")) { AutoFlush=true, NewLine="\n" };
        _headless=DisplayServer.GetName()=="headless";
        _windowedRequested=args.Contains("--display-windowed");
        _clock.Start();
        Exception? failure=null;
        try
        {
            await Frames(3);
            if(!_headless)
            {
                bool fullscreen=args.Contains("--playtest-restore-display")?_root.Preferences.Fullscreen:!_windowedRequested;
                await WindowMode(fullscreen?Window.ModeEnum.Fullscreen:Window.ModeEnum.Windowed,"launch reaches requested display mode");
            }
            Event("begin",new { scenario=_scenario,display=DisplayServer.GetName(),deterministicSeed=64 });
            _catalogHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Godot.FileAccess.GetFileAsBytes("res://assets/testing/scenarios.json"))).ToLowerInvariant();
            var file=Arg("--playtest-file","");
            if(file.Length>0){int before=_checks.Count;await Script(file);if(_checks.Count==before)throw new InvalidOperationException("Script produced no checks");_completedScenarios.Add("script");}
            else await RunRegisteredScenario(_scenario);
        }
        catch(Exception error)
        {
            failure=error; Event("failure",new { error=error.ToString() }); GD.PushError(error.ToString());
            try { await Capture("failure"); } catch(Exception captureError) { Event("capture-error",new { error=captureError.Message }); }
        }
        finally
        {
            var backend=RendererBackend.Observe();
            var summary=new { schemaVersion=1,catalogSha256=_catalogHash,completedScenarios=_completedScenarios,scenario=_scenario,passed=failure==null,frames=_frame,seconds=_clock.Elapsed.TotalSeconds,
                assertions=_checks.Count,checks=_checks,frameStallMs=_frameStallMs,screenshots=_screenshots,captureFallbacks=_captureFallbacks,screenshotMode=_headless?"disabled-headless":"graphical",
                failure=failure?.ToString(),screen=_root.ScreenName,snapshots=_root.SnapshotCount,finalState=_root.LatestState,
                platform=OS.GetName(),renderingDriver=backend.Driver,requestedDriver=backend.RequestedDriver,fallbackReason=backend.FallbackReason,
                renderingMethod=RenderingServer.GetCurrentRenderingMethod(),adapter=RenderingServer.GetVideoAdapterName(),
                adapterType=RenderingServer.GetVideoAdapterType().ToString(),
                displayRestartRequested=_displayRestartRequested,displayRestoration=args.Contains("--playtest-restore-display"),
                release=new {version=OnlineProtocol.ReleaseVersion,build=OnlineProtocol.ReleaseBuild,
                    protocolRevision=OnlineProtocol.Revision,admissionToken=OnlineProtocol.WireRevision,revision=OnlineProtocol.SourceRevision} };
            File.WriteAllText(Path.Combine(_output,"summary.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions { WriteIndented=true,NewLine="\n" }));
            _trace.Dispose();
            GD.Print($"PLAYTEST_{(failure==null?"PASS":"FAIL")} scenario={_scenario} frames={_frame} assertions={_checks.Count} screenshots={_screenshots.Count} output={_output}");
            _root.RequestQuit(failure==null?0:1);
        }
    }

    private async Task KeyPress(Key key,bool alt=false)
    {
        int InputWindow(){int popup=DisplayServer.WindowGetActivePopup();return popup>=0?popup:GetWindow().GetWindowId();}
        Godot.Input.ParseInputEvent(new InputEventKey {WindowId=InputWindow(),Keycode=key,PhysicalKeycode=key,AltPressed=alt,Pressed=true});
        await Frames(1);
        Godot.Input.ParseInputEvent(new InputEventKey {WindowId=InputWindow(),Keycode=key,PhysicalKeycode=key,AltPressed=alt,Pressed=false});
        await Frames(3);
    }
    private async Task ResizeWindow(Vector2I size)
    {
        GetWindow().Size=size;
        if(_headless){await Frames(6);return;}
        var wall=Stopwatch.StartNew();double? stableSince=null;
        var previous=GetViewport().GetScreenTransform();bool stable=false;int samples=0;
        // A real first frame after resizing can compile Metal render targets
        // for several seconds in the QA guest. Still require the actual size
        // and unchanged input transform for 200 ms, with a bounded wait.
        while(wall.Elapsed.TotalSeconds<20)
        {
            await Frames(1);await Task.Delay(20);
            samples++;
            var transform=GetViewport().GetScreenTransform();
            if(GetWindow().Size==size&&transform.IsEqualApprox(previous))
            {
                stableSince??=wall.Elapsed.TotalSeconds;
                if(wall.Elapsed.TotalSeconds-stableSince.Value>=.2){stable=true;break;}
            }
            else stableSince=null;
            previous=transform;
        }
        Event("window-resize-settle",new{requested=size.ToString(),actual=GetWindow().Size.ToString(),samples,seconds=wall.Elapsed.TotalSeconds,stable});
        Check(stable,"native window size and input transform settle: "+size);
    }
    private Button NamedButton(string name) => _root.FindChild(name,true,false) as Button ?? throw new InvalidOperationException("Button not found: "+name);
    private async Task ActivateNamed(string name)
    {
        var button=NamedButton(name);
        Check(button.IsVisibleInTree()&&!button.Disabled,"named button visible: "+name);
        button.GrabFocus();await Frames(1);await KeyPress(Key.Enter);
    }
    private JsonElement Roster => JsonSerializer.SerializeToElement(_root.RosterState);
    private Button FindButton(string label)
    {
        Button? Find(Node node)
        {
            foreach(var child in node.GetChildren())
            {
                if(child is Button button && button.IsVisibleInTree() && !button.Disabled && button.Text.Contains(label,StringComparison.OrdinalIgnoreCase))return button;
                var found=Find(child);if(found!=null)return found;
            }
            return null;
        }
        return Find(_root)??throw new InvalidOperationException("Visible button not found: "+label);
    }
    private async Task Click(string label)
    {
        var target=FindButton(label);
        target.GrabFocus();await Frames(1);
        Check(target.HasFocus(),"button receives keyboard focus: "+label);
        Event("activate",new {label,method="Input.ParseInputEvent Enter"});
        Godot.Input.ParseInputEvent(new InputEventKey {Keycode=Key.Enter,PhysicalKeycode=Key.Enter,Pressed=true});
        await Frames(1);
        Godot.Input.ParseInputEvent(new InputEventKey {Keycode=Key.Enter,PhysicalKeycode=Key.Enter,Pressed=false});
        await Frames(3);
    }
    private async Task MouseClick(string label)
    {
        await ControllerFocusReady();
        var target=FindButton(label);
        // ParseInputEvent does not move the OS pointer. Pending native mouse-motion
        // events could otherwise cancel the synthetic press during the next frame.
        target.GetViewport().WarpMouse(target.GetGlobalTransformWithCanvas()*(target.Size/2));
        await Frames(2);await Task.Delay(40);
        // Godot's documented canvas-local -> screen transform includes stretch and window placement.
        var center=target.GetViewport().GetScreenTransform()*target.GetGlobalTransformWithCanvas()*(target.Size/2);
        Event("mouse-click",new {label,x=center.X,y=center.Y,windowWidth=GetWindow().Size.X,windowHeight=GetWindow().Size.Y});
        Godot.Input.ParseInputEvent(new InputEventMouseMotion {Position=center,GlobalPosition=center});
        // Deliver one ordinary click as a paired event burst. Splitting it over
        // rendered frames lets a pending native warp-motion cancel the press.
        // Native control hit testing and pressed/released signals still run.
        Godot.Input.ParseInputEvent(new InputEventMouseButton {Position=center,GlobalPosition=center,ButtonIndex=MouseButton.Left,Pressed=true});
        Godot.Input.ParseInputEvent(new InputEventMouseButton {Position=center,GlobalPosition=center,ButtonIndex=MouseButton.Left,Pressed=false});
        await Frames(3);
    }
    private JsonElement Visual => JsonSerializer.SerializeToElement(_root.VisualState);
    private static string VisibleText(Node node)
    {
        var parts=new List<string>();
        if(node is Label label && label.IsVisibleInTree())parts.Add(label.Text);
        foreach(var child in node.GetChildren())parts.Add(VisibleText(child));
        return string.Join("\n",parts.Where(p=>p.Length>0));
    }
    private JsonElement State => _root.LatestState ?? throw new InvalidOperationException("No authoritative state yet.");
    private JsonElement Player => State.GetProperty("players").EnumerateArray().OrderBy(p=>p.GetProperty("slot").GetInt32()).First();
    private void Input(int seat,PlayerInput input)
    {
        _inputs[seat]=input;_root.InjectInput(seat,input);
        // Publish transport intent immediately: a slow rendered frame must not
        // delay the test controller's neutral command at a narrow stair landing.
        if(seat==0&&_root.ActiveSession is NetworkSession network)network.SendInput(0,input);
        Event("input",new { seat,input.X,input.Z,input.A,input.B,input.R,input.Aim });
    }
    private readonly HashSet<ulong> _tracedRolePopups=[];
    /// <summary>Trace every Roster role-popup close: runtime-log allowlisting is bounded by these markers.</summary>
    private void TraceRolePopups()
    {
        if(_root.ScreenName!="Roster"||_tracedRolePopups.Count>=8)return;
        for(int slot=0;slot<8;slot++)
        {
            if(_root.FindChild($"Slot{slot}Role",true,false) is not OptionButton role)continue;
            var popup=role.GetPopup();int traced=slot;
            if(_tracedRolePopups.Add(popup.GetInstanceId()))
                popup.PopupHide+=()=>Event("role-popup-closed",new {slot=traced,embedded=popup.IsEmbedded()});
        }
    }
    private async Task Frames(int count)
    {
        for(var i=0;i<count;i++)
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); _frame++;
            if(_frame%30==0)ProgressCheckpoint();
            TraceRolePopups();
            if(_lastSnapshot!=_root.SnapshotCount)
            {
                _lastSnapshot=_root.SnapshotCount;
                Event("state",new { screen=_root.ScreenName,snapshots=_lastSnapshot,state=_root.LatestState });
            }
        }
    }
    private async Task WaitState(Func<JsonElement,bool> predicate,string description,int maxFrames)
    {
        for(var i=0;i<maxFrames;i++)
        {
            if(_root.LatestState is {} state && predicate(state)) { Check(true,description);return; }
            await Frames(1);
            if(_root.ActiveSession is NetworkSession)await Task.Delay(16);
        }
        Check(false,description+" (timeout)");
    }
    private async Task MoveTo(float x,float z,int maxFrames)
    {
        Event("move-to",new { x,z,maxFrames });
        bool network=_root.ActiveSession is NetworkSession;
        float tolerance=network?.35f:.15f;
        for(var i=0;i<maxFrames;i++)
        {
            var dx=x-Player.GetProperty("x").GetSingle();var dz=z-Player.GetProperty("z").GetSingle();
            if(network&&_slowMovementFeedback)System.Threading.Thread.Sleep(180);
            if(MathF.Sqrt(dx*dx+dz*dz)<tolerance)
            {
                Input(0,default);
                if(network)
                {
                    int stoppedAt=_root.SnapshotCount;
                    await WaitState(_=>_root.SnapshotCount>=stoppedAt+3,"network movement settles after release",120);
                    float sx=x-Player.GetProperty("x").GetSingle(),sz=z-Player.GetProperty("z").GetSingle();
                    Check(MathF.Sqrt(sx*sx+sz*sz)<.5f,$"network walk settles inside destination cell {x},{z}: {Player}");
                }
                await Frames(3);Check(true,$"walk to {x},{z}");return;
            }
            Input(0,FeedbackSteering.Toward(dx,dz,network&&_delayedPeerFeedback));
            if(_root.ActiveSession is NetworkSession connection&&connection.Party?.Hosting is not ("peer" or "auto"))
            {
                // An observer controls a real-time server from delayed snapshots.
                // Bound the ordinary key press independently of a stalled engine
                // frame, then observe fresh snapshots after release before steering.
                var release=Task.Run(async()=>
                {
                    await Task.Delay(50).ConfigureAwait(false);
                    connection.SendInput(0,default);
                });
                try {await Frames(1);await release;}
                finally {Input(0,default);}
                int stoppedAt=_root.SnapshotCount;
                await WaitState(_=>_root.SnapshotCount>=stoppedAt+3,"network control pulse settles after release",120);
                continue;
            }
            await Frames(1);
            if(_root.ActiveSession is NetworkSession)await Task.Delay(16);
        }
        Input(0,default); Check(false,$"walk to {x},{z}: timed out at {Player}");
    }
    private void Check(bool passed,string description)
    {
        _checks.Add(new { description,passed,frame=_frame }); Event("assert",new {description,passed});
        if(!passed)throw new InvalidOperationException("Assertion failed: "+description);
    }
    // Verified software rasterizers: Mesa's CPU device, or Windows WARP, which
    // Godot's D3D12 driver reports as IntegratedGpu under this exact name.
    private static bool SoftwareRenderer=>RenderingServer.GetVideoAdapterType()==RenderingDevice.DeviceType.Cpu||
        OS.GetName()=="Windows"&&RenderingServer.GetCurrentRenderingDriverName()=="d3d12"&&
        RenderingServer.GetVideoAdapterName()=="Microsoft Basic Render Driver";
    // Deadline for "wait until" loops. Hardware keeps the wall-clock deadline.
    // Hosted WARP can draw about one frame per second, so a software renderer
    // also gets the frames a 30 FPS machine would draw in that time. A wedged
    // predicate still fails; this is not native GPU responsiveness evidence.
    private static bool Waiting(Stopwatch clock,double seconds,ulong firstFrame)=>clock.Elapsed.TotalSeconds<seconds||
        SoftwareRenderer&&Engine.GetProcessFrames()-firstFrame<(ulong)(seconds*30);
    // A real-time server samples held buttons in wall time. On a software
    // renderer one frame can last seconds, so "press for N frames" becomes a
    // long hold (a held A charges a throw instead of placing a bomb). Send the
    // press edge now and release it after a bounded wall-clock interval.
    private async Task NetworkTap(NetworkSession connection,PlayerInput press,PlayerInput hold=default,int milliseconds=100)
    {
        Input(0,hold);connection.SendInput(0,press);
        await Task.Run(async()=>{await Task.Delay(milliseconds).ConfigureAwait(false);connection.SendInput(0,hold);});
        await Frames(1);
    }
    private async Task<long> NetworkPulse(NetworkSession connection,PlayerInput press,PlayerInput hold=default,int milliseconds=100)
    {
        if(milliseconds is <1 or >1000)throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if(!ReferenceEquals(_root.ActiveSession,connection))throw new InvalidOperationException("Input pulse requires its owned active session");
        int generation=connection.ConnectionGeneration;
        long allocatedAtStart=connection.InputSequence;
        Input(0,hold);
        long started=Stopwatch.GetTimestamp(),expires=started+(long)(Stopwatch.Frequency*milliseconds/1000d);
        // The engine thread checks the same expiry before republishing. A late
        // frame must never resurrect movement after the socket worker released it.
        _root.InjectInputProvider(0,()=>Stopwatch.GetTimestamp()<expires?press:hold);
        connection.SendInput(0,press);
        async Task<double> Release()
        {
            await Task.Delay(milliseconds).ConfigureAwait(false);
            if(connection.ConnectionGeneration==generation)connection.SendInput(0,hold); // thread-safe; no engine calls here
            return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        var released=Release();
        long sequence;
        try
        {
            double actual=await released;sequence=connection.InputSequence;
            await Frames(1);
            Event("network-input-pulse",new{requestedMs=milliseconds,releasedMs=actual,resumedMs=Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                allocatedAtStart,sequence,x=press.X,z=press.Z,primary=press.A,carry=press.B,holdCarry=hold.B});
        }
        finally
        {
            await released;
            if(ReferenceEquals(_root.ActiveSession,connection)&&connection.ConnectionGeneration==generation)Input(0,hold);
        }
        if(!ReferenceEquals(_root.ActiveSession,connection)||connection.ConnectionGeneration!=generation)
            throw new InvalidOperationException("Input pulse session changed before its observation");
        return sequence;
    }
    private async Task<long> NetworkNudge(NetworkSession connection,PlayerInput press,PlayerInput hold=default)
    {
        if(!ReferenceEquals(_root.ActiveSession,connection)||connection.Party?.Hosting is "peer" or "auto")
            throw new InvalidOperationException("Observed nudge requires its owned server-hosted session");
        int generation=connection.ConnectionGeneration;
        long started=Stopwatch.GetTimestamp();
        // Ordinary speed is 4.5 cells/s. Reserve .25 of the verified corridor
        // for scheduling/network jitter, then reject any final spatial overrun.
        long corridorTicks=(long)(Stopwatch.Frequency*(NetworkNudgeDistance-.25d)/4.5d);
        var gate=new ObservedInputNudge(press,generation,connection.LatestInputSend?.TotalSentSamples??0,
            started,Stopwatch.Frequency*5,corridorTicks);
        Input(0,hold);
        _root.InjectInputProvider(0,()=>gate.Pressed(connection.LatestInputSend,Stopwatch.GetTimestamp(),connection.ConnectionGeneration)?press:hold);
        connection.SendInput(0,press);
        var completion=new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer=new Thread(()=>
        {
            try
            {
                // No engine calls and no dependency on a rendered frame or the
                // thread-pool timer that may itself be delayed by a hitch.
                while(gate.Pressed(connection.LatestInputSend,Stopwatch.GetTimestamp(),connection.ConnectionGeneration))Thread.Sleep(1);
                if(connection.ConnectionGeneration==generation)connection.SendInput(0,hold);
                completion.TrySetResult(connection.InputSequence);
            }
            catch(Exception error)
            {
                gate.Cancel(Stopwatch.GetTimestamp());
                if(connection.ConnectionGeneration==generation)connection.SendInput(0,hold);
                completion.TrySetException(error);
            }
        }){IsBackground=true,Name="QA observed movement"};
        try
        {
            observer.Start();long sequence=await completion.Task;await Frames(1);
            var decision=gate.Decision!;
            Event("network-input-nudge",new{decision.Reason,decision.Delivered,
                decisionMs=Stopwatch.GetElapsedTime(started,decision.Timestamp).TotalMilliseconds,
                resumedMs=Stopwatch.GetElapsedTime(started).TotalMilliseconds,corridor=NetworkNudgeDistance,
                sequence,observation=decision.Observation,x=press.X,z=press.Z,holdCarry=hold.B});
            if(!decision.Delivered)throw new InvalidOperationException("Nudge delivery was not observed inside its corridor budget: "+decision.Reason);
            return sequence;
        }
        finally
        {
            gate.Cancel(Stopwatch.GetTimestamp());
            if(observer.IsAlive&&!observer.Join(1000))throw new TimeoutException("QA nudge observer did not stop");
            if(ReferenceEquals(_root.ActiveSession,connection)&&connection.ConnectionGeneration==generation)Input(0,hold);
        }
    }
    // The single owner of a game-loop-free fixture session: advance and drain it
    // like a 30 FPS client on the engine thread (Godot peer polling stays there),
    // never faster in uncapped headless frames. Messages go to onMessage; others
    // are consumed as GameRoot would. Cadence is kept for peer diagnostics.
    private sealed class FixtureLive:IDisposable
    {
        private readonly SceneTree _tree;
        private readonly NetworkSession _session;
        private readonly Action<JsonElement>? _onMessage;
        private readonly Stopwatch _paced=Stopwatch.StartNew(),_life=Stopwatch.StartNew();
        private bool _started,_active=true;
        private int _pumps;
        private double _maxGap;
        public FixtureLive(SceneTree tree,NetworkSession session,Action<JsonElement>? onMessage)
        {_tree=tree;_session=session;_onMessage=onMessage;_tree.ProcessFrame+=Pump;}
        private void Pump()
        {
            double gap=_paced.Elapsed.TotalSeconds;
            if(_started&&gap<1d/30)return;
            if(_started)_maxGap=Math.Max(_maxGap,gap);
            _started=true;_paced.Restart();_pumps++;
            _session.Advance(1d/30);
            foreach(var message in _session.DrainMessages())_onMessage?.Invoke(message);
        }
        public object Cadence=>new{pumps=_pumps,maxGapSeconds=Math.Round(_maxGap,3),aliveSeconds=Math.Round(_life.Elapsed.TotalSeconds,1)};
        public void Dispose(){if(_active){_tree.ProcessFrame-=Pump;_active=false;}}
    }
    private FixtureLive KeepFixtureLive(NetworkSession session,Action<JsonElement>? onMessage=null)=>new(GetTree(),session,onMessage);
    private void Event(string type,object data) => _trace.WriteLine(JsonSerializer.Serialize(new {frame=_frame,type,data}));
    private async Task Capture(string label,int settleFrames=2)
    {
        if(_headless) { Event("screenshot-disabled",new {label,reason="headless display"});return; }
        await Frames(settleFrames);
        async Task DrawFence() => await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var fence=DrawFence();
        if(await Task.WhenAny(fence,Task.Delay(500))!=fence)
        {
            if(OS.GetThreadCallerId()!=OS.GetMainThreadId())throw new InvalidOperationException("Render fallback must run on the engine main thread.");
            _captureFallbacks++;
            Event("render-fallback",new {label,frame=_frame,drawnFrames=Engine.GetFramesDrawn(),reason="post-draw timeout",mainThread=true});
            RenderingServer.ForceDraw(false,0);
            RenderingServer.ForceSync();
        }
        else await fence;
        var path=Path.Combine(_output,label+".png");
        using var pixels=GetViewport().GetTexture().GetImage();
        if(pixels.IsEmpty())throw new IOException("Renderer returned an empty capture");
        var colors=new HashSet<int>();float low=1,high=0;
        for(int y=0;y<pixels.GetHeight();y+=Math.Max(1,pixels.GetHeight()/18))
            for(int x=0;x<pixels.GetWidth();x+=Math.Max(1,pixels.GetWidth()/32))
            {var color=pixels.GetPixel(x,y);colors.Add(((int)(color.R*31)<<10)|((int)(color.G*31)<<5)|(int)(color.B*31));float light=(color.R+color.G+color.B)/3;low=Math.Min(low,light);high=Math.Max(high,light);}
        if(colors.Count<8||high-low<.05f)throw new IOException("Capture appears blank or unrendered: "+label);
        Event("capture-health",new {label,width=pixels.GetWidth(),height=pixels.GetHeight(),colors=colors.Count,contrast=high-low});
        var error=pixels.SavePng(path);
        if(error!=Error.Ok)throw new IOException($"Screenshot failed: {error}");
        _screenshots.Add(path);Event("screenshot",new {label,path});
    }
}

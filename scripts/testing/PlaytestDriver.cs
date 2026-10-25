using Godot;
using Bomber.Client;
using Bomber.Game;
using System.Diagnostics;
using System.Text.Json;
using BlastCircuit.App;
using BlastCircuit.Arena;
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
    private GameRoot _root = null!;
    private string _scenario = "all", _output = "", _server = "http://127.0.0.1:18496";
    private StreamWriter _trace = null!;
    private int _frame, _lastSnapshot = -1, _captureFallbacks;
    private readonly List<object> _checks = [];
    private readonly List<string> _screenshots = [];
    private readonly Dictionary<int, PlayerInput> _inputs = [];
    private readonly Stopwatch _clock = new();
    private bool _headless, _windowedRequested;

    public static bool TryAttach(GameRoot root)
    {
        var args = OS.GetCmdlineUserArgs();
        if (!args.Any(a => a.StartsWith("--playtest="))) return false;
        var driver = new PlaytestDriver { _root = root };
        root.AddChild(driver);
        return true;
    }

    public override async void _Ready()
    {
        var args=OS.GetCmdlineUserArgs();
        string Arg(string key,string fallback) => args.FirstOrDefault(a => a.StartsWith(key+"="))?[(key.Length+1)..] ?? fallback;
        _scenario=Arg("--playtest","all");
        _output=Path.GetFullPath(Arg("--playtest-output",ProjectSettings.GlobalizePath("res://artifacts/playtest")));
        _server=Arg("--playtest-server",_server);
        Directory.CreateDirectory(_output);
        _trace=new StreamWriter(Path.Combine(_output,"trace.jsonl")) { AutoFlush=true };
        _headless=DisplayServer.GetName()=="headless";
        _windowedRequested=args.Contains("--display-windowed");
        _clock.Start();
        Exception? failure=null;
        try
        {
            await Frames(3);
            if(!_headless)await WindowMode(_windowedRequested?Window.ModeEnum.Windowed:Window.ModeEnum.Fullscreen,"launch reaches requested display mode");
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
            var summary=new { schemaVersion=1,catalogSha256=_catalogHash,completedScenarios=_completedScenarios,scenario=_scenario,passed=failure==null,frames=_frame,seconds=_clock.Elapsed.TotalSeconds,
                assertions=_checks.Count,checks=_checks,screenshots=_screenshots,captureFallbacks=_captureFallbacks,screenshotMode=_headless?"disabled-headless":"graphical",
                failure=failure?.ToString(),screen=_root.ScreenName,snapshots=_root.SnapshotCount,finalState=_root.LatestState };
            File.WriteAllText(Path.Combine(_output,"summary.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions { WriteIndented=true }));
            _trace.Dispose();
            GD.Print($"PLAYTEST_{(failure==null?"PASS":"FAIL")} scenario={_scenario} frames={_frame} assertions={_checks.Count} screenshots={_screenshots.Count} output={_output}");
            _root.GetTree().Quit(failure==null?0:1);
        }
    }

    private async Task KeyPress(Key key,bool alt=false)
    {
        Godot.Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,AltPressed=alt,Pressed=true});
        await Frames(1);
        Godot.Input.ParseInputEvent(new InputEventKey {Keycode=key,PhysicalKeycode=key,AltPressed=alt,Pressed=false});
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
    private void Input(int seat,PlayerInput input) { _inputs[seat]=input;_root.InjectInput(seat,input);Event("input",new { seat,input.X,input.Z,input.A,input.B,input.R,input.Aim }); }
    private async Task Frames(int count)
    {
        for(var i=0;i<count;i++)
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); _frame++;
            if(_frame%30==0)ProgressCheckpoint();
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
            if(MathF.Sqrt(dx*dx+dz*dz)<tolerance)
            {
                Input(0,default);
                if(network)
                {
                    int stoppedAt=_root.SnapshotCount;
                    await WaitState(_=>_root.SnapshotCount>=stoppedAt+3,"network movement settles after release",120);
                    float sx=x-Player.GetProperty("x").GetSingle(),sz=z-Player.GetProperty("z").GetSingle();
                    Check(MathF.Sqrt(sx*sx+sz*sz)<.5f,$"network walk settles inside destination cell {x},{z}");
                }
                await Frames(3);Check(true,$"walk to {x},{z}");return;
            }
            Input(0,new PlayerInput(Math.Clamp(dx*4,-1,1),Math.Clamp(dz*4,-1,1)));
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

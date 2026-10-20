using Godot;
using BlastCircuit;
using Bomber.Client;
using Bomber.Game;
using System.Diagnostics;
using System.Text.Json;

namespace Bomber.Testing;

/// <summary>Runs real game input through GameRoot; never edits simulation or renderer state.</summary>
public partial class PlaytestDriver : Node
{
    private GameRoot _root = null!;
    private string _scenario = "all", _output = "", _server = "http://127.0.0.1:18496";
    private StreamWriter _trace = null!;
    private int _frame, _lastSnapshot = -1;
    private readonly List<object> _checks = [];
    private readonly List<string> _screenshots = [];
    private readonly Dictionary<int, PlayerInput> _inputs = [];
    private readonly Stopwatch _clock = new();
    private bool _headless;

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
        _clock.Start();
        Exception? failure=null;
        try
        {
            await Frames(3);
            Event("begin",new { scenario=_scenario,display=DisplayServer.GetName(),deterministicSeed=64 });
            var file=Arg("--playtest-file","");
            if(file.Length>0) await Script(file);
            else switch(_scenario)
            {
                case "menu": await Menus(); break;
                case "local-actions": await LocalActions(); break;
                case "ramp": await Ramps(); break;
                case "network": await Network(); break;
                case "combat": await Combat(); break;
                case "results": await Results(); break;
                case "all": await Menus(); await LocalActions(); await Ramps(); await Combat(); await Results(); await Network(); break;
                default: throw new ArgumentException("Unknown scenario: "+_scenario);
            }
        }
        catch(Exception error)
        {
            failure=error; Event("failure",new { error=error.ToString() }); GD.PushError(error.ToString());
            try { await Capture("failure"); } catch(Exception captureError) { Event("capture-error",new { error=captureError.Message }); }
        }
        finally
        {
            var summary=new { scenario=_scenario,passed=failure==null,frames=_frame,seconds=_clock.Elapsed.TotalSeconds,
                assertions=_checks.Count,checks=_checks,screenshots=_screenshots,screenshotMode=_headless?"disabled-headless":"graphical",
                failure=failure?.ToString(),screen=_root.ScreenName,snapshots=_root.SnapshotCount,finalState=_root.LatestState };
            File.WriteAllText(Path.Combine(_output,"summary.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions { WriteIndented=true }));
            _trace.Dispose();
            GD.Print($"PLAYTEST_{(failure==null?"PASS":"FAIL")} scenario={_scenario} frames={_frame} assertions={_checks.Count} screenshots={_screenshots.Count} output={_output}");
            _root.GetTree().Quit(failure==null?0:1);
        }
    }

    private async Task Menus()
    {
        _root.Navigate("Modes"); await Frames(3);
        Check(_root.ScreenName=="Modes","mode selector appears");
        await Capture("menu-modes");
        foreach(var label in new[] { "LOCAL BATTLE","ONLINE BATTLE","PRACTICE" })
        {
            await Click(label);
            var expected=label=="ONLINE BATTLE"?"Online":"Setup";
            Check(_root.ScreenName==expected,"focused activation "+label+" opens "+expected);
            await Capture("menu-"+label.ToLowerInvariant().Replace(' ','-'));
            await Click("BACK");
            Check(_root.ScreenName=="Modes","keyboard back returns to Modes");
        }
        foreach(var size in new[] {new Vector2I(1280,800),new Vector2I(1024,640)})
        {
            GetWindow().Size=size;await Frames(6);
            await MouseClick("LOCAL BATTLE");
            Check(_root.ScreenName=="Setup",$"mouse local button after resize {size}");
            await MouseClick("BACK");
            Check(_root.ScreenName=="Modes",$"mouse back button after resize {size}");
            await Capture($"menu-resized-{size.X}x{size.Y}");
        }
        GetWindow().Size=new Vector2I(1280,800);await Frames(3);
    }
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
        var target=FindButton(label);
        // Godot's documented canvas-local -> screen transform includes stretch and window placement.
        var center=target.GetViewport().GetScreenTransform()*target.GetGlobalTransformWithCanvas()*(target.Size/2);
        Event("mouse-click",new {label,x=center.X,y=center.Y,windowWidth=GetWindow().Size.X,windowHeight=GetWindow().Size.Y});
        Godot.Input.ParseInputEvent(new InputEventMouseMotion {Position=center,GlobalPosition=center});
        Godot.Input.ParseInputEvent(new InputEventMouseButton {Position=center,GlobalPosition=center,ButtonIndex=MouseButton.Left,Pressed=true});
        await Frames(1);
        Godot.Input.ParseInputEvent(new InputEventMouseButton {Position=center,GlobalPosition=center,ButtonIndex=MouseButton.Left,Pressed=false});
        await Frames(3);
    }
    private async Task LocalActions()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"practice reaches fight",150);
        Check(State.GetProperty("players").GetArrayLength()==1,"practice contains one human");
        await Capture("practice-start");
        Input(0,new PlayerInput(0,0,A:true)); await Frames(4);
        Check(State.GetProperty("bombs").GetArrayLength()==1,"bomb input creates authoritative bomb");
        Input(0,default); await Frames(4);
        Input(0,new PlayerInput(0,0,B:true)); await Frames(4);
        Check(Player.GetProperty("carrying").GetString()=="bomb","lift input carries bomb");
        Input(0,new PlayerInput(0,0,A:true,B:true)); await Frames(4);
        Check(State.GetProperty("bombs")[0].GetProperty("pumps").GetInt32()>=1,"pump input enlarges held bomb");
        await Capture("bomb-held-pumped");
        Input(0,new PlayerInput(1,0)); await Frames(4);
        Check(Player.GetProperty("carrying").ValueKind==JsonValueKind.Null,"release lift throws bomb");
        Check(State.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==3),"thrown bomb follows flight state");
        await Capture("bomb-flight");
        Input(0,default); _root.Back(); await Frames(4);
        Check(_root.ActiveSession==null,"leave practice disposes active session");
    }
    private async Task Ramps()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ramp practice reaches fight",150);
        Check(Math.Abs(Player.GetProperty("x").GetSingle()-10.5f)<.1,"temple spawn centered on staircase corridor");
        await MoveTo(10.5f,3.5f,200);
        Check(Player.GetProperty("y").GetSingle()>=4,"walked all three stair flights to crown tier4");
        await Capture("temple-crown");
        await MoveTo(10.5f,12.5f,200);
        Check(Player.GetProperty("y").GetSingle()<=1,"walked back down to base terrace tier1");
        await Capture("temple-descended");
        _root.Back(); await Frames(4);
    }
    private async Task Combat()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:3,Wins:1,RoundSeconds:60,SuddenDeath:false),1);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"combat reaches fight",150);
        var bombs=new HashSet<string>();var hazards=new HashSet<string>();var items=new HashSet<string>();
        bool roundOver=false,eliminated=false;
        for(var frame=0;frame<2200;frame++)
        {
            await Frames(1);
            if(_root.LatestState is not {} state)continue;
            foreach(var b in state.GetProperty("bombs").EnumerateArray())bombs.Add(b.GetProperty("id").GetString()!);
            foreach(var h in state.GetProperty("hazards").EnumerateArray())hazards.Add(h.GetProperty("id").GetString()!);
            foreach(var item in state.GetProperty("items").EnumerateArray())items.Add(item.GetProperty("id").GetString()!);
            eliminated|=state.GetProperty("players").EnumerateArray().Any(p=>!p.GetProperty("alive").GetBoolean()||p.GetProperty("ghost").GetBoolean());
            roundOver|=state.GetProperty("phase").GetInt32()==3 || _root.ScreenName=="Results";
            if(bombs.Count>0 && hazards.Count>0 && items.Count>0 && roundOver)break;
        }
        Event("combat-observed",new {bombs=bombs.Count,explosions=hazards.Count,pickups=items.Count,roundOver,eliminated});
        Check(bombs.Count>0,"combat observes server simulated bot bombs");
        Check(hazards.Count>0,"combat observes live explosions");
        Check(items.Count>0,"combat observes spawned item pickups through renderer");
        Check(roundOver,"combat observes completed round or match results");
        await Capture("combat-pickups-and-round");
        _root.Back();await Frames(3);
    }
    private async Task Results()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:1,Wins:1,Ghosts:false),1);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"results duel reaches fight",150);
        Input(0,new PlayerInput(0,0,A:true));await Frames(4);Input(0,default);
        Check(State.GetProperty("bombs").GetArrayLength()>0,"results duel places real self-elimination bomb");
        for(var i=0;i<600 && _root.ScreenName!="Results";i++)await Frames(1);
        Check(_root.ScreenName=="Results","real explosion eliminates human and completes CPU-won match");
        Check(State.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("bot").GetBoolean()&&p.GetProperty("wins").GetInt32()>=1),"CPU receives match-winning point");
        Check(VisibleText(_root).Contains("1 WINS",StringComparison.Ordinal),"results UI displays preserved winning point");
        await Capture("results-cpu-victory");
        await Click("PLAY AGAIN");await Frames(4);
        Check(_root.ScreenName=="Match" && _root.ActiveSession!=null,"results replay button starts fresh match");
        _root.Back();await Frames(3);
        Check(_root.ScreenName=="Modes" && _root.ActiveSession==null,"leave replay returns to mode selector");
    }
    private async Task Network()
    {
        await _root.StartOnlineAsync(new Uri(_server),"Harness host",new SessionSettings(Stage:"sunken-temple",Bots:0),NetworkMode.CreatePrivate);
        Check(_root.ActiveSession is NetworkSession,"engine uses product network adapter");
        var host=(NetworkSession)_root.ActiveSession!;
        Check(!string.IsNullOrWhiteSpace(host.JoinCode),"network host exposes join code");
        await using var peer=await NetworkSession.ConnectAsync(new Uri(_server),"Harness peer",new SessionSettings(Bots:0),NetworkMode.JoinCode,host.JoinCode);
        Check(peer.LobbyId==host.LobbyId,"second product client joins same kernel lobby");
        bool fullLobby=false;
        for(var i=0;i<180 && !fullLobby;i++)
        {
            await Frames(1);await Task.Delay(16);
            if(_root.LatestLobby is {} rawLobby)
            {
                var status=LobbyStatus.Parse(rawLobby);
                fullLobby=status.Players.Count==2 && status.Players.All(p=>p.Connected) && status.State=="countdown";
            }
        }
        Check(fullLobby,"engine lobby receives two connected humans and real countdown");
        Event("lobby",_root.LatestLobby!.Value);
        Check(_root.ScreenName=="Lobby","connected humans remain in actual countdown lobby");
        var lobbyText=VisibleText(_root);
        Event("lobby-ui",new {text=lobbyText});
        Check(lobbyText.Contains("Harness host",StringComparison.Ordinal),"lobby renders host name in visible UI");
        Check(lobbyText.Contains("Harness peer",StringComparison.Ordinal),"lobby renders peer name in visible UI");
        Check(lobbyText.Contains("BATTLE BEGINS IN",StringComparison.Ordinal),"lobby renders real countdown text");
        await Capture("network-lobby");
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1 && s.GetProperty("players").GetArrayLength()==2,"engine receives two-human authoritative fight",900);
        var before=Player.GetProperty("z").GetSingle();
        Input(0,new PlayerInput(0,-1,A:true));
        bool observed=false;
        for(var i=0;i<150 && !observed;i++)
        {
            await Frames(1);await Task.Delay(16);
            foreach(var message in peer.DrainMessages())
                if(message.GetProperty("t").GetString()=="state")
                {
                    var d=message.GetProperty("d");
                    observed=d.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId && Math.Abs(p.GetProperty("z").GetSingle()-before)>.15);
                }
        }
        Check(observed,"injected engine movement observed by second network client");
        Input(0,default); await Capture("network-two-humans");
        _root.Back(); await Frames(4);
    }
    private static string VisibleText(Node node)
    {
        var parts=new List<string>();
        if(node is Label label && label.IsVisibleInTree())parts.Add(label.Text);
        foreach(var child in node.GetChildren())parts.Add(VisibleText(child));
        return string.Join("\n",parts.Where(p=>p.Length>0));
    }
    private JsonElement State => _root.LatestState ?? throw new InvalidOperationException("No authoritative state yet.");
    private JsonElement Player => State.GetProperty("players").EnumerateArray().OrderBy(p=>p.GetProperty("slot").GetInt32()).First();
    private void Input(int seat,PlayerInput input) { _inputs[seat]=input;_root.InjectInput(seat,input);Event("input",new { seat,input.X,input.Z,input.A,input.B,input.R }); }
    private async Task Frames(int count)
    {
        for(var i=0;i<count;i++)
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); _frame++;
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
        for(var i=0;i<maxFrames;i++)
        {
            var dx=x-Player.GetProperty("x").GetSingle();var dz=z-Player.GetProperty("z").GetSingle();
            if(MathF.Sqrt(dx*dx+dz*dz)<.15) { Input(0,default);await Frames(3);Check(true,$"walk to {x},{z}");return; }
            Input(0,new PlayerInput(Math.Clamp(dx*4,-1,1),Math.Clamp(dz*4,-1,1)));
            await Frames(1);
        }
        Input(0,default); Check(false,$"walk to {x},{z}: timed out at {Player}");
    }
    private void Check(bool passed,string description)
    {
        _checks.Add(new { description,passed,frame=_frame }); Event("assert",new {description,passed});
        if(!passed)throw new InvalidOperationException("Assertion failed: "+description);
    }
    private void Event(string type,object data) => _trace.WriteLine(JsonSerializer.Serialize(new {frame=_frame,type,data}));
    private async Task Capture(string label)
    {
        if(_headless) { Event("screenshot-disabled",new {label,reason="headless display"});return; }
        await Frames(2);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var path=Path.Combine(_output,label+".png");
        var error=GetViewport().GetTexture().GetImage().SavePng(path);
        if(error!=Error.Ok)throw new IOException($"Screenshot failed: {error}");
        _screenshots.Add(path);Event("screenshot",new {label,path});
    }
    private async Task Script(string path)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(path));
        foreach(var step in document.RootElement.GetProperty("steps").EnumerateArray())
        {
            var op=step.GetProperty("op").GetString();Event("step",step);
            int Number(string key,int fallback) => step.TryGetProperty(key,out var p)?p.GetInt32():fallback;
            float Scalar(string key,float fallback=0) => step.TryGetProperty(key,out var p)?p.GetSingle():fallback;
            bool Flag(string key) => step.TryGetProperty(key,out var p)&&p.GetBoolean();
            switch(op)
            {
                case "navigate":_root.Navigate(step.GetProperty("screen").GetString()!);break;
                case "back":_root.Back();break;
                case "click":await Click(step.GetProperty("label").GetString()!);break;
                case "mouse-click":await MouseClick(step.GetProperty("label").GetString()!);break;
                case "resize":GetWindow().Size=new Vector2I(Number("width",1280),Number("height",800));await Frames(6);break;
                case "start-local":_root.StartLocal(new SessionSettings(Stage:step.GetProperty("stage").GetString()!,Bots:Number("bots",0)),Number("humans",1),Flag("practice"));break;
                case "frames":await Frames(Number("count",1));break;
                case "input":Input(Number("seat",0),new PlayerInput(Scalar("x"),Scalar("z"),Flag("a"),Flag("b"),Flag("r")));break;
                case "move-to":await MoveTo(Scalar("x"),Scalar("z"),Number("maxFrames",300));break;
                case "capture":await Capture(step.GetProperty("label").GetString()!);break;
                case "assert-screen":Check(_root.ScreenName==step.GetProperty("screen").GetString(),"script screen "+step.GetProperty("screen"));break;
                case "assert-state":
                    var value=State;foreach(var segment in step.GetProperty("path").GetString()!.Split('.'))value=int.TryParse(segment,out var index)?value[index]:value.GetProperty(segment);
                    Check(step.TryGetProperty("equals",out var equal)?value.ToString()==equal.ToString():value.GetDouble()>=step.GetProperty("gte").GetDouble(),"script state "+step.GetProperty("path"));break;
                default:throw new ArgumentException("Unknown script op: "+op);
            }
        }
    }
}

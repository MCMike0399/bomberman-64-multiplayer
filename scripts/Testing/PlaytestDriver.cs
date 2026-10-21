using Godot;
using BlastCircuit;
using Bomber.Client;
using Bomber.Game;
using System.Diagnostics;
using System.Text.Json;

namespace Bomber.Testing;

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

    private async Task Menus()
    {
        _root.Navigate("Modes"); await Frames(3);
        Check(_root.ScreenName=="Modes","mode selector appears");
        await Capture("menu-modes");
        foreach(var label in new[] { "LOCAL BATTLE","ONLINE BATTLE","PRACTICE" })
        {
            await Click(label);
            var expected=label=="LOCAL BATTLE"?"Roster":label=="ONLINE BATTLE"?"Online":"Setup";
            Check(_root.ScreenName==expected,"focused activation "+label+" opens "+expected);
            await Capture("menu-"+label.ToLowerInvariant().Replace(' ','-'));
            await Click("BACK");
            Check(_root.ScreenName=="Modes","keyboard back returns to Modes");
        }
        bool restoreFullscreen=!_headless&&GetWindow().Mode==Window.ModeEnum.Fullscreen;
        if(restoreFullscreen){await KeyPress(Key.F11);await WindowMode(Window.ModeEnum.Windowed,"menu temporarily enters windowed resize QA");}
        foreach(var size in new[] {new Vector2I(1280,720),new Vector2I(1280,800),new Vector2I(1024,640)})
        {
            await ResizeWindow(size);
            await MouseClick("LOCAL BATTLE");
            Check(_root.ScreenName=="Roster",$"mouse local button after resize {size}");
            await MouseClick("BACK");
            Check(_root.ScreenName=="Modes",$"mouse back button after resize {size}");
            await Capture($"menu-resized-{size.X}x{size.Y}");
        }
        await ResizeWindow(new Vector2I(1280,800));
        if(restoreFullscreen){await KeyPress(Key.F11);await WindowMode(Window.ModeEnum.Fullscreen,"menu restores fullscreen after resize QA");}
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
        var previous=GetViewport().GetScreenTransform();bool stable=false;
        while(wall.Elapsed.TotalSeconds<4)
        {
            await Frames(1);await Task.Delay(20);
            var transform=GetViewport().GetScreenTransform();
            if(GetWindow().Size==size&&transform.IsEqualApprox(previous))
            {
                stableSince??=wall.Elapsed.TotalSeconds;
                if(wall.Elapsed.TotalSeconds-stableSince.Value>=.2){stable=true;break;}
            }
            else stableSince=null;
            previous=transform;
        }
        Check(stable,"native window size and input transform settle: "+size);
    }
    private Button NamedButton(string name) => _root.FindChild(name,true,false) as Button ?? throw new InvalidOperationException("Button not found: "+name);
    private async Task ActivateNamed(string name)
    {
        var button=NamedButton(name);
        Check(button.IsVisibleInTree()&&!button.Disabled,"named button visible: "+name);
        button.GrabFocus();await Frames(1);await KeyPress(Key.Enter);
    }
    private async Task SelectRole(string name,int index)
    {
        var option=NamedButton(name) as OptionButton??throw new InvalidOperationException("Role control is not an OptionButton.");
        await ActivateNamed(name);
        var popup=option.GetPopup();Check(popup.Visible,"real role popup opens: "+name);
        async Task PopupKey(Key key)
        {
            Godot.Input.ParseInputEvent(new InputEventKey {WindowId=popup.GetWindowId(),Keycode=key,PhysicalKeycode=key,Pressed=true});await Frames(1);
            Godot.Input.ParseInputEvent(new InputEventKey {WindowId=popup.GetWindowId(),Keycode=key,PhysicalKeycode=key,Pressed=false});await Frames(2);
            Event("popup-key",new {key=key.ToString(),window=popup.GetWindowId(),focused=popup.GetFocusedItem(),visible=popup.Visible});
        }
        int current=popup.GetFocusedItem();
        if(current<0)throw new InvalidOperationException("Role popup has no focused item.");
        for(int i=0;i<Math.Abs(index-current);i++)await PopupKey(index<current?Key.Up:Key.Down);
        Check(popup.GetFocusedItem()==index,"arrow input focuses requested role item");
        await PopupKey(Key.Enter);await Frames(2);
    }
    private async Task Fullscreen()
    {
        Check(!_headless,"fullscreen mode verification requires a graphical display");
        await WindowMode(Window.ModeEnum.Fullscreen,"normal launch defaults to fullscreen");
        await Capture("fullscreen-default");
        await KeyPress(Key.F11);await WindowMode(Window.ModeEnum.Windowed,"F11 switches to windowed");
        await KeyPress(Key.F11);await WindowMode(Window.ModeEnum.Fullscreen,"F11 restores fullscreen");
        await KeyPress(Key.Enter,true);await WindowMode(Window.ModeEnum.Windowed,"Alt+Enter switches to windowed");
        await KeyPress(Key.Enter,true);await WindowMode(Window.ModeEnum.Fullscreen,"Alt+Enter restores fullscreen");
        await Capture("fullscreen-restored");
    }
    private async Task WindowMode(Window.ModeEnum expected,string description)
    {
        var wall=Stopwatch.StartNew();double? stableSince=null;bool stable=false;
        while(wall.Elapsed.TotalSeconds<12)
        {
            if(GetWindow().Mode==expected)
            {
                stableSince??=wall.Elapsed.TotalSeconds;
                if(wall.Elapsed.TotalSeconds-stableSince.Value>=.5){stable=true;break;}
            }
            else stableSince=null;
            await Frames(1);await Task.Delay(20);
        }
        Event("window-mode",new {expected=expected.ToString(),actual=GetWindow().Mode.ToString(),stable,window=GetWindow().Size.ToString()});
        Check(stable,description+" (stable500ms)");
    }
    private async Task Framing()
    {
        async Task VerifyStages(string view)
        {
            foreach(var stage in new[] {"sunken-temple","rock-garden","up-and-down"})
            {
                _root.StartLocal(new SessionSettings(Stage:stage,Bots:0),1,practice:true);await Frames(10);
                Check(_root.LatestStage is not null,"camera check receives stage "+stage);
                CheckFraming(stage,GetWindow().Size);
                await Capture($"framing-{stage}-{view}");_root.Back();await Frames(3);
            }
        }
        bool restoreFullscreen=!_headless&&!_windowedRequested;
        if(restoreFullscreen)
        {
            await WindowMode(Window.ModeEnum.Fullscreen,"camera QA begins in native fullscreen");
            await VerifyStages("native-fullscreen");
            await KeyPress(Key.F11);await WindowMode(Window.ModeEnum.Windowed,"camera temporarily enters windowed aspect QA");
        }
        else if(!_headless)await WindowMode(Window.ModeEnum.Windowed,"explicit windowed QA override is honored");
        else Event("display-check-not-applicable",new {reason="headless has no native fullscreen window"});
        foreach(var size in new[] {new Vector2I(1280,720),new Vector2I(1280,800)})
        {
            await ResizeWindow(size);await VerifyStages($"{size.X}x{size.Y}");
        }
        await ResizeWindow(new Vector2I(1280,800));
        if(restoreFullscreen){await KeyPress(Key.F11);await WindowMode(Window.ModeEnum.Fullscreen,"camera restores fullscreen after aspect QA");}
    }
    private void CheckFraming(string stage,Vector2I window)
    {
        var camera=_root.GetViewport().GetCamera3D()??throw new InvalidOperationException("No active arena camera.");
        var data=_root.LatestStage!.Value;var cells=data.GetProperty("cells");int width=data.GetProperty("width").GetInt32();
        var viewport=_root.GetViewport().GetVisibleRect().Size;
        float minX=1,minY=1,maxX=0,maxY=0;int points=0;bool behind=false;
        for(var i=0;i<cells.GetArrayLength();i++)
        {
            var cell=cells[i];int kind=cell.GetProperty("k").GetInt32();if(kind is 2 or 3)continue;
            float y=kind==4?cell.GetProperty("rt").GetSingle():cell.GetProperty("t").GetSingle();
            foreach(float dx in new[]{0f,1f})foreach(float dz in new[]{0f,1f})foreach(float head in new[]{0f,kind==1?1.21f:2.1f})
            {
                var world=new Vector3(i%width+dx,y*1.35f+head,i/width+dz);
                behind|=camera.IsPositionBehind(world);var p=camera.UnprojectPosition(world)/viewport;
                minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);minY=Math.Min(minY,p.Y);maxY=Math.Max(maxY,p.Y);points++;
            }
        }
        Event("camera-framing",new {stage,window=window.ToString(),viewport=viewport.ToString(),points,minX,maxX,minY,maxY,behind});
        Check(!behind,"all playable geometry is in front of camera: "+stage);
        Check(minX>=.02f&&maxX<=.98f,$"stage and character extents fit horizontal view: {stage} {window}");
        Check(minY>=.14f&&maxY<=.90f,$"stage and character extents avoid HUD: {stage} {window}");
    }
    private JsonElement Roster => JsonSerializer.SerializeToElement(_root.RosterState);
    private async Task RosterSelection()
    {
        _root.Navigate("Modes");await Frames(3);await Click("LOCAL BATTLE");
        Check(_root.ScreenName=="Roster","local battle opens animated player selection");
        for(int i=0;i<8;i++)_root.ConfigureRosterSlot(i,"off",i);
        _root.ConfigureRosterSlot(0,"human",5);_root.ConfigureRosterSlot(2,"cpu",1);_root.ConfigureRosterSlot(5,"human",3);_root.ConfigureRosterSlot(7,"cpu",6);await Frames(4);
        Check(Roster.GetProperty("humans").GetInt32()==2&&Roster.GetProperty("cpu").GetInt32()==2,"mixed roster supports gaps without losing counts");
        await ActivateNamed("Slot0Appearance");
        Check(Roster.GetProperty("slots")[0].GetProperty("appearance").GetInt32()==6,"real appearance button changes selected suit");
        Check(Roster.GetProperty("slots")[0].GetProperty("previewAppearance").GetInt32()==6,"visible shared-stage model uses chosen appearance");
        Check(Roster.GetProperty("sharedStage").GetBoolean()&&Roster.GetProperty("slots")[0].GetProperty("previewVisible").GetBoolean(),"roster diagnostics refer to visible shared-stage characters");
        // Two samples can coincide on opposite sides of a contact even while the
        // character is walking. Observe most of a stride, including both reaches.
        var initialPose=Roster.GetProperty("slots")[0];
        double beforeClock=initialPose.GetProperty("animationTime").GetDouble();
        var poses=new List<float> {initialPose.GetProperty("walkPose").GetSingle()};
        for(int i=0;i<24;i++){await Frames(1);poses.Add(Roster.GetProperty("slots")[0].GetProperty("walkPose").GetSingle());}
        float minimumPose=poses.Min(),maximumPose=poses.Max();
        double elapsedAnimation=Roster.GetProperty("slots")[0].GetProperty("animationTime").GetDouble()-beforeClock;
        Event("roster-walking-excursion",new {selected=Roster.GetProperty("selected").GetInt32(),minimumPose,maximumPose,elapsedAnimation,poses});
        Check(minimumPose<-.15f&&maximumPose>.15f&&maximumPose-minimumPose>.4f,"selected 3D character actually changes walking pose");
        Check(elapsedAnimation>.5,"visible roster animation clock advances while the selected character walks");
        await Capture("roster-selected-walking");
        await SelectRole("Slot2Role",0);
        Check(Roster.GetProperty("slots")[2].GetProperty("role").GetString()=="off","real role popup changes CPU seat to off");
        await SelectRole("Slot2Role",2);
        Check(Roster.GetProperty("slots")[2].GetProperty("role").GetString()=="cpu","real role popup restores CPU seat");
        await Capture("roster-mixed-ready");
        await Click("CONTINUE TO RULES");Check(_root.ScreenName=="Setup","roster continues to match rules");
        await Click("Start Battle");
        await WaitState(s=>s.GetProperty("players").GetArrayLength()==4,"roster starts four-contender match",150);
        var packed=State.GetProperty("players").EnumerateArray().OrderBy(p=>p.GetProperty("slot").GetInt32()).ToArray();
        Check(packed.Select(p=>p.GetProperty("appearance").GetInt32()).SequenceEqual(new[]{6,3,1,6}),"noncontiguous roster appearances reach packed human and CPU authority slots");
        Check(packed.Count(p=>p.GetProperty("bot").GetBoolean())==2,"selected CPU roles reach match");
        await Capture("roster-selected-match");_root.Back();await Frames(3);
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
    private JsonElement Visual => JsonSerializer.SerializeToElement(_root.VisualState);
    private async Task Powerups()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"training pickups reach fight",150);
        var initialCapacity=Player.GetProperty("bombs").GetInt32();
        Check(Visual.GetProperty("powerups").GetInt32()>=6,"six training pickup models visible");
        await Capture("powerups-visible-cache");
        await MoveTo(8.5f,12.5f,80);
        Check(Player.GetProperty("fire").GetInt32()==2,"walking onto first flame increases fire level");
        await MoveTo(7.5f,12.5f,60);
        Check(Player.GetProperty("fire").GetInt32()==3,"second flame reaches regular fire cap3");
        await MoveTo(12.5f,12.5f,120);
        Check(Player.GetProperty("bombs").GetInt32()==initialCapacity+1,"walking onto bomb increases capacity by exactly one");
        await MoveTo(12.5f,13.5f,60);
        Check(Player.GetProperty("red").GetBoolean()&&!Player.GetProperty("remote").GetBoolean(),"walking onto red power selects red bomb mode");
        await MoveTo(10.5f,13.5f,80);
        Check(Player.GetProperty("heart").GetBoolean(),"walking onto heart grants extra hit");
        await MoveTo(8.5f,13.5f,80);
        Check(Player.GetProperty("remote").GetBoolean()&&!Player.GetProperty("red").GetBoolean(),"walking onto remote replaces red with command detonation");
        Check(Player.GetProperty("pickupSerial").GetInt64()>=6,"six real pickups accepted by authority");
        Check(Visual.GetProperty("powerups").GetInt32()==0,"collected pickup models disappear");
        await Capture("powerups-collected-hud");
        Input(0,new PlayerInput(0,0,A:true));await Frames(4);Input(0,default);await Frames(4);
        Check(State.GetProperty("bombs").EnumerateArray().Any(b=>!b.GetProperty("red").GetBoolean()&&b.GetProperty("remote").GetBoolean()),"latest remote pickup determines newly placed bomb mode");
        await Capture("powerups-remote-bomb");
        Input(0,new PlayerInput(0,0,R:true));await Frames(4);Input(0,default);await Frames(4);
        Check(State.GetProperty("hazards").GetArrayLength()>0,"remote action detonates upgraded bomb");
        // In-battle HUD buttons deliberately stay out of D-pad focus navigation.
        await MouseClick("RESET");await Frames(4);
        Check(Player.GetProperty("fire").GetInt32()==1&&Player.GetProperty("bombs").GetInt32()==initialCapacity,"practice reset restores baseline upgrades");
        Check(Player.GetProperty("pickupSerial").GetInt64()==0&&Visual.GetProperty("powerups").GetInt32()>=6,"practice reset restores visible training cache");
        await Capture("powerups-practice-reset");
        _root.Back();await Frames(3);
    }
    private async Task PumpGrowth()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"pump training reaches fight",150);
        Input(0,new PlayerInput(0,0,B:true));await Frames(2);
        Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(4);
        Check(Player.GetProperty("carrying").GetString()=="bomb","hold lift and press bomb creates carried bomb");
        Input(0,new PlayerInput(0,0,B:true));await Frames(12);
        float previous=Visual.GetProperty("largestBombScale").GetSingle();
        float baseScale=previous;
        await Capture("pump-stage-0");
        for(var charge=1;charge<=3;charge++)
        {
            Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(4);
            Check(Visual.GetProperty("pumpIntensity").GetSingle()>.1f,$"body squash pose is active during pump{charge}");
            await Capture($"pump-action-{charge}");
            Input(0,new PlayerInput(0,0,B:true));await Frames(12);
            Check(State.GetProperty("bombs")[0].GetProperty("pumps").GetInt32()==charge,$"accepted pump stage{charge}");
            var scale=Visual.GetProperty("largestBombScale").GetSingle();
            Check(scale<=2.3f,$"pumped bomb stays within readable size limit at stage{charge}");
            Check(scale>previous+.2f,$"rendered bomb grows at pump stage{charge}");previous=scale;
            Event("pump-visual",new {charge,scale,visual=Visual});
            await Capture($"pump-stage-{charge}");
        }
        Check(Visual.GetProperty("carriedBombs").GetInt32()==1,"charged model remains attached to carrier socket");
        Check(previous>baseScale*1.7f,"fully charged bomb stays visibly larger than its unpumped model");
        Check(Visual.GetProperty("pumpAnimations").GetInt32()>=3,"three accepted pumps trigger body animation");
        Check(Player.GetProperty("pumpSerial").GetInt64()>=3,"authority publishes three accepted pump events");
        Input(0,new PlayerInput(1,0));await Frames(4);
        Check(State.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==3&&b.GetProperty("pumps").GetInt32()==3),"release throws fully charged bomb");
        await Capture("pump-charged-flight");
        _root.Back();await Frames(3);
    }
    private async Task HurryWater()
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0,Wins:3,RoundSeconds:60,SuddenDeath:true,Ghosts:false),2);
        Input(1,default);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==2,"one-minute temple round enters hurry",180);
        Check(State.GetProperty("hurryMode").GetString()=="water","temple sudden death selects water mode");
        float initial=Visual.GetProperty("waterHeight").GetSingle();
        await MoveTo(10.5f,3.5f,200);
        Check(Player.GetProperty("y").GetSingle()>=4,"human climbs to flood-safe crown using inputs");
        await Capture("hurry-water-highground");
        await WaitState(s=>s.GetProperty("waterLevel").GetSingle()>=.25f,"rising water submerges lowest court",3000);
        Check(Visual.GetProperty("waterHeight").GetSingle()>initial+.5f,"rendered water plane rises with authority");
        Check(State.GetProperty("closedLeft").GetInt32()==0&&State.GetProperty("closedRight").GetInt32()==0,"temple does not shrink by closing columns");
        await Capture("hurry-water-court-flooded");
        await WaitState(s=>s.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("slot").GetInt32()==1&&!p.GetProperty("alive").GetBoolean()),"rising water eliminates lower-terrace human",3000);
        Check(Player.GetProperty("alive").GetBoolean()&&Player.GetProperty("y").GetSingle()>=4,"highground human survives lower terrace flooding");
        Check(State.GetProperty("waterLevel").GetSingle()>=1,"drowning occurs after water reaches low terrace height");
        await Capture("hurry-water-low-out-high-safe");
        _root.Back();await Frames(3);
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
        Check((_root.FindChild("Standings",true,false) as Label)!.Text.Contains("1 WIN",StringComparison.Ordinal),"results UI displays preserved winning point");
        var winner=State.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("bot").GetBoolean());
        var preview=(_root.FindChild("WinnerPreview",true,false) as BlastCircuit.UI.CharacterPreview)!;
        Check(preview.Appearance==winner.GetProperty("appearance").GetInt32()&&preview.Celebrating,"results celebrate the actual winner's selected suit");
        await Frames(12);
        var replayButton=VisibleButton("Play Again");var modeButton=VisibleButton("Mode Select");
        replayButton.GrabFocus();await KeyPress(Key.D);Check(modeButton.HasFocus(),"D moves across Results actions");
        await KeyPress(Key.A);Check(replayButton.HasFocus(),"A returns to the Results replay action");
        await Capture("results-cpu-victory");
        await Click("PLAY AGAIN");await Frames(4);
        Check(_root.ScreenName=="Match" && _root.ActiveSession!=null,"results replay button starts fresh match");
        _root.Back();await Frames(3);
        Check(_root.ScreenName=="Modes" && _root.ActiveSession==null,"leave replay returns to mode selector");
    }
    private async Task Network()
    {
        _root.Navigate("Online");_root.SetOnlineAppearance(3);await Frames(3);
        await ActivateNamed("OnlineAppearance");
        Check(_root.SelectedAppearance==4,"online suit button selects the host appearance");
        await _root.StartOnlineAsync(new Uri(_server),"Harness host",new SessionSettings(Stage:"sunken-temple",Bots:0),NetworkMode.CreatePrivate,appearance:_root.SelectedAppearance);
        Check(_root.ActiveSession is NetworkSession,"engine uses product network adapter");
        var host=(NetworkSession)_root.ActiveSession!;
        Check(!string.IsNullOrWhiteSpace(host.JoinCode),"network host exposes join code");
        await using var peer=await NetworkSession.ConnectAsync(new Uri(_server),"Harness peer",new SessionSettings(Bots:0),NetworkMode.JoinCode,host.JoinCode,appearance:6);
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
        Check(lobbyText.Contains("Battle begins in",StringComparison.Ordinal),"lobby renders real countdown text");
        await Capture("network-lobby");
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1 && s.GetProperty("players").GetArrayLength()==2,"engine receives two-human authoritative fight",900);
        await WaitState(s=>s.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId&&p.GetProperty("appearance").GetInt32()==4)&&s.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==peer.PlayerId&&p.GetProperty("appearance").GetInt32()==6),"network authority reflects both chosen appearances",180);
        Check(State.GetProperty("hurryMode").GetString()=="water","network server supplies temple-specific water sudden death");
        Check(State.TryGetProperty("waterLevel",out var networkWater)&&networkWater.ValueKind==JsonValueKind.Number,"network server publishes authoritative water height");
        Check(State.TryGetProperty("presentationSeq",out _)&&State.TryGetProperty("events",out _),"network server publishes accepted presentation events");
        int bombCueBefore=_root.BattleAudio.Counts.GetValueOrDefault("bomb");
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
        Input(0,default); await Frames(4);
        Check(_root.BattleAudio.Counts.GetValueOrDefault("bomb")==bombCueBefore+1,"network bomb event plays once across repeated snapshots");
        await Capture("network-two-humans");
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

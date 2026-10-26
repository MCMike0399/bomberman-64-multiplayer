using Godot;
using System.Text.Json;
using Bomber.Client.Sessions;
using System.Diagnostics;
namespace BlastCircuit.Testing;

/// <summary>Menu navigation, display modes, battle framing and local roster selection journeys.</summary>
public partial class PlaytestDriver
{
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
            foreach(var stage in new[] {"sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap"})
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
        for(var i=0;i<cells.GetArrayLength();i++)Sample(cells[i],i%width,i/width);
        void Sample(JsonElement cell,int x,int z)
        {
            if(cell.TryGetProperty("below",out var lower)&&lower.ValueKind==JsonValueKind.Object)Sample(lower,x,z);
            int kind=cell.GetProperty("k").GetInt32();if(kind is 2 or 3)return;
            float y=kind==4?cell.GetProperty("rt").GetSingle():cell.GetProperty("t").GetSingle();
            foreach(float dx in new[]{0f,1f})foreach(float dz in new[]{0f,1f})foreach(float head in new[]{0f,kind==1?(cell.TryGetProperty("oh",out var crown)&&crown.ValueKind==JsonValueKind.Number?crown.GetSingle()*1.35f:1.21f):2.1f})
            {
                var world=new Vector3(x+dx,y*1.35f+head,z+dz);
                behind|=camera.IsPositionBehind(world);var p=camera.UnprojectPosition(world)/viewport;
                minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);minY=Math.Min(minY,p.Y);maxY=Math.Max(maxY,p.Y);points++;
            }
        }
        Event("camera-framing",new {stage,window=window.ToString(),viewport=viewport.ToString(),points,minX,maxX,minY,maxY,behind});
        Check(!behind,"all playable geometry is in front of camera: "+stage);
        Check(minX>=.02f&&maxX<=.98f,$"stage and character extents fit horizontal view: {stage} {window}");
        Check(minY>=.14f&&maxY<=.90f,$"stage and character extents avoid HUD: {stage} {window}");
    }

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
}

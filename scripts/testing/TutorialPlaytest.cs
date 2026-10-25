using Godot;
using Bomber.Game;
using BlastCircuit.App;
using BlastCircuit.InputDevices;
using BlastCircuit.UI.Hud;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task TutorialTour()
    {
        _root.Navigate("Modes");await Frames(4);await Click("Tutorial");
        Check(_root.ScreenName=="Tutorial","mode button opens dedicated how-to section");
        await Capture("tutorial-browser");
        async Task Lesson(string id)
        {
            if(_root.ScreenName!="Tutorial")_root.Navigate("Tutorial");
            await Frames(4);await ActivateNamed("Lesson_"+id);
            await WaitState(s=>s.TryGetProperty("tutorial",out var t)&&t.ValueKind==System.Text.Json.JsonValueKind.Object&&t.GetProperty("id").GetString()==id&&s.GetProperty("phase").GetInt32()!=0,"lesson launches through menu: "+id,150);
            Check(_root.LatestStage?.GetProperty("id").GetString()=="circuit-garden","tutorial owns a separate authored arena");
        }
        bool Cleared()=>State.GetProperty("tutorial").GetProperty("complete").GetBoolean();
        await Lesson("move");
        await Capture("tutorial-garden-move");
        for(int frame=0;frame<70&&!Cleared();frame++){Input(0,new(1,0));await Frames(1);}
        Input(0,default);await Frames(3);
        Check(Cleared(),"movement clears from actual accepted travel");
        Check(_root.Preferences.CompletedTutorials.Contains("move"),"clear mark saved in device preferences");
        Check(PlayerPreferences.Load(_root.PreferencesPath).CompletedTutorials.Contains("move"),"clear mark survives preferences reload");
        await Capture("tutorial-lesson-clear");
        await ActivateNamed("TutorialNext");
        await WaitState(s=>s.GetProperty("tutorial").GetProperty("id").GetString()=="bomb"&&s.GetProperty("phase").GetInt32()==1,"next enters bomb lesson",150);
        Input(0,new(0,0,A:true));await Frames(2);Input(0,default);await Frames(90);
        Check(State.GetProperty("tutorial").GetProperty("failed").GetBoolean(),"being caught fails the safety objective");
        await Capture("tutorial-retry");
        await ActivateNamed("TutorialRetry");
        await WaitState(s=>!s.GetProperty("tutorial").GetProperty("failed").GetBoolean()&&s.GetProperty("phase").GetInt32()==1,"retry rebuilds a clean lesson",150);
        Input(0,new(0,0,A:true));await Frames(2);Input(0,new(-1,0));await Frames(25);Input(0,default);await Frames(90);
        Check(Cleared(),"plant and retreat clears after the real blast expires");
        if(!_headless)
        {
            var hub=ControllerHub.Current;
            try
            {
                foreach(var pad in QaPads)
                {
                    CtlReleaseAll();hub.OverrideDevicesForPlaytest([pad]);await Lesson("carry");
                    await ControllerFocusReady();_root.InjectInput(0,null);
                    var family=ControllerProfile.Family(pad.Name);
                    CtlButton(pad.RuntimeId,JoyButton.LeftShoulder,true);await ControllerFrames(3);
                    CtlButton(pad.RuntimeId,ControllerProfile.BombButton(family),true);await ControllerFrames(3);
                    CtlButton(pad.RuntimeId,ControllerProfile.BombButton(family),false);await ControllerFrames(3);
                    Check(Player.GetProperty("carrying").GetString()=="bomb",pad.Name+": shoulder and primary ready the real tutorial bomb");
                    Check(VisibleText(_root).Contains("Hold "+ControllerProfile.ButtonName(family,JoyButton.LeftShoulder)),pad.Name+": instructions use active family labels");
                    await Capture("tutorial-carry-"+family);
                    CtlButton(pad.RuntimeId,JoyButton.LeftShoulder,false);await ControllerFrames(5);
                    Check(Cleared(),pad.Name+": real throw clears tutorial");
                    CtlButton(pad.RuntimeId,ControllerProfile.ConfirmButton(family),true);await ControllerFrames(2);
                    CtlButton(pad.RuntimeId,ControllerProfile.ConfirmButton(family),false);await ControllerFrames(3);
                    Check(State.GetProperty("tutorial").GetProperty("id").GetString()=="primary-hold",pad.Name+": controller confirms Next lesson");
                }
            }
            finally {CtlReleaseAll();hub.RestorePhysicalDevicesForPlaytest();}
        }
        await Lesson("red");await MoveTo(9.5f,12.5f,100);await MoveTo(11.0f,12.5f,100);
        Input(0,new(0,0,A:true));await Frames(2);Input(0,new(-1,0));await Frames(25);Input(0,default);
        await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"power bomb creates a real blast",150);
        await Capture("tutorial-power-blast");
        await WaitState(_=>Cleared(),"power lesson clears only after its blast destroys cover",60);
        // Every lesson can be selected without prerequisites; initial setups are product-authored.
        foreach(string id in new[]{"pump","aim","items","effects","grapple","ghost","flood","duel"})
        {
            await Lesson(id);await Capture("tutorial-"+id);
            var hud=_root.GetChildren().OfType<CanvasLayer>().SelectMany(layer=>layer.GetChildren()).OfType<TutorialHud>().Single();
            Check(hud.Visible,"tutorial instructions remain visible: "+id);
            Check(!State.GetProperty("tutorial").GetProperty("complete").GetBoolean(),"opening a lesson never awards completion: "+id);
            foreach(string name in new[]{"TutorialHeader","TutorialInstructions"})
            {
                var panel=(Control)hud.FindChild(name,true,false);var rect=panel.GetGlobalRect();
                Check(rect.Position.Y>=0&&rect.End.Y<=GetViewport().GetVisibleRect().Size.Y+1,"tutorial panel fits viewport: "+id+" "+name);
            }
        }
        await KeyPress(Key.Escape);Check(_root.ScreenName=="Pause","Escape pauses the local lesson");
        long snapshots=_root.SnapshotCount;await Frames(12);Check(_root.SnapshotCount==snapshots,"pause stops local tutorial authority");
        await KeyPress(Key.Escape);Check(_root.ScreenName=="Match","Escape resumes the same lesson");
        await KeyPress(Key.Escape);await ActivateNamed("PauseRetryLesson");
        await WaitState(s=>s.GetProperty("tutorial").GetProperty("id").GetString()=="duel"&&s.GetProperty("phase").GetInt32()==1,"pause retry restarts lesson through native button",150);
        await KeyPress(Key.Escape);await Capture("tutorial-pause");await ActivateNamed("LeaveGame");
        await Frames(4);Check(_root.ScreenName=="Tutorial","pause leave returns to lessons");
        Check(NamedButton("Lesson_move").Text.StartsWith("★"),"browser retains completion marks");
        await Capture("tutorial-progress");
        await ActivateNamed("TutorialFreePractice");await Frames(75);
        Check(_root.LatestStage?.GetProperty("id").GetString()=="circuit-garden"&&State.GetProperty("tutorial").ValueKind==System.Text.Json.JsonValueKind.Null,"free practice opens garden without lesson rules");
        Check(State.GetProperty("items").GetArrayLength()>0,"free practice exposes the equipment collection");
        await Capture("tutorial-free-practice");
        _root.Back();_root.Navigate("Modes");await Frames(3);
        _root.StartLocal(new Bomber.Client.SessionSettings());
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ordinary battle works after tutorial exit",150);
        Check(State.GetProperty("tutorial").ValueKind==System.Text.Json.JsonValueKind.Null&&_root.LatestStage?.GetProperty("id").GetString()=="sunken-temple","normal battle has no tutorial rules or garden terrain");
        Check(!_root.FindChildren("*","",true,false).OfType<TutorialHud>().Any(h=>h.IsVisibleInTree()),"tutorial overlay is hidden after leaving training");
        _root.Back();await Frames(3);
    }
}

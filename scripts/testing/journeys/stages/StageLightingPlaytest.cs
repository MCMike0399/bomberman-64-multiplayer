using Godot;
using Bomber.Client.Sessions;
using BlastCircuit.App;
using BlastCircuit.Arena;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private ArenaView LightingArena=>_root.GetChildren().OfType<ArenaView>().Single();

    private async Task SelectLocalLighting(StageTime time)
    {
        bool match=_root.ScreenName=="Match";
        if(match){await KeyPress(Key.Escape);await ActivateNamed("PauseSettings");}
        else if(_root.ScreenName!="Settings"){_root.Navigate("Modes");await Frames(2);await ActivateNamed("OpenSettings");}
        var session=_root.ActiveSession;int snapshots=_root.SnapshotCount;
        var dial=NamedButton("StageLighting");dial.GrabFocus();
        for(int i=0;i<4&&_root.Preferences.StageLighting!=time;i++)await KeyPress(Key.Right);
        Check(_root.Preferences.StageLighting==time,"native lighting dial selects "+time);
        Check(PlayerPreferences.Load(_root.PreferencesPath).StageLighting==time,"lighting persists as "+time);
        if(session is LocalSession)
            Check(ReferenceEquals(session,_root.ActiveSession)&&_root.SnapshotCount==snapshots,"changing lighting preserves paused local authority");
        await KeyPress(Key.Escape);
        if(match)await ActivateNamed("ResumeGame");
        await Frames(12);
    }

    private async Task StageLightingTour()
    {
        await ControllerFocusReady();
        var original=_root.Preferences.StageLighting;
        try
        {
            foreach(string stage in new[]{"sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap"})
            {
                _root.StartLocal(new SessionSettings(Stage:stage,Bots:0),1,practice:true);
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"lighting tour enters "+stage,180);
                foreach(var time in Enum.GetValues<StageTime>())
                {
                    await SelectLocalLighting(time);
                    Check(LightingArena.TimeOfDay==time,"active stage uses personal lighting: "+stage+" "+time);
                    CheckFraming(stage,GetWindow().Size);
                    await Capture($"lighting-{stage}-{StageLighting.ToWire(time)}");
                }
                // Actual bombs and the advisory marker must remain legible in the darkest look.
                Input(0,new Bomber.Game.Matches.PlayerInput(0,0,true,true));await Frames(3);
                Input(0,new Bomber.Game.Matches.PlayerInput(0,0,false,true,Aim:true));await Frames(6);
                Check(_root.ThrowPreviews.Any(p=>p.EndpointMarkerVisible),"night retains a visible carried-bomb aim marker on "+stage);
                await Capture("lighting-night-aim-"+stage);Input(0,default);
                _root.Back();await Frames(3);
            }
            // Real timed flooding, observed from the battle camera and a closer shoreline view.
            _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0,Wins:3,RoundSeconds:60,SuddenDeath:true,Ghosts:false),2);
            Input(1,default);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==2,"lighting flood enters hurry",180);
            await MoveTo(12.5f,2.5f,200);
            await WaitState(s=>s.GetProperty("waterLevel").GetSingle()>=.25f,"lighting flood covers courtyard",3000);
            var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
            foreach(var time in Enum.GetValues<StageTime>())
            {
                await SelectLocalLighting(time);
                Check(LightingArena.TimeOfDay==time&&Player.GetProperty("alive").GetBoolean(),"flood preserves lighting and crown survivor: "+time);
                await Capture("lighting-flood-"+StageLighting.ToWire(time));
                camera.Position=new(21,14,34);camera.LookAt(new Vector3(12.5f,1,20));
                await Capture("lighting-shore-detail-"+StageLighting.ToWire(time));camera.Transform=pose;
            }
            _root.Back();await Frames(3);
            _root.Navigate("Settings");await Frames(3);await Capture("lighting-settings");await KeyPress(Key.Escape);
        }
        finally
        {
            Input(0,default);Input(1,default);_root.Navigate("Modes");
            await SelectLocalLighting(original);
        }
    }
}

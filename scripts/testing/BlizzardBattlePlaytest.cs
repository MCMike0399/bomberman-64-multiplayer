using Godot;
using Bomber.Client;
using Bomber.Game;
using Bomber.Game.Simulation;
using BlastCircuit.Arena;
using BlastCircuit.Rendering;
using BlastCircuit.UI.Menu;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task BlizzardBattleTour()
    {
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            Input(0,default);Input(1,default);
            _root.Navigate("Setup");await Frames(6);
            var menu=AuditDescendants(_root).OfType<ArcadeMenu>().Single();
            menu.Configure(new SessionSettings(),1);NamedButton("StageSelector").GrabFocus();
            for(int i=0;i<4;i++)await KeyPress(Key.Right);
            Check(menu.Settings.Stage==BlastStages.BlizzardBattle,"ordinary arena dial selects Blizzard Battle");
            await Capture("blizzard-menu-miniature");
            _root.StartLocal(new SessionSettings(Stage:BlastStages.BlizzardBattle,Bots:0,SuddenDeath:false),4);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"snowfield four-player opening reaches fight",150);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            var cones=terrain.GetParent().GetChildren().SelectMany(n=>AuditDescendants(n)).OfType<Node3D>()
                .Where(n=>n.Name.ToString().StartsWith("IceCone")).ToArray();
            Check(cones.Length==16,"actual opening renders sixteen pointed destructible ice cones");
            Check(State.GetProperty("players").GetArrayLength()==4,"reference four-player corner arrangement starts");
            Check(terrain.FindChild("FrozenCourt",true,false)!=null&&terrain.FindChild("Seabed",true,false)==null,"snowfield uses opaque ice with no ocean");
            var court=terrain.GetNode<MeshInstance3D>("FrozenCourt");var foundation=terrain.GetNode<MeshInstance3D>("GlacierFoundation");
            Check(foundation.Position.Y+foundation.Mesh.GetAabb().End.Y<court.Position.Y+court.Mesh.GetAabb().End.Y-.02f,"ice and foundation have separate depth planes on every preset");
            var problems=AuditModelProblems(cones[0],out int meshes,out int vertices);
            Check(problems.Count==0&&meshes>0&&vertices>0,"actual crystal geometry has finite normals, UVs and triangles");
            var crystal=cones[0].GetChildren().OfType<MeshInstance3D>().Single();
            Check(crystal.Mesh.GetAabb().End.Y>=1.7f&&Math.Abs(crystal.Mesh.GetAabb().End.Y-terrain.Layout.SoftBlockHeight*TempleStageView.HeightScale)<.001f,"large violet spires reach character height and match accepted cover collision");
            await Capture("blizzard-four-player-opening");
            var weather=terrain.Weather!;float clock=weather.Clock;await Frames(12);
            Check(weather.Clock>clock&&weather.VisibleFlakes>0,"production snow flurries animate above the court");
            await KeyPress(Key.Escape);float paused=weather.Clock;int snapshots=_root.SnapshotCount;await Frames(12);
            Check(weather.Clock==paused&&_root.SnapshotCount==snapshots,"ordinary pause freezes snow animation and authoritative weather");
            await ActivateNamed("ResumeGame");
            var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
            if(!_headless)
            {
                camera.Position=new(8,7,14);camera.LookAt(new Vector3(7,.35f,8));
                await Capture("blizzard-crystal-and-ice-detail");camera.Transform=pose;
                foreach(var preset in Enum.GetValues<GraphicsPreset>())
                {
                    await SelectGraphics(preset);
                    Check(weather.VisibleFlakes==(preset==GraphicsPreset.Low?72:preset==GraphicsPreset.Medium?152:240),"snow draw budget follows "+preset);
                    CheckFraming(BlastStages.BlizzardBattle,GetWindow().Size);
                    await Capture("blizzard-"+GraphicsQuality.ToWire(preset));
                }
                await SelectLocalLighting(StageTime.Night);await Capture("blizzard-night");
                await SelectLocalLighting(StageTime.Midday);
            }
            _root.Back();await Frames(3);

            _root.StartLocal(new SessionSettings(Stage:BlastStages.BlizzardBattle,Bots:0),1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"snowfield exploration starts",150);
            float start=Player.GetProperty("x").GetSingle();await Frames(45);
            Check(Player.GetProperty("x").GetSingle()>start+.3f,"accepted wind moves an idle living player");
            await MoveTo(8.5f,9.5f,200);
            Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(2);
            Input(0,new PlayerInput(0,0,B:true,Aim:true));await Frames(6);
            Check(_root.ThrowPreviews.Any(p=>p.EndpointMarkerVisible),"wind-aware aim guide appears for an ordinary carried bomb");
            await Capture("blizzard-wind-aware-aim");
            Input(0,default);await WaitState(s=>s.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==3),"ordinary release throws a bomb through the wind",30);
            await Capture("blizzard-wind-thrown-bomb");
            await MoveTo(8.5f,5.5f,100);await Frames(80);
            _root.Back();await Frames(3);

            // Real cover destruction, never inventory/position mutation.
            _root.StartLocal(new SessionSettings(Stage:BlastStages.BlizzardBattle,Bots:0,SuddenDeath:false,Ghosts:false)
                {Advanced=new AdvancedRules{DropPercent=100,DisabledItems="remote,red,bounce,fast,heart,focus,tiny,big,haste,slow"}},2);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ice cone loot battle starts",150);
            await MoveTo(5.5f,2.5f,120);
            Input(0,new PlayerInput(1,0));await Frames(1);Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);
            await MoveTo(3.5f,5.5f,100);
            await WaitState(s=>s.GetProperty("bombs").GetArrayLength()==0&&s.GetProperty("hazards").GetArrayLength()==0,"real fuse destroys an ice cone",150);
            Check(State.GetProperty("items").GetArrayLength()>0,"shattered ice exposes a real dropped upgrade");
            await Capture("blizzard-shattered-ice-reward");
            var item=State.GetProperty("items")[0];long serial=Player.GetProperty("pickupSerial").GetInt64();
            await MoveTo(item.GetProperty("cx").GetInt32()+.5f,item.GetProperty("cz").GetInt32()+.5f,140);
            Check(Player.GetProperty("pickupSerial").GetInt64()>serial,"walking collects an upgrade from broken ice");
            _root.Back();await Frames(3);

            _root.StartLocal(new SessionSettings(Stage:BlastStages.BlizzardBattle,Bots:0,RoundSeconds:60,Ghosts:false)
                {Advanced=new AdvancedRules{BlockPercent=0}},8);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==2,"snowfield enters real sudden death",150);
            await MoveTo(9.5f,9.5f,200);
            await WaitState(s=>s.GetProperty("closedLeft").GetInt32()>=4,"left and right avalanches reach the interior",420);
            Check(State.GetProperty("hurryMode").GetString()=="avalanche"&&State.GetProperty("closedLeft").GetInt32()==State.GetProperty("closedRight").GetInt32(),"bilateral avalanche snapshot replaces columns/fireballs");
            Check(AuditDescendants(LightingArena).OfType<Node3D>().Any(n=>n.Name.ToString().StartsWith("AvalancheColumn")),"accepted avalanche columns render packed animated snow");
            Check(State.GetProperty("players").EnumerateArray().Any(p=>!p.GetProperty("alive").GetBoolean())&&Player.GetProperty("alive").GetBoolean(),"side avalanche eliminates exposed seats while center survivor escapes");
            await Capture("blizzard-avalanches-closing");_root.Back();await Frames(3);

            _root.StartLocal(new SessionSettings(Stage:BlastStages.BlizzardBattle,Bots:7,SuddenDeath:false),1);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-seat snowfield CPU battle starts",150);
            bool bomb=false,blast=false;
            for(int i=0;i<300&&_root.ScreenName=="Match";i++)
            {bomb|=State.GetProperty("bombs").GetArrayLength()>0;blast|=State.GetProperty("hazards").GetArrayLength()>0;await Frames(1);}
            Check(bomb&&blast,"CPU players navigate wind and generate real bombs/explosions");
            await Capture("blizzard-eight-player-combat");_root.Back();await Frames(3);
        }
        finally
        {
            Input(0,default);Input(1,default);_root.Navigate("Modes");
            if(!_headless){await SelectGraphics(quality);await SelectLocalLighting(lighting);}
        }
    }
}

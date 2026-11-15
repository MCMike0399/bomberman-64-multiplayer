using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;
using BlastCircuit.UI.Menu;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task InTheGutterTour()
    {
        Event("gutter-policy",new{ordinaryInputs=true,stateMutation=false,shallowWater="safe floor",current="authority-owned"});
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            _root.Navigate("Setup");await Frames(6);
            var menu=AuditDescendants(_root).OfType<ArcadeMenu>().Single();
            menu.Configure(new SessionSettings(),1);NamedButton("StageSelector").GrabFocus();
            for(int n=0;n<6;n++)await KeyPress(Key.Right);
            Check(menu.Settings.Stage==BlastStages.InTheGutter,"native arena dial reaches In the Gutter");
            await Capture("in-the-gutter-menu-miniature");
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,SuddenDeath:false),4);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ordinary Gutter battle starts",180);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            Check(terrain.Layout.Id==BlastStages.InTheGutter&&State.GetProperty("players").GetArrayLength()==4,"four original-relative post starts use accepted Gutter layout");
            Check(terrain.GetNodeOrNull<MeshInstance3D>("GutterShallowWater")!=null&&terrain.GetNodeOrNull<Node3D>("GutterCurrentMarker")!=null,"shallow water and central flow marker render in the actual arena");
            Check(terrain.Scenery!=null&&!AuditDescendants(terrain.Scenery).OfType<CollisionObject3D>().Any(),"toy lock surroundings add no gameplay collision");
            await Capture("in-the-gutter-four-player-opening");
            if(!_headless)
            {
                foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High})
                {
                    await SelectGraphics(preset);CheckFraming(BlastStages.InTheGutter,GetWindow().Size);
                    var material=(ShaderMaterial)terrain.GetNode<MeshInstance3D>("GutterShallowWater").MaterialOverride;
                    Check(material.Shader==ArtMaterials.WaterShaderFor(preset),"Gutter water follows the shared "+preset+" water identity");
                    await Capture("in-the-gutter-"+GraphicsQuality.ToWire(preset));
                }
                await SelectLocalLighting(StageTime.Night);await Capture("in-the-gutter-night");
                await SelectLocalLighting(StageTime.Midday);
                var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
                camera.Position=new(29,21,32);camera.LookAt(new Vector3(10.5f,.8f,10.5f));
                await Capture("in-the-gutter-oblique-inspection");camera.Transform=pose;
            }
            _root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,SuddenDeath:false)
                {Advanced=new AdvancedRules{BlockPercent=0}},2,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"clear shallow-water exploration starts",180);
            Input(0,new PlayerInput(0,1));await Frames(42);Input(0,default);await Frames(8);
            Check(Player.GetProperty("alive").GetBoolean()&&Player.GetProperty("y").GetSingle()==0,"ordinary walking drops from the post into safe shallow water");
            await Capture("in-the-gutter-safe-shallow-court");
            Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);
            Check(State.GetProperty("bombs").GetArrayLength()>0,"ordinary primary input places a live bomb in the current");
            await Capture("in-the-gutter-current-and-bomb");
            await KeyPress(Key.Escape);var paused=State.GetProperty("gutter").GetProperty("time").GetSingle();int snapshots=_root.SnapshotCount;
            await Frames(12);
            Check(_root.SnapshotCount==snapshots&&State.GetProperty("gutter").GetProperty("time").GetSingle()==paused,"pause freezes accepted current schedule");
            await ActivateNamed("ResumeGame");_root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:7,SuddenDeath:false),1);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player waterworks CPU battle starts",180);
            bool bomb=false,blast=false;
            for(int n=0;n<300&&_root.ScreenName=="Match";n++)
            {bomb|=State.GetProperty("bombs").GetArrayLength()>0;blast|=State.GetProperty("hazards").GetArrayLength()>0;await Frames(1);}
            Check(bomb&&blast,"CPU players leave posts and create real bombs and explosions");
            await Capture("in-the-gutter-eight-player-battle");_root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,RoundSeconds:60,Ghosts:false)
                {Advanced=new AdvancedRules{BlockPercent=0}},8);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==2,"Gutter enters real sudden death",180);
            await WaitState(s=>s.GetProperty("closedLeft").GetInt32()>=3,"both grates enter the shallow court",180);
            Check(State.GetProperty("hurryMode").GetString()=="columns"&&State.GetProperty("closedLeft").GetInt32()==State.GetProperty("closedRight").GetInt32(),"closing grates track bilateral authoritative column hazard");
            Check(AuditDescendants(LightingArena).OfType<Node3D>().Any(n=>n.Name.ToString().StartsWith("ClosingGutterGrate")),"opaque metal grates replace generic red translucent walls");
            await Capture("in-the-gutter-closing-grates");
            await WaitState(s=>s.GetProperty("players").EnumerateArray().Any(p=>!p.GetProperty("alive").GetBoolean()),"grates crush exposed seats",240);
            Check(State.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="death"&&e.GetProperty("reason").GetString()=="columns"),"Gutter deaths retain the crush reason");
            await Capture("in-the-gutter-grate-crush");
        }
        finally
        {
            Input(0,default);Input(1,default);_root.Navigate("Modes");await Frames(3);
            if(!_headless){await SelectGraphics(quality);await SelectLocalLighting(lighting);}
        }
    }

    private async Task InTheGutterNetworkTour()
    {
        foreach(var mode in new[]{NetworkMode.CreatePrivate,NetworkMode.CreatePeer})
        {
            var settings=new SessionSettings(Stage:BlastStages.InTheGutter,Bots:0,SuddenDeath:false)
                {Hosting=mode==NetworkMode.CreatePeer?"peer":"server",Advanced=new AdvancedRules{BlockPercent=0}};
            await _root.StartOnlineAsync(new Uri(_server),"Waterworks host",settings,mode);
            var host=(NetworkSession)_root.ActiveSession!;
            await using var peer=await NetworkSession.ConnectAsync(new Uri(_server),"Waterworks observer",settings,NetworkMode.JoinCode,host.JoinCode);
            Action pump=()=>peer.Advance(1d/30);GetTree().ProcessFrame+=pump;
            try
            {
                var clock=System.Diagnostics.Stopwatch.StartNew();
                while(host.Party?.CanStart!=true&&clock.Elapsed.TotalSeconds<20)
                {await Frames(1);await Task.Delay(16);}
                Check(host.Party?.CanStart==true,mode+" admits two compatible Gutter clients");host.RequestStart();
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,mode+" starts real Gutter authority",900);
                Check(_root.LatestStage!.Value.GetProperty("id").GetString()==BlastStages.InTheGutter,mode+" accepts the correct stage");
                await MoveTo(4.5f,9.5f,240);
                Check(Player.GetProperty("y").GetSingle()==0,mode+" accepted ordinary movement reaches the shallow floor");
                Input(0,new PlayerInput(0,0,A:true));
                await WaitState(s=>s.GetProperty("bombs").GetArrayLength()>0,mode+" accepts a real ground bomb",120);
                Input(0,default);
                bool stageSeen=false,currentSeen=false,bombSeen=false;clock.Restart();
                while(clock.Elapsed.TotalSeconds<8&&!(stageSeen&&currentSeen&&bombSeen))
                {
                    await Frames(1);await Task.Delay(16);
                    foreach(var frame in peer.DrainMessages())
                    {
                        string? kind=frame.GetProperty("t").GetString();var data=frame.GetProperty("d");
                        if(kind=="stage")stageSeen|=data.GetProperty("id").GetString()==BlastStages.InTheGutter;
                        if(kind!="state")continue;
                        currentSeen|=data.GetProperty("gutter").ValueKind==System.Text.Json.JsonValueKind.Object;
                        bombSeen|=data.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("ownerId").GetString()==host.PlayerId&&b.GetProperty("y").GetSingle()==0);
                    }
                }
                Event("gutter-network-observer",new{mode,stageSeen,currentSeen,bombSeen});
                Check(stageSeen,mode+" observer receives the real Gutter stage");
                Check(currentSeen,mode+" observer receives the accepted current schedule");
                Check(bombSeen,mode+" observer receives the real ground bomb");
                await Capture("in-the-gutter-network-"+mode.ToString().ToLowerInvariant());
            }
            finally
            {
                Input(0,default);GetTree().ProcessFrame-=pump;_root.Back();await Frames(3);
            }
        }
    }
}

using Godot;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Models;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;
using BlastCircuit.UI.Menu;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task FieldOfGrassShapeStudy()
    {
        Event("cutaway-hollow-study-policy",new{ordinaryInputs=true,stateMutation=false,artApproval=false,
            brief="rounded loam banks and exposed strata; exact authority support sheets"});
        var original=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            foreach(var (preset,time,label) in new[]{(GraphicsPreset.High,StageTime.Midday,"high-midday"),
                (GraphicsPreset.High,StageTime.Night,"high-night"),(GraphicsPreset.Low,StageTime.Midday,"low-midday")})
            {
                if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
                if(!_headless){await SelectGraphics(preset);await SelectLocalLighting(time);}
                _root.StartLocal(new SessionSettings(Stage:BlastStages.FieldOfGrass,Bots:7,SuddenDeath:false),1);
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"Cutaway Hollow study enters "+label,150);
                var arena=_root.GetChildren().OfType<ArenaView>().Single();var terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
                Check(AuditDescendants(terrain).OfType<MeshInstance3D>().Any(n=>n.Name=="HollowStrataCutFace")&&
                    AuditDescendants(terrain).OfType<MeshInstance3D>().Count(n=>n.Name.ToString().StartsWith("HollowRounded"))==2,
                    "study consumes the new exposed earth slice and rounded bank masses");
                CheckClockwiseMeshes(AuditDescendants(terrain).OfType<MeshInstance3D>()
                    .Where(n=>n.Name.ToString().StartsWith("MeadowBankSlope")||n.Name.ToString().StartsWith("Hollow"))
                    .Select(n=>(n.Mesh,n.Name.ToString(),n.Name.ToString().StartsWith("MeadowBankSlope")))
                    .Concat(AuditDescendants(arena).OfType<MeshInstance3D>().Where(n=>n.GetParent().Name.ToString().StartsWith("HollowStandingStone"))
                        .Select(n=>(n.Mesh,n.GetParent().Name.ToString(),false))));
                Check(SceneryIntrusions(terrain).Count==0,"blocked stile and hay vignette stay outside all standing volumes");
                CheckFraming(BlastStages.FieldOfGrass,GetWindow().Size);
                await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"ordinary eight-player hollow battle produces real blasts",600);
                await Capture("cutaway-hollow-shape-"+label+"-combat");
                if(!_headless&&time==StageTime.Midday&&preset==GraphicsPreset.High)
                {
                    var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;var(h,v)=(camera.HOffset,camera.VOffset);
                    try
                    {
                        camera.HOffset=0;camera.VOffset=0;
                        camera.Position=new(terrain.Width+6,13,terrain.Depth+12);camera.LookAt(new Vector3(terrain.Width/2f,.4f,terrain.Depth/2f));
                        await Capture("cutaway-hollow-shape-oblique-inspection");
                        camera.Position=new(terrain.Width/2f,4.8f,terrain.Depth+12);camera.LookAt(new Vector3(terrain.Width/2f,.3f,terrain.Depth-1));
                        await Capture("cutaway-hollow-shape-cut-face-inspection");
                    }
                    finally{camera.Transform=pose;camera.HOffset=h;camera.VOffset=v;}
                }
            }
        }
        finally
        {
            Input(0,default);Input(1,default);
            if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
            if(!_headless){await SelectGraphics(original);await SelectLocalLighting(lighting);}
        }
    }

    private async Task FieldOfGrassNetworkTour()
    {
        foreach(var mode in new[]{NetworkMode.CreatePrivate,NetworkMode.CreatePeer})
        {
            var settings=new SessionSettings(Stage:BlastStages.FieldOfGrass,Bots:0,SuddenDeath:false)
                {Hosting=mode==NetworkMode.CreatePeer?"peer":"server",Advanced=new AdvancedRules{BlockPercent=0}};
            await _root.StartOnlineAsync(new Uri(_server),"Meadow host",settings,mode);
            var host=(NetworkSession)_root.ActiveSession!;
            await using var observer=await NetworkSession.ConnectAsync(new Uri(_server),"Meadow observer",settings,NetworkMode.JoinCode,host.JoinCode);
            Action pump=()=>observer.Advance(1d/30);GetTree().ProcessFrame+=pump;
            try
            {
                var clock=System.Diagnostics.Stopwatch.StartNew();
                while(host.Party?.CanStart!=true&&clock.Elapsed.TotalSeconds<20)
                {await Frames(1);await Task.Delay(16);}
                Check(host.Party?.CanStart==true,mode+" meadow room admits two compatible humans");host.RequestStart();
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,mode+" starts actual meadow authority",900);
                Check(_root.LatestStage!.Value.GetProperty("id").GetString()==BlastStages.FieldOfGrass,mode+" renders the accepted meadow layout");
                await MoveTo(2.5f,9.5f,240);await MoveTo(10.5f,9.5f,300);
                Check(Math.Abs(Player.GetProperty("y").GetSingle())<.03f,mode+" ordinary input descends the continuous bank into grass");
                Input(0,new PlayerInput(0,0,A:true));await Frames(3);Input(0,default);
                bool stageSeen=false,floorSeen=false,bombSeen=false;clock.Restart();
                while(clock.Elapsed.TotalSeconds<8&&!(stageSeen&&floorSeen&&bombSeen))
                {
                    await Frames(1);await Task.Delay(16);
                    foreach(var frame in observer.DrainMessages())
                    {
                        var kind=frame.GetProperty("t").GetString();var data=frame.GetProperty("d");
                        if(kind=="stage")stageSeen|=data.GetProperty("id").GetString()==BlastStages.FieldOfGrass;
                        if(kind!="state")continue;
                        floorSeen|=data.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId&&Math.Abs(p.GetProperty("y").GetSingle())<.03f);
                        bombSeen|=data.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("ownerId").GetString()==host.PlayerId&&Math.Abs(b.GetProperty("y").GetSingle())<.03f);
                    }
                }
                Check(stageSeen&&floorSeen&&bombSeen,mode+" observer receives actual stage, descended player and planted meadow bomb");
                await Capture("field-of-grass-network-"+mode.ToString().ToLowerInvariant());
            }
            finally{Input(0,default);GetTree().ProcessFrame-=pump;if(_root.ActiveSession!=null){_root.Back();await Frames(3);}}
        }
    }

    private async Task FieldOfGrassTour()
    {
        Event("field-of-grass-policy",new{ordinaryInputs=true,stateMutation=false,reference="official guide p103",concealment="opaque foliage only; terrain and blast rules unchanged"});
        var original=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            _root.Navigate("Setup");await Frames(5);
            var menu=_root.FindChildren("*","",true,false).OfType<ArcadeMenu>().Single();
            menu.Configure(new SessionSettings(Stage:BlastStages.FieldOfGrass),1);await Frames(8);
            Check(menu.Settings.Stage==BlastStages.FieldOfGrass,"native local setup retains Field of Grass");
            await Capture("field-of-grass-menu-miniature");
            _root.StartLocal(new SessionSettings(Stage:BlastStages.FieldOfGrass,Bots:0,SuddenDeath:false),8);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player meadow starts",150);
            Check(State.GetProperty("players").GetArrayLength()==8&&State.GetProperty("players").EnumerateArray().All(p=>Math.Abs(p.GetProperty("y").GetSingle()-2)<.01f),"all eight seats stand on the two side-bank ledges");
            var arena=_root.GetChildren().OfType<ArenaView>().Single();
            var terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
            Check(AuditDescendants(terrain).OfType<MultiMeshInstance3D>().Any(n=>n.Name=="ConcealingMeadow"&&n.Multimesh.InstanceCount>500),"actual arena renders a dense opaque meadow");
            Check(AuditDescendants(arena).OfType<Node3D>().Count(n=>n.Name.ToString().StartsWith("HollowStandingStone"))==28,"accepted battle cover renders as twenty-eight original mossy standing stones");
            CheckClockwiseMeshes(AuditDescendants(terrain).OfType<MeshInstance3D>()
                .Where(n=>n.Name.ToString().StartsWith("MeadowBankSlope")||n.Name.ToString().StartsWith("ContinuousBankEdge")||n.Name.ToString().StartsWith("Hollow"))
                .Select(n=>(n.Mesh,n.Name.ToString(),n.Name.ToString().StartsWith("MeadowBankSlope"))).Concat(AuditDescendants(terrain).OfType<MultiMeshInstance3D>()
                    .Where(n=>n.Name=="ConcealingMeadow").Select(n=>(n.Multimesh.Mesh,n.Name.ToString(),false)))
                .Concat(AuditDescendants(arena).OfType<MeshInstance3D>().Where(n=>n.GetParent().Name.ToString().StartsWith("HollowStandingStone"))
                    .Select(n=>(n.Mesh,n.GetParent().Name.ToString(),false))));
            CheckFraming(BlastStages.FieldOfGrass,GetWindow().Size);
            await Capture("field-of-grass-eight-player-opening");
            foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High})
            {
                if(!_headless)await SelectGraphics(preset);
                Check(AuditDescendants(terrain).OfType<MultiMeshInstance3D>().Single(n=>n.Name=="ConcealingMeadow").IsVisibleInTree(),"gameplay grass remains on "+preset);
                await Capture("field-of-grass-"+GraphicsQuality.ToWire(preset));
            }
            var grassPaint=(ShaderMaterial)AuditDescendants(terrain).OfType<MultiMeshInstance3D>().Single(n=>n.Name=="ConcealingMeadow").MaterialOverride;
            float windBefore=grassPaint.GetShaderParameter("ambient_clock").AsSingle();await Frames(45);
            Check(grassPaint.GetShaderParameter("ambient_clock").AsSingle()>windBefore,"opaque meadow wind advances through its CPU-owned clock");
            await Capture("field-of-grass-high-wind");
            await KeyPress(Key.Escape);await Frames(3);
            float frozenWind=grassPaint.GetShaderParameter("ambient_clock").AsSingle();int frozenSnapshots=_root.SnapshotCount;await Frames(12);
            Check(grassPaint.GetShaderParameter("ambient_clock").AsSingle()==frozenWind&&_root.SnapshotCount==frozenSnapshots,"local pause freezes real canopy wind and authority together");
            await ActivateNamed("ResumeGame");
            if(!_headless)foreach(var time in new[]{StageTime.Morning,StageTime.Afternoon,StageTime.Night})
            {await SelectLocalLighting(time);await Capture("field-of-grass-lighting-"+StageLighting.ToWire(time));}
            _root.Back();await Frames(3);

            _root.StartLocal(new SessionSettings(Stage:BlastStages.FieldOfGrass,Bots:0,SuddenDeath:false),1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"meadow exploration begins",150);
            await GrappleWalk(0,2,9);await GrappleWalk(0,3,9);
            arena=_root.GetChildren().OfType<ArenaView>().Single();terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
            var slope=AuditDescendants(terrain).OfType<MeshInstance3D>().Single(n=>n.Name=="MeadowBankSlope3_9");
            var slopeVertices=slope.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var sa=slopeVertices[0];var sn=(slopeVertices[2]-sa).Cross(slopeVertices[1]-sa);
            float footX=Player.GetProperty("x").GetSingle(),footZ=Player.GetProperty("z").GetSingle();
            float renderedY=sa.Y-(sn.X*(footX-sa.X)+sn.Z*(footZ-sa.Z))/sn.Y;
            Check(Player.GetProperty("y").GetSingle() is >1.25f and <1.75f&&Math.Abs(Player.GetProperty("y").GetSingle()*TempleStageView.HeightScale-renderedY)<.025f,
                "ordinary mid-slope actor feet meet the actual upward-facing rendered bank mesh");
            await Capture("field-of-grass-real-mid-slope-support");
            await GrappleWalk(0,10,9);
            Check(Math.Abs(Player.GetProperty("y").GetSingle())<.02f,"ordinary movement descends continuously into meadow");
            await Capture("field-of-grass-waist-high-canopy");
            // Ordinary primary tap leaves an uncharged bomb under the meadow.
            Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(3);
            Check(State.GetProperty("bombs").GetArrayLength()==1,"normal bomb is accepted on the grassy floor");
            await Capture("field-of-grass-normal-bomb-concealment",0);
            await GrappleWalk(0,13,9);
            await WaitState(s=>s.GetProperty("bombs").GetArrayLength()==0&&s.GetProperty("hazards").GetArrayLength()==0,"normal meadow bomb completes real fuse and blast",160);
            await GrappleWalk(0,10,9);
            Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(2);Input(0,new PlayerInput(0,0,B:true));await Frames(4);
            for(int pump=0;pump<3;pump++)
            {Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(2);Input(0,new PlayerInput(0,0,B:true));await Frames(16);}
            Input(0,new PlayerInput(0,0,B:true,AimTarget:new(12.5f,9.5f)));await Frames(4);
            Check(_root.ThrowPreviews.Single().Pumps==3,"three accepted pump presses enlarge the real bomb above the meadow");
            await Capture("field-of-grass-pumped-bomb-aim");
            Input(0,default);
            await WaitState(s=>s.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==0),"pumped bomb lands on actual grassy floor",110);
            Check(AuditDescendants(arena= _root.GetChildren().OfType<ArenaView>().Single()).OfType<BombModel>().Any(b=>b.Charge==3),"production pumped-bomb model retains its enlarged charge");
            await Capture("field-of-grass-pumped-bomb-visible",0);
            _root.Back();await Frames(3);

            _root.StartLocal(new SessionSettings(Stage:BlastStages.FieldOfGrass,Bots:7,SuddenDeath:false),1);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player meadow CPU battle begins",150);
            bool bomb=false,blast=false;
            for(int tick=0;tick<300&&_root.ScreenName=="Match";tick++)
            {bomb|=State.GetProperty("bombs").GetArrayLength()>0;blast|=State.GetProperty("hazards").GetArrayLength()>0;await Frames(1);}
            Check(bomb&&blast,"eight-player bots navigate the banks and generate real bombs and blasts");
            await Capture("field-of-grass-eight-player-combat");_root.Back();await Frames(3);

            _root.StartLocal(new SessionSettings(Stage:BlastStages.FieldOfGrass,Bots:0,RoundSeconds:60,Ghosts:false),3);
            await WaitState(s=>s.GetProperty("meteors").GetArrayLength()>0,"meadow hurry publishes a bank fireball warning",180);
            Input(0,new PlayerInput(1,0));await Frames(25);Input(0,default);
            await Capture("field-of-grass-fireball-warning");
            await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"meadow meteor hits actual bank support",80);
            Check(State.GetProperty("hurryMode").GetString()=="meteors"&&State.GetProperty("closedLeft").GetInt32()==0,"fireballs replace closing walls on the meadow");
            await Capture("field-of-grass-fireball-impact");
        }
        finally
        {
            Input(0,default);Input(1,default);
            if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
            if(!_headless){await SelectGraphics(original);await SelectLocalLighting(lighting);}
        }
    }
}

using Godot;
using System.Text.Json;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Effects;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;
using BlastCircuit.UI.Menu;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task SeaSickFinishTour()
    {
        Event("sea-sick-colour-policy",new{ordinaryInputs=true,stateMutation=false,barrels="stock 48",combat="fresh seeded eight-player matches",roll="actual slow two-degree cycle"});
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            foreach(var(preset,time,label) in new[]{(GraphicsPreset.High,StageTime.Midday,"high-midday"),
                (GraphicsPreset.High,StageTime.Night,"high-night"),(GraphicsPreset.Low,StageTime.Midday,"low-midday")})
            {
                await SelectGraphics(preset);await SelectLocalLighting(time);
                _root.StartLocal(new SessionSettings(Stage:BlastStages.SeaSick,Bots:7,SuddenDeath:false),1);
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player coloured ship starts: "+label,150);
                Check(LightingArena.TimeOfDay==time,"rendered local lighting matches the colour capture: "+time);
                Check(State.GetProperty("players").GetArrayLength()==8,"colour review retains eight roster seats: "+label);
                await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"real CPU combat reaches a blast: "+label,360);
                CheckFraming(BlastStages.SeaSick,GetWindow().Size);
                await Capture("sea-sick-colour-eight-player-"+label,0);_root.Back();await Frames(3);
            }
            await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Midday);
            await StartColourShip();
            float sign=0;
            for(int pass=0;pass<2;pass++)
            {
                for(int frame=0;frame<420&&(Math.Abs(LightingArena.ShipRollSine)<.0347f||pass==1&&Mathf.Sign(LightingArena.ShipRollSine)==sign);frame++)await Frames(1);
                Check(Math.Abs(LightingArena.ShipRollSine)>=.0347f,"coloured capture reaches a real roll extremum");
                if(pass==0)sign=Mathf.Sign(LightingArena.ShipRollSine);
                else Check(Mathf.Sign(LightingArena.ShipRollSine)!=sign,"coloured waterline review reaches the opposite roll");
                await Capture("sea-sick-colour-"+(pass==0?"maximum-roll":"opposite-roll"),0);
                ShipColourPose(pass==0?"maximum-roll":"opposite-roll");
            }
            foreach(var size in new[]{new Vector2I(1280,800),new Vector2I(1280,720)})
            {
                if(!_headless)await ResizeWindow(size);
                CheckFraming(BlastStages.SeaSick,GetWindow().Size);CheckShipBowHeel();
                await Capture($"sea-sick-colour-bow-heel-{size.X}x{size.Y}");
            }
            if(!_headless)await ResizeWindow(new(1280,800));
            await GrappleWalk(0,4,4);
            Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(2);
            Input(0,new PlayerInput(0,0,B:true,Aim:true,AimTarget:new System.Numerics.Vector2(9.0f,4.5f)));await Frames(5);
            Check(Player.GetProperty("carrying").GetString()=="bomb","ordinary carry and aim prepare a live rocking-deck guide");
            for(int frame=0;frame<420;frame++)
            {
                float phase=Mathf.PosMod(LightingArena.ShipMotionClock*.55f,Mathf.Tau);
                if(phase>=.14f&&phase<=.23f)break;
                await Frames(1);
            }
            Input(1,new PlayerInput(0,0,A:true));await Frames(2);Input(1,default);
            await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0&&Math.Abs(LightingArena.ShipRollSine)>.0345f,
                "ordinary second-player bomb reaches a real blast at maximum roll",150);
            var guide=AuditDescendants(LightingArena).OfType<BombThrowPreview>().Single();
            var marker=guide.GetChildren().OfType<MeshInstance3D>().Last();
            var reticle=AuditDescendants(guide).OfType<ThrowEndpointReticle>().Single();
            Check(!reticle.EdgeClamped&&reticle.Position.DistanceTo(GetViewport().GetCamera3D()!.UnprojectPosition(marker.GlobalPosition))<.75f,
                "maximum-roll aim marker remains on its actual world landing beside a real blast");
            await Capture("sea-sick-colour-maximum-roll-aim-and-blast",0);ShipColourPose("aim-and-blast");
            Input(0,default);_root.Back();await Frames(3);

            _root.StartLocal(new SessionSettings(Stage:BlastStages.SeaSick,Bots:0,RoundSeconds:60,Ghosts:false),3);
            await WaitState(s=>s.GetProperty("meteors").GetArrayLength()>0,"coloured sixty-second Hurry fixture publishes a real meteor",180);
            await Capture("sea-sick-colour-meteor-warning",0);
            Input(0,new PlayerInput(0,1));await Frames(25);Input(0,default);
            await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"coloured meteor reaches impact",90);
            await Capture("sea-sick-colour-meteor-impact",0);_root.Back();await Frames(3);

            await SeaSickMiniatureReview();
        }
        finally
        {
            Input(0,default);Input(1,default);
            for(int i=0;i<3&&_root.ActiveSession!=null;i++){_root.Back();await Frames(3);}
            _root.Navigate("Modes");await Frames(3);await SelectGraphics(quality);await SelectLocalLighting(lighting);_root.Navigate("Modes");
        }
    }

    private async Task StartColourShip()
    {
        _root.StartLocal(new SessionSettings(Stage:BlastStages.SeaSick,Bots:0,SuddenDeath:false,Ghosts:false),4);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"stock-barrel coloured roll battle starts",150);
        var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
        var problems=AuditModelProblems(terrain,out int meshes,out int vertices);
        Check(problems.Count==0,$"finished ship has finite clockwise geometry: {string.Join("; ",problems.Take(3))}");
        Check(AuditDescendants(LightingArena).Count(n=>n.HasMeta("sea_sick_barrel"))==48,"coloured review restores all stock barrels unchanged");
    }

    private void CheckShipBowHeel()
    {
        var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();var camera=GetViewport().GetCamera3D()!;
        var viewport=GetViewport().GetVisibleRect().Size;
        foreach(var p in new[]{new Vector3(25.3f,5.0f,9.5f),new Vector3(25.5f,5.1f,9.5f)})
        {
            var screen=camera.UnprojectPosition(terrain.ToGlobal(p))/viewport;
            Check(screen.X>=.015f&&screen.X<=.985f&&screen.Y>=.13f&&screen.Y<=.90f,"bow stem and bowsprit heel remain framed: "+screen);
        }
    }

    private void ShipColourPose(string label)
    {
        var arena=LightingArena;var camera=GetViewport().GetCamera3D()!;
        Event("ship-colour-pose",new{label,lighting=arena.TimeOfDay.ToString(),clock=arena.ShipMotionClock,rollDegrees=Mathf.Asin(arena.ShipRollSine)*180/Mathf.Pi,
            parent=arena.StageAssemblyTransform.ToString(),camera=camera.GetCameraTransform().ToString(),
            near=arena.ScreenPoint(new System.Numerics.Vector3(9.5f,0,17.5f)).ToString()});
    }

    private async Task SeaSickRollColour()
    {
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Midday);await StartColourShip();
            float first=LightingArena.ShipMotionClock,period=Mathf.Tau/.55f;
            for(int i=0;i<8;i++)
            {
                float target=first+period*i/8;
                for(int frame=0;frame<420&&LightingArena.ShipMotionClock<target;frame++)await Frames(1);
                await Capture($"sea-sick-roll-cycle-{i:D2}",0);ShipColourPose($"cycle-{i:D2}");
            }
        }
        finally
        {
            _root.Back();await Frames(3);await SelectGraphics(quality);await SelectLocalLighting(lighting);_root.Navigate("Modes");
        }
    }
    private async Task SeaSickFramingRegression()
    {
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        await StartColourShip();var stage=_root.LatestStage!.Value;
        var points=new List<Vector3>();int width=stage.GetProperty("width").GetInt32(),index=0;
        foreach(var cell in stage.GetProperty("cells").EnumerateArray())
        {
            int x=index%width,z=index++/width,kind=cell.GetProperty("k").GetInt32();if(kind is 2 or 3)continue;
            float y=(kind==4?cell.GetProperty("rt"):cell.GetProperty("t")).GetSingle()*TempleStageView.HeightScale;
            float head=kind==1?Math.Min(cell.GetProperty("oh").GetSingle()*TempleStageView.HeightScale,2.1f):2.1f;
            foreach(float dx in new[]{0f,1f})foreach(float dz in new[]{0f,1f})foreach(float h in new[]{0f,head})points.Add(new(x+dx,y+h,z+dz));
        }
        try
        {
            foreach(var size in new[]{new Vector2I(1280,800),new Vector2I(1280,720)})
            {
                var view=new SubViewport{OwnWorld3D=true,Size=size,RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};AddChild(view);
                try
                {
                    var world=new Node3D();view.AddChild(world);
                    var camera=new Camera3D{Fov=36,Near=.1f,Far=180,Current=true};world.AddChild(camera);await Frames(1);
                    var rig=new BattleCameraRig(camera);rig.SetStage(stage);var fitted=camera.GetCameraTransform();
                    foreach(float roll in new[]{-ArenaView.ShipRollAmplitude,ArenaView.ShipRollAmplitude})foreach(float pitch in new[]{-ArenaView.ShipPitchAmplitude,ArenaView.ShipPitchAmplitude})
                    {
                        var basis=PresentationMath.Rotation(new(roll,0,pitch));var pivot=new Vector3(9.5f,0,9.5f);var pose=new Transform3D(basis,pivot-basis*pivot);
                        var projected=points.Select(p=>camera.UnprojectPosition(pose*p)/new Vector2(size.X,size.Y)).ToArray();
                        var min=projected.Aggregate(new Vector2(float.MaxValue,float.MaxValue),(a,b)=>a.Min(b));
                        var max=projected.Aggregate(new Vector2(float.MinValue,float.MinValue),(a,b)=>a.Max(b));
                        Event("ship-fixed-camera-envelope",new{size=size.ToString(),roll,pitch,min=min.ToString(),max=max.ToString()});
                        Check(min.X>=.02f&&max.X<=.98f&&min.Y>=.14f&&max.Y<=.90f,$"fixed Sea Sick camera contains full motion envelope at {size}: {min}–{max}");
                        Check(camera.GetCameraTransform().IsEqualApprox(fitted),"framing regression never follows or compensates the ship's roll");
                    }
                }
                finally{view.QueueFree();await Frames(1);}
            }
        }
        finally{_root.Back();await Frames(3);}
    }

    private async Task SeaSickMiniatureReview()
    {
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        _root.Navigate("Setup");await Frames(8);var settings=_root.FindChild("SetupSettings",true,false)!;
        var menu=(ArcadeMenu)settings.GetParent().GetParent().GetParent();
        try
        {
            menu.Configure(new SessionSettings(Stage:BlastStages.SeaSick),1);await Frames(8);
            var miniature=AuditDescendants(_root).OfType<TempleStageView>().Single(t=>t.GetParent().Name=="ArenaDiorama"&&t.IsVisibleInTree());
            var hull=miniature.GetNode<MeshInstance3D>("CutterHull");
            Check(hull.IsVisibleInTree(),"menu miniature keeps the cutter's signature navy belly visible");
            var sea=miniature.GetNode<MeshInstance3D>("SeaSickMiniatureSea");
            Check(sea.IsVisibleInTree()&&sea.MaterialOverride is StandardMaterial3D {Transparency:BaseMaterial3D.TransparencyEnum.Disabled},"miniature seats the ship in an opaque sea disc");
            var mount=miniature.GetParent<Node3D>();var inverse=mount.GlobalTransform.AffineInverse();
            var seaBounds=(inverse*sea.GlobalTransform)*sea.Mesh.GetAabb();
            float waterline=(inverse*miniature.ToGlobal(new Vector3(12.2f,-2.1f,9.5f))).Y;
            Check(Math.Abs(waterline-seaBounds.End.Y)<.004f&&seaBounds.End.Y>-.315f&&seaBounds.End.Y<-.27f,"miniature waterline meets the sea disc directly above the display stand");
            var hullVertices=hull.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            float submergedRadius=hullVertices.Where(p=>p.Y<=-2.1f).Select(p=>
            {
                var at=inverse*hull.ToGlobal(p);return new Vector2(at.X,at.Z).Length();
            }).DefaultIfEmpty(0).Max();
            Event("sea-miniature-seating",new{waterline,seaTop=seaBounds.End.Y,submergedRadius,discRadius=4.55f});
            Check(submergedRadius<=4.56f,"opaque miniature sea covers the whole submerged keel footprint");
            Check(miniature.Scenery==null,"ship miniature omits exterior scenery");
            await Capture("sea-sick-colour-menu-miniature");
        }
        finally{menu.Configure(new SessionSettings(),1);_root.Navigate("Modes");await Frames(3);}
    }

}

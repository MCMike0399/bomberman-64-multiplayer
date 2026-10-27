using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Models;
using BlastCircuit.Rendering;
using BlastCircuit.UI.Menu;
using BlastCircuit.Arena.Stages;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task GreedyTrapTour()
    {
        Event("greedy-trap-policy",new{ordinaryInputs=true,stateMutation=false,inspectionCameras="explicitly labelled",turquoiseFloor="dry opaque tiles"});
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        _root.Navigate("Setup");await Frames(8);
        var state=_root.FindChild("SetupSettings",true,false)!;
        var menu=(ArcadeMenu)state.GetParent().GetParent().GetParent();
        menu.Configure(new SessionSettings(),1);NamedButton("StageSelector").GrabFocus();
        for(int i=0;i<5;i++)await KeyPress(Key.Right);
        Check(menu.Settings.Stage==BlastStages.GreedyTrap,"native arena selector reaches Greedy Trap");
        var miniature=AuditDescendants(_root).OfType<TempleStageView>().Single(t=>t.GetParent().Name=="ArenaDiorama"&&t.IsVisibleInTree());
        Check(!miniature.GetNode<Node3D>("GreedyExterior").IsVisibleInTree(),"menu miniature trims the exterior to keep the display plinth readable");
        await Capture("greedy-trap-menu-miniature");
        _root.StartLocal(new SessionSettings(Stage:BlastStages.GreedyTrap,Bots:0),4);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ordinary four-player Greedy Trap battle starts",150);
        Check(State.GetProperty("players").GetArrayLength()==4&&CoverCount()==8,"four upper starts surround eight real bottom blocks");
        var openingTerrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
        foreach(var tread in openingTerrain.GetChildren().OfType<MeshInstance3D>().Where(n=>n.Name.ToString().StartsWith("GreedyTread_")))
        {
            var support=openingTerrain.GetNode<MeshInstance3D>(tread.Name.ToString().Replace("GreedyTread_","GreedyStep_"));
            Check(tread.Position.Y+tread.Mesh.GetAabb().End.Y>support.Position.Y+support.Mesh.GetAabb().End.Y+.005f,
                "stair paving stays visibly above its structural top: "+tread.Name);
        }
        var scenery=openingTerrain.GetNodeOrNull<Node3D>("GreedyExterior");
        Check(scenery is not null&&scenery.IsVisibleInTree(),"Greedy Trap has a visible exterior beyond its enclosed battle floor");
        Check(AuditDescendants(LightingArena).OfType<WorldEnvironment>().Single().Environment.BackgroundMode==Godot.Environment.BGMode.Sky,
            "Greedy Trap shares the other outdoor arenas' lit sky instead of a black background");
        Check(!AuditDescendants(scenery!).OfType<CollisionObject3D>().Any(),"reservoir scenery adds no gameplay collision bodies");
        var playable=new Rect2(-.35f,-.35f,openingTerrain.Width+.7f,openingTerrain.Depth+.7f);
        var intrusive=AuditDescendants(scenery!).OfType<MeshInstance3D>().Where(n=>n.Mesh!=null).Where(n=>
        {
            var bounds=(openingTerrain.GlobalTransform.AffineInverse()*n.GlobalTransform)*n.Mesh.GetAabb();
            return bounds.End.Y>=0&&playable.Intersects(new Rect2(bounds.Position.X,bounds.Position.Z,bounds.Size.X,bounds.Size.Z));
        }).Select(n=>n.Name.ToString()).ToArray();
        Check(intrusive.Length==0,"exterior architecture stays below or outside the playable volume: "+string.Join(',',intrusive));
        var wheel=scenery!.GetNode<Node3D>("GreedyWaterwheel");float wheelAngle=wheel.Rotation.Z;
        await Frames(8);Check(Math.Abs(wheel.Rotation.Z-wheelAngle)>.01f,"decorative waterwheel rotates independently of match hazards");
        var brick=openingTerrain.GetNode<MeshInstance3D>("GreedyWallBrick_9_0");
        var cap=openingTerrain.GetNode<MeshInstance3D>("GreedyWallCap_9_0");
        Check(brick.Position.Y+brick.Mesh.GetAabb().End.Y<=cap.Position.Y+cap.Mesh.GetAabb().Position.Y+.001f,
            "brick tops remain below the separate wall-cap surface without coplanar flicker");
        Check(Math.Abs(cap.Position.Y+cap.Mesh.GetAabb().End.Y-(2+openingTerrain.AimObstacleHeight(9,0))*TempleStageView.HeightScale)<.001f,
            "visible perimeter crown matches authored throw collision height");
        await Capture("greedy-trap-four-player-opening");
        if(!_headless)
        {
            using var image=Image.LoadFromFile(_screenshots[^1]);int visible=0,samples=0;
            foreach(int side in new[]{0,1})for(int y=image.GetHeight()/4;y<image.GetHeight()*3/4;y+=8)
                for(int x=8;x<image.GetWidth()/8;x+=8)
                {
                    var color=image.GetPixel(side==0?x:image.GetWidth()-1-x,y);samples++;
                    if(color.R*.2126f+color.G*.7152f+color.B*.0722f>.025f)visible++;
                }
            Event("greedy-background-coverage",new{visible,samples});
            Check(visible>samples*.9f,"normal battle view fills both former black side margins with scenery");
        }
        if(!_headless)
        {
            foreach(var size in new[]{new Vector2I(1280,720),new Vector2I(1280,800)})
            {await ResizeWindow(size);CheckFraming(BlastStages.GreedyTrap,size);await Capture($"greedy-trap-overview-{size.X}x{size.Y}");}
            var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
            try
            {
                camera.Position=new(25,29,32);camera.LookAt(new Vector3(9.5f,1.5f,9.5f));
                await Capture("greedy-trap-oblique-inspection");
                camera.Position=new(9.5f,31,11);camera.LookAt(new Vector3(9.5f,0,9.5f));
                await Capture("greedy-trap-overhead-inspection");
            }
            finally{camera.Transform=pose;}
        }
        foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High})
        {
            await SelectGraphics(preset);await Frames(3);
            var terrain=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
            var materials=AuditDescendants(terrain).OfType<MeshInstance3D>().Select(n=>n.MaterialOverride).OfType<ShaderMaterial>().Distinct().ToArray();
            Check(materials.Count(m=>m.Shader.Code.Contains("uniform int surface_style"))==6&&materials.All(m=>m.GetShaderParameter("quality_detail").AsSingle()==(preset==GraphicsPreset.Low?0:1)),"arena and exterior textures follow "+preset);
            Check(!terrain.GetChildren().OfType<MeshInstance3D>().Any(n=>n.Mesh is PlaneMesh),"turquoise pit has no water plane at "+preset);
            var moat=terrain.GetNode<MeshInstance3D>("GreedyExterior/GreedyReservoirWater");
            Check(((ShaderMaterial)moat.MaterialOverride).Shader==ArtMaterials.WaterShaderFor(preset),"decorative reservoir follows the shared water quality policy at "+preset);
            Check(Math.Abs(moat.Position.Y-TempleStageView.GreedyMoatHeight)<.001f,"exterior reservoir stays below the safe chamber floor");
            await Capture("greedy-trap-"+GraphicsQuality.ToWire(preset));
        }
        await SelectGraphics(quality);
        foreach(var time in new[]{StageTime.Morning,StageTime.Afternoon,StageTime.Night})
        {await SelectLocalLighting(time);await Capture("greedy-trap-lighting-"+time.ToString().ToLowerInvariant());}
        await SelectLocalLighting(lighting);
        _root.Back();await Frames(3);_root.Navigate("Practice");await Frames(3);await ActivateNamed("LaunchMatch");
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ordinary Practice menu retains Greedy Trap",150);
        Check(_root.LatestStage!.Value.GetProperty("id").GetString()==BlastStages.GreedyTrap,"Practice uses selected authority layout");
        await MoveTo(9.5f,2.5f,110);await MoveTo(9.5f,5.5f,70);AtTier(1,"north stairs reach the ring");
        await Capture("greedy-trap-north-return-stairs");
        await MoveTo(5.5f,5.5f,80);await MoveTo(5.5f,6.5f,40);await MoveTo(7.5f,6.5f,60);AtTier(0,"northwest inner stairs reach dry bottom");
        await MoveTo(11.5f,6.5f,80);await MoveTo(13.5f,6.5f,60);AtTier(1,"northeast inner stairs regain ring");
        await MoveTo(13.5f,12.5f,100);await MoveTo(11.5f,12.5f,60);AtTier(0,"southeast inner stairs reach bottom");
        await Capture("greedy-trap-bottom-chamber");
        await MoveTo(7.5f,12.5f,80);await MoveTo(5.5f,12.5f,60);AtTier(1,"southwest inner stairs regain ring");
        await MoveTo(5.5f,13.5f,40);await MoveTo(9.5f,13.5f,80);await MoveTo(9.5f,16.5f,70);AtTier(2,"south stairs return to upper court");
        await Capture("greedy-trap-south-return-stairs");
        _root.Back();await Frames(3);
        await GreedyTrapLoot();
        _root.StartLocal(new SessionSettings(Stage:BlastStages.GreedyTrap,Bots:7,SuddenDeath:false),1);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player CPU battle starts",150);
        bool explosion=false;
        for(int i=0;i<240;i++)
        {await Frames(2);explosion|=State.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="blast");}
        Check(explosion,"real CPU match produces explosions on Greedy Trap");await Capture("greedy-trap-eight-player-battle");
        _root.Back();await Frames(3);
        _root.StartLocal(new SessionSettings(Stage:BlastStages.GreedyTrap,Bots:0,RoundSeconds:60,Ghosts:false),4);
        await WaitState(s=>s.GetProperty("closedLeft").GetInt32()>=2,"timed hurry closes the real side walls",240);
        await Frames(5);
        var arena=AuditDescendants(LightingArena).OfType<TempleStageView>().Single();
        var left=arena.GetNode<Node3D>("GreedyCrusherLeft");var right=arena.GetNode<Node3D>("GreedyCrusherRight");
        Check(arena.GetNode<Node3D>("GreedyExterior").Transform==Transform3D.Identity&&
            Math.Abs(arena.GetNode<MeshInstance3D>("GreedyExterior/GreedyReservoirWater").Position.Y-TempleStageView.GreedyMoatHeight)<.001f,
            "closing walls leave the decorative reservoir and town stationary");
        // Faces creep across the next column between closures: never behind the
        // lethal boundary and never more than the one column about to close.
        float LeftFace()=>left.Position.X+left.Scale.X*.5f;float RightFace()=>right.Position.X-right.Scale.X*.5f;
        int closedLeft=State.GetProperty("closedLeft").GetInt32(),closedRight=State.GetProperty("closedRight").GetInt32();
        Check(left.Visible&&right.Visible&&LeftFace()>=closedLeft-.01f&&LeftFace()<=closedLeft+1.01f,"left crushing face leads authority collision by at most the closing column");
        Check(RightFace()<=19-closedRight+.01f&&RightFace()>=18-closedRight-.01f,"right crushing face leads authority collision by at most the closing column");
        float leftFace=LeftFace(),rightFace=RightFace();await Frames(4);
        if(State.GetProperty("closedLeft").GetInt32()==closedLeft)
            Check(LeftFace()>leftFace+.01f&&RightFace()<rightFace-.01f,"crushing faces advance smoothly between authority closures");
        Check(Math.Abs(left.Position.X-left.Scale.X*.5f)<.01f&&Math.Abs(right.Position.X+right.Scale.X*.5f-19)<.01f,
            "solid compression volumes cover every closed column without false outside lanes");
        var gear=arena.GetChildren().OfType<Node3D>().First(n=>n.Name.ToString().StartsWith("GreedyFlywheel"));float angle=gear.Rotation.Z;
        await Frames(5);Check(Math.Abs(gear.Rotation.Z-angle)>.01f,"rear flywheels animate when the walls advance");
        await Capture("greedy-trap-animated-compression");
        await WaitState(s=>!s.GetProperty("players")[0].GetProperty("alive").GetBoolean(),"closing wall crushes a real upper-court player",90);
        await Capture("greedy-trap-compression-crush");
        _root.Back();await Frames(3);menu.Configure(new SessionSettings(),1);
        int CoverCount()=>Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!).Sum(b=>System.Numerics.BitOperations.PopCount((uint)b));
        void AtTier(float tier,string label)=>Check(Math.Abs(Player.GetProperty("y").GetSingle()-tier)<.03f,label);
    }

    private async Task GreedyTrapLoot()
    {
        var rules=new AdvancedRules{DropPercent=100,ItemSeconds=60,DisabledItems=string.Join(',',AdvancedRules.ItemTypes.Where(t=>t!="red"))};
        _root.StartLocal(new SessionSettings(Stage:BlastStages.GreedyTrap,Bots:0,SuddenDeath:false,Ghosts:false){Advanced=rules},3);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"three-human loot journey begins",150);
        await MoveTo(9.5f,2.5f,110);await MoveTo(9.5f,5.5f,70);await MoveTo(5.5f,5.5f,80);
        await MoveTo(5.5f,6.5f,40);await MoveTo(7.5f,6.5f,60);await MoveTo(9.5f,6.5f,60);await MoveTo(9.5f,7.5f,40);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);
        await MoveTo(9.5f,6.5f,40);await MoveTo(7.5f,6.5f,60);await MoveTo(5.5f,6.5f,60);
        await WaitState(s=>s.GetProperty("bombs").GetArrayLength()==0&&s.GetProperty("hazards").GetArrayLength()==0,"bottom bomb destroys masonry through its real fuse",150);
        Check(State.GetProperty("items").EnumerateArray().Any(i=>i.GetProperty("type").GetString()=="red"&&i.GetProperty("y").GetSingle()==0),"destroyed bottom masonry reveals real loot on the lowest story");
        var pickup=AuditDescendants(LightingArena).OfType<PowerupModel>().First();
        Check(Math.Abs(pickup.Position.Y-.48f)<.01f,"bottom reward mesh uses accepted floor height");
        await Capture("greedy-trap-real-bottom-loot");
        await MoveTo(7.5f,6.5f,60);await MoveTo(8.5f,6.5f,40);await MoveTo(8.5f,7.5f,40);
        Check(Player.GetProperty("bombType").GetString()=="red","ordinary pickup equips a reward from the pit");
        await Capture("greedy-trap-collected-reward");_root.Back();await Frames(3);
    }

    private async Task GreedyTrapNetworkTour()
    {
        foreach(var mode in new[]{NetworkMode.CreatePrivate,NetworkMode.CreatePeer})
        {
            var settings=new SessionSettings(Stage:BlastStages.GreedyTrap,Bots:0,SuddenDeath:false)
                {Hosting=mode==NetworkMode.CreatePeer?"peer":"server",Advanced=new AdvancedRules{BlockPercent=0}};
            await _root.StartOnlineAsync(new Uri(_server),"Greedy host",settings,mode);
            var host=(NetworkSession)_root.ActiveSession!;
            await using var peer=await NetworkSession.ConnectAsync(new Uri(_server),"Greedy observer",settings,NetworkMode.JoinCode,host.JoinCode);
            // Peer heartbeats and transport polling belong on the engine thread,
            // including while ordinary MoveTo inputs drive the host's stairs.
            Action pump=()=>peer.Advance(1d/30);GetTree().ProcessFrame+=pump;
            try
            {
            var clock=System.Diagnostics.Stopwatch.StartNew();
            while(host.Party?.CanStart!=true&&clock.Elapsed.TotalSeconds<20)
            {peer.Advance(1d/30);await Frames(1);await Task.Delay(16);}
            Check(host.Party?.CanStart==true,mode+" has two compatible humans and explicit start authority");host.RequestStart();
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,mode+" starts the real Greedy Trap authority",900);
            Check(_root.LatestStage!.Value.GetProperty("id").GetString()==BlastStages.GreedyTrap,mode+" renders the accepted Greedy Trap layout");
            await MoveTo(9.5f,2.5f,240);await MoveTo(9.5f,5.5f,240);
            Check(Math.Abs(Player.GetProperty("y").GetSingle()-1)<.03f,mode+" ordinary north stairs reach the ring");
            await MoveTo(5.5f,5.5f,240);await MoveTo(5.5f,6.5f,120);await MoveTo(7.5f,6.5f,240);
            Check(Player.GetProperty("y").GetSingle()==0,mode+" ordinary lower stairs reach the basin");
            Input(0,new PlayerInput(0,0,A:true));await Frames(3);Input(0,default);
            bool stageSeen=false,bottomSeen=false,bombSeen=false;clock.Restart();
            while(clock.Elapsed.TotalSeconds<8&&!(stageSeen&&bottomSeen&&bombSeen))
            {
                peer.Advance(1d/30);await Frames(1);await Task.Delay(16);
                foreach(var frame in peer.DrainMessages())
                {
                    var kind=frame.GetProperty("t").GetString();var data=frame.GetProperty("d");
                    if(kind=="stage")stageSeen|=data.GetProperty("id").GetString()==BlastStages.GreedyTrap;
                    if(kind!="state")continue;
                    bottomSeen|=data.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId&&p.GetProperty("y").GetSingle()==0);
                    bombSeen|=data.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("ownerId").GetString()==host.PlayerId&&b.GetProperty("y").GetSingle()==0);
                }
            }
            Check(stageSeen&&bottomSeen&&bombSeen,mode+" second client receives the real stage, lower player and lower bomb");
            await Capture("greedy-trap-network-"+mode.ToString().ToLowerInvariant());_root.Back();await Frames(3);
            }
            finally{GetTree().ProcessFrame-=pump;}
        }
    }
}

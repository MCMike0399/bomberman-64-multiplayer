using Godot;
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
    private async Task SeaSickTour()
    {
        Event("sea-sick-policy",new{ordinaryInputs=true,stateMutation=false,rocking="presentation parent only",current=false,sliding=false});
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        var quality=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        _root.Navigate("Setup");await Frames(8);
        var state=_root.FindChild("SetupSettings",true,false)!;
        var menu=(ArcadeMenu)state.GetParent().GetParent().GetParent();
        menu.Configure(new SessionSettings(),1);NamedButton("StageSelector").GrabFocus();
        for(int i=0;i<16&&menu.Settings.Stage!=BlastStages.SeaSick;i++)await KeyPress(Key.Right);
        Check(menu.Settings.Stage==BlastStages.SeaSick,"native arena selector reaches Sea Sick");
        var miniature=AuditDescendants(_root).OfType<TempleStageView>().Single(t=>t.GetParent().Name=="ArenaDiorama"&&t.IsVisibleInTree());
        Check(miniature.Layout.Id==BlastStages.SeaSick&&miniature.Scenery==null,"Sea Sick miniature uses the actual ship without exterior life");
        Check(AuditDescendants(miniature).Any(n=>n.Name.ToString().StartsWith("MainMast")),"setup miniature retains the ship's signature mast");
        await Capture("sea-sick-menu-miniature");

        _root.StartLocal(new SessionSettings(Stage:BlastStages.SeaSick,Bots:0,SuddenDeath:false),4);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ordinary four-player Sea Sick battle starts",150);
        Check(State.GetProperty("players").GetArrayLength()==4&&Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!).Sum(b=>System.Numerics.BitOperations.PopCount((uint)b))==48,"four original corner starts receive generous barrel groves");
        var arena=LightingArena;var terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
        Check(AuditDescendants(arena).Count(n=>n.HasMeta("sea_sick_barrel"))==48,"actual destructible cover renders as forty-eight barrels");
        Check(AuditDescendants(terrain).Count(n=>n.Name.ToString().StartsWith("ForwardPost"))==2,"actual deck has two short forward posts");
        var problems=AuditModelProblems(terrain,out int meshes,out int vertices);
        Check(problems.Count==0&&meshes>100,$"ship geometry is finite, correctly wound and textured: {string.Join("; ",problems.Take(4))}");
        var ocean=terrain.GetNode<MeshInstance3D>("ShipOcean");var seaPose=ocean.GlobalTransform;
        var signal=terrain.GetNode<Node3D>("MastSignalPennant");var signalPose=signal.Transform;

        var shipPose=arena.StageAssemblyTransform;var actor=Player.Clone();await Frames(70);
        Check(!arena.StageAssemblyTransform.IsEqualApprox(shipPose)&&ocean.GlobalTransform.IsEqualApprox(seaPose),"ship rocks coherently against a stationary ocean");
        Check(!signal.Transform.IsEqualApprox(signalPose),"core mast signal pennant shares the pausable ship life clock");
        if(!_headless)
        {
            var cloth=signal.GetNode<MeshInstance3D>("CutterSignalCloth");var camera=GetViewport().GetCamera3D()!;
            var viewport=GetViewport().GetVisibleRect().Size;
            var points=Enumerable.Range(0,8).Select(i=>camera.UnprojectPosition(cloth.ToGlobal(cloth.Mesh.GetAabb().GetEndpoint(i)))/viewport).ToArray();
            Check(points.All(p=>p.X>.025f&&p.X<.975f&&p.Y>.145f&&p.Y<.90f),"mast signal pennant is actually framed below the HUD in the ordinary battle camera");
        }
        Check(Player.GetProperty("x").GetSingle()==actor.GetProperty("x").GetSingle()&&Player.GetProperty("z").GetSingle()==actor.GetProperty("z").GetSingle(),"rendered ship motion leaves accepted idle positions unchanged");
        foreach(var point in new[]{new System.Numerics.Vector3(4.5f,0,6.5f),new System.Numerics.Vector3(1.5f,1,7.5f),new System.Numerics.Vector3(17.5f,1,7.5f)})
        {
            var hit=arena.ArenaPoint(arena.ScreenPoint(point));
            Check(hit.HasValue&&System.Numerics.Vector2.Distance(hit.Value,new(point.X,point.Z))<.025f,"pointer projection picks the rendered rocking deck at "+point);
        }
        foreach(var size in new[]{new Vector2I(1280,800),new Vector2I(1280,720)})
        {if(!_headless)await ResizeWindow(size);CheckFraming(BlastStages.SeaSick,GetWindow().Size);await Capture($"sea-sick-opening-{size.X}x{size.Y}");}
        if(!_headless)await ResizeWindow(new(1280,800));
        foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High})
        {
            await SelectGraphics(preset);
            Check(terrain.Scenery!.IsVisibleInTree()==(preset!=GraphicsPreset.Low),"Sea Sick scenery follows "+preset);
            Check(signal.IsVisibleInTree(),"signal pennant remains core on "+preset);
            Check(((ShaderMaterial)ocean.MaterialOverride).Shader==ArtMaterials.SeaWaterShaderFor(preset),"thin hull contact keeps all shared ocean features at "+preset);
            Check(terrain.GetNode<OmniLight3D>("SternLantern/SternLanternGlow").IsVisibleInTree()==(preset!=GraphicsPreset.Low),"stern lantern keeps emissive-only Low policy at "+preset);
            await Capture("sea-sick-"+GraphicsQuality.ToWire(preset));
        }
        await SelectLocalLighting(StageTime.Night);await Capture("sea-sick-night");await SelectLocalLighting(lighting);
        await KeyPress(Key.Escape);Check(_root.ScreenName=="Pause","Sea Sick pauses through the ordinary menu");
        var paused=arena.StageAssemblyTransform;int snapshots=_root.SnapshotCount;await Frames(20);
        Check(arena.StageAssemblyTransform.IsEqualApprox(paused)&&_root.SnapshotCount==snapshots,"local pause freezes both ship motion and authority");
        var pausedSignal=signal.Transform;await Frames(12);
        Check(signal.Transform.IsEqualApprox(pausedSignal),"ordinary pause freezes the core signal pennant too");
        await ActivateNamed("ResumeGame");await Frames(12);
        Check(!arena.StageAssemblyTransform.IsEqualApprox(paused),"resuming restarts the gentle ship sway");
        if(!_headless)
        {
            var camera=GetViewport().GetCamera3D()!;var pose=camera.Transform;
            camera.Position=new(25,17,28);camera.LookAt(new Vector3(9.5f,.5f,9.5f));
            await Capture("sea-sick-oblique-inspection");camera.Transform=pose;
        }
        _root.Back();await Frames(3);

        _root.StartLocal(new SessionSettings(Stage:BlastStages.SeaSick,Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"cleared ship exploration starts",150);
        await GrappleWalk(0,4,4);await GrappleWalk(0,1,4);
        Check(Math.Abs(Player.GetProperty("y").GetSingle()-1)<.02f,"ordinary port stairs reach the side gallery");
        await Capture("sea-sick-port-gallery");
        await GrappleWalk(0,1,14);await GrappleWalk(0,4,14);await GrappleWalk(0,14,14);await GrappleWalk(0,17,14);
        Check(Math.Abs(Player.GetProperty("y").GetSingle()-1)<.02f,"ordinary starboard stairs reach the opposite gallery");
        Input(0,new PlayerInput(1,0));await Frames(45);Input(0,default);
        Check(Player.GetProperty("x").GetSingle()<18&&Player.GetProperty("alive").GetBoolean(),"gunwale blocks actual walking off the ship");
        await Capture("sea-sick-cleared-deck");
        Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(2);
        Input(0,new PlayerInput(0,0,B:true,Aim:true,AimTarget:new System.Numerics.Vector2(13.5f,14.5f)));await Frames(6);
        Check(Player.GetProperty("carrying").GetString()=="bomb","ordinary gallery carry creates a real throw guide");
        var guide=AuditDescendants(LightingArena).OfType<BombThrowPreview>().Single();
        var endpoint=AuditDescendants(guide).OfType<ThrowEndpointReticle>().Single();
        var marker=guide.GetChildren().OfType<MeshInstance3D>().Last();
        var projected=GetViewport().GetCamera3D()!.UnprojectPosition(marker.GlobalPosition);
        Check(!endpoint.EdgeClamped&&endpoint.Position.DistanceTo(projected)<.75f,"screen reticle stays on the actual rocking-deck landing marker");
        await Capture("sea-sick-gallery-aim");Input(0,default);_root.Back();await Frames(3);
        await SeaSickLootBattle();

        _root.StartLocal(new SessionSettings(Stage:BlastStages.SeaSick,Bots:7,SuddenDeath:false),1);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player ship CPU battle starts",150);
        bool bomb=false,blast=false;
        for(int i=0;i<300&&_root.ScreenName=="Match";i++)
        {bomb|=State.GetProperty("bombs").GetArrayLength()>0;blast|=State.GetProperty("hazards").GetArrayLength()>0;await Frames(1);}
        Check(bomb&&blast,"Sea Sick CPU battle navigates and produces real bombs and blasts");
        await Capture("sea-sick-eight-player-combat");_root.Back();await Frames(3);

        _root.StartLocal(new SessionSettings(Stage:BlastStages.SeaSick,Bots:0,RoundSeconds:60,Ghosts:false),3);
        await WaitState(s=>s.GetProperty("meteors").GetArrayLength()>0,"Sea Sick Hurry publishes a fixed fireball target",180);
        Check(State.GetProperty("hurryMode").GetString()=="meteors"&&State.GetProperty("closedLeft").GetInt32()==0,"fireballs replace closing ship walls");
        await Capture("sea-sick-fireball-warning");Input(0,new PlayerInput(0,1));await Frames(25);Input(0,default);
        await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"ship fireball reaches its real impact",90);
        Check(Player.GetProperty("alive").GetBoolean(),"ordinary movement escapes the fixed ship fireball target");
        await Capture("sea-sick-fireball-impact");_root.Back();await Frames(3);
        await SelectGraphics(quality);menu.Configure(new SessionSettings(),1);_root.Navigate("Modes");
    }

    private async Task SeaSickLootBattle()
    {
        var stage=BlastStages.Get(BlastStages.SeaSick);
        _root.StartLocal(new SessionSettings(Stage:stage.Id,Bots:0,SuddenDeath:false,Ghosts:false)
            {Advanced=new AdvancedRules{DropPercent=100,DisabledItems="remote,red,bounce,heart,focus,tiny,big,haste,slow"}},2);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ordinary ship loot battle starts",150);
        bool Covered((int X,int Z) c){var bits=Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!);int i=c.Z*19+c.X;return (bits[i/8]&(1<<(i%8)))!=0;}
        var start=(X:(int)Player.GetProperty("x").GetSingle(),Z:(int)Player.GetProperty("z").GetSingle());
        var opening=LootReachable(stage,start,Covered,7).OrderBy(c=>c.Value)
            .SelectMany(c=>new[]{(1,0),(-1,0),(0,1),(0,-1)}.Select(d=>(From:c.Key,Cover:(X:c.Key.X+d.Item1,Z:c.Key.Z+d.Item2))))
            .First(c=>stage.At(c.Cover.X,c.Cover.Z).IsPassable&&Covered(c.Cover));
        await GrappleWalk(0,opening.From.X,opening.From.Z);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
        Check(State.GetProperty("bombs").GetArrayLength()==1,"ordinary primary tap places one deck bomb");
        var retreat=LootReachable(stage,opening.From,Covered,6).Where(c=>(c.Key.X-opening.From.X)*(c.Key.X-opening.From.X)+(c.Key.Z-opening.From.Z)*(c.Key.Z-opening.From.Z)>=9).OrderBy(c=>c.Value).First().Key;
        await GrappleWalk(0,retreat.X,retreat.Z);
        await WaitState(s=>s.GetProperty("bombs").GetArrayLength()==0&&s.GetProperty("hazards").GetArrayLength()==0,"real ship fuse and blast finish",180);
        Check(!Covered(opening.Cover)&&Player.GetProperty("alive").GetBoolean(),"real explosion breaks a barrel and retreat survives");
        Check(State.GetProperty("items").EnumerateArray().Any(i=>i.GetProperty("cx").GetInt32()==opening.Cover.X&&i.GetProperty("cz").GetInt32()==opening.Cover.Z),"destroyed barrel reveals a real upgrade");
        await Capture("sea-sick-barrel-reward");long serial=Player.GetProperty("pickupSerial").GetInt64();
        await GrappleWalk(0,opening.Cover.X,opening.Cover.Z);
        Check(Player.GetProperty("pickupSerial").GetInt64()>serial,"ordinary walking collects the barrel upgrade");
        await Capture("sea-sick-collected-upgrade");_root.Back();await Frames(3);
    }

    private async Task SeaSickNetworkTour()
    {
        foreach(var mode in new[]{NetworkMode.CreatePrivate,NetworkMode.CreatePeer})
        {
            var settings=new SessionSettings(Stage:BlastStages.SeaSick,Bots:0,SuddenDeath:false)
                {Hosting=mode==NetworkMode.CreatePeer?"peer":"server",Advanced=new AdvancedRules{BlockPercent=0}};
            await _root.StartOnlineAsync(new Uri(_server),"Ship host",settings,mode);
            var host=(NetworkSession)_root.ActiveSession!;
            await using var peer=await NetworkSession.ConnectAsync(new Uri(_server),"Ship observer",settings,NetworkMode.JoinCode,host.JoinCode);
            Action pump=()=>peer.Advance(1d/30);GetTree().ProcessFrame+=pump;
            try
            {
                var clock=System.Diagnostics.Stopwatch.StartNew();
                while(host.Party?.CanStart!=true&&clock.Elapsed.TotalSeconds<20){await Frames(1);await Task.Delay(16);}
                Check(host.Party?.CanStart==true,mode+" Sea Sick room admits two compatible humans");host.RequestStart();
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,mode+" starts real ship authority",900);
                await MoveTo(4.5f,4.5f,240);await MoveTo(1.5f,4.5f,240);
                Check(Math.Abs(Player.GetProperty("y").GetSingle()-1)<.03f,mode+" ordinary port stairs reach the gallery");
                Input(0,new PlayerInput(0,0,A:true));await Frames(3);Input(0,default);
                bool stageSeen=false,gallerySeen=false,bombSeen=false;clock.Restart();
                while(clock.Elapsed.TotalSeconds<8&&!(stageSeen&&gallerySeen&&bombSeen))
                {
                    await Frames(1);await Task.Delay(16);
                    foreach(var frame in peer.DrainMessages())
                    {
                        var kind=frame.GetProperty("t").GetString();var data=frame.GetProperty("d");
                        if(kind=="stage")stageSeen|=data.GetProperty("id").GetString()==BlastStages.SeaSick;
                        if(kind!="state")continue;
                        gallerySeen|=data.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId&&Math.Abs(p.GetProperty("y").GetSingle()-1)<.03f);
                        bombSeen|=data.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("ownerId").GetString()==host.PlayerId&&Math.Abs(b.GetProperty("y").GetSingle()-1)<.03f);
                    }
                }
                Check(stageSeen&&gallerySeen&&bombSeen,mode+" observer receives the ship, gallery player and supported bomb");
                await Capture("sea-sick-network-"+mode.ToString().ToLowerInvariant());_root.Back();await Frames(3);
            }
            finally{GetTree().ProcessFrame-=pump;}
        }
    }
}

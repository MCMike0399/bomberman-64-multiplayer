using Godot;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Rendering;
using BlastCircuit.UI.Menu;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task LostAtSeaMaterialWitness()
    {
        var original=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            if(!_headless){await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Midday);}
            _root.StartLocal(new SessionSettings(Stage:BlastStages.LostAtSea,Bots:0,SuddenDeath:false)
                {Advanced=new AdvancedRules{BlockPercent=0}},8);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"material witness enters an ordinary eight-human clear-deck battle",150);
            await GrappleWalk(3,18,15);
            foreach(int seat in new[]{3,6})
            {
                Input(seat,new PlayerInput(-1,0));
                await WaitState(_=>GrapplePlayer(seat).GetProperty("x").GetSingle()<=16.5f,"ordinary input moves suit onto glass: "+seat,80);
                Input(seat,default);await Frames(45);
                var player=GrapplePlayer(seat);
                Check(player.GetProperty("x").GetSingle() is >=3 and <18&&player.GetProperty("z").GetSingle() is >=3 and <18,
                    "blue/purple suit really stands on the physical glass floor: "+seat);
            }
            await Capture("glass-material-blue-purple-high-midday");
            if(!_headless)await SelectLocalLighting(StageTime.Night);
            await Capture("glass-material-blue-purple-high-night");
            if(!_headless){await SelectGraphics(GraphicsPreset.Medium);await SelectLocalLighting(StageTime.Afternoon);}
            await Capture("glass-material-blue-purple-medium-afternoon");
            if(!_headless){await SelectGraphics(GraphicsPreset.Low);await SelectLocalLighting(StageTime.Midday);}
            await Capture("glass-material-blue-purple-low-midday");
        }
        finally
        {
            foreach(int seat in new[]{0,3,6})Input(seat,default);
            if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
            if(!_headless){await SelectGraphics(original);await SelectLocalLighting(lighting);}
        }
    }

    private async Task LostAtSeaSlideWitness()
    {
        Event("glass-slide-witness-policy",new{ordinaryInputs=true,stateMutation=false,
            scope="real player glass travel and rim traction; real kicked shot resting at the far rim/boundary"});
        _root.StartLocal(new SessionSettings(Stage:BlastStages.LostAtSea,Bots:0,SuddenDeath:false),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"slide witness enters normal clear-deck Practice",150);
        await GrappleWalk(0,2,8);Input(0,new PlayerInput(1,0));
        await WaitState(_=>Player.GetProperty("x").GetSingle()>=10,"actual player crosses the glass",150);
        await Capture("glass-slide-player-crossing",0);
        await WaitState(_=>Player.GetProperty("x").GetSingle()>=18.1f,"actual player reaches the grippy rim",150);
        Input(0,default);await Frames(3);float stopped=Player.GetProperty("x").GetSingle();await Frames(12);
        Check(Math.Abs(Player.GetProperty("x").GetSingle()-stopped)<.01f,"player release on the rim stops without glass coast");
        await Capture("glass-slide-player-stopped-on-rim",0);
        Input(0,new PlayerInput(-1,0));
        await WaitState(_=>Player.GetProperty("x").GetSingle()<=10.5f,"ordinary return reaches the middle of the glass",150);
        Input(0,default);await Frames(45);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
        Input(0,new PlayerInput(-1,0,A:true));await Frames(2);Input(0,default);
        await WaitState(s=>s.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==1),"ordinary kick sends a real bomb over the glass",30);
        await Capture("glass-slide-real-kicked-shot",0);
        await WaitState(s=>s.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==0&&b.GetProperty("x").GetSingle()<3),
            "real kicked shot comes to rest at the far rim/boundary",90);
        await Capture("glass-slide-shot-resting-at-far-rim",0);
        await Frames(24);Input(0,default);_root.Back();await Frames(3);
    }

    private async Task LostAtSeaDepthWitness()
    {
        Check(!_headless,"under-glass visibility requires an actual renderer, never scene-node presence");
        Event("glass-depth-witness-policy",new{ordinaryInputs=true,stateMutation=false,
            controls="labelled presentation-only hidden-subject comparisons after natural later-phase motion"});
        var original=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            await SelectGraphics(GraphicsPreset.High);await SelectLocalLighting(StageTime.Midday);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.LostAtSea,Bots:0,SuddenDeath:false),1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"depth witness enters ordinary clear-deck Practice",150);
            await Frames(1080);
            await Capture("glass-buoy-depth-world-later-phase");
            var arena=_root.GetChildren().OfType<ArenaView>().Single();var terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
            bool processing=terrain.IsProcessing();terrain.SetProcess(false);
            try
            {
                foreach(string name in new[]{"MantaRay","SunkenWreck"})
                {
                    var subject=AuditDescendants(terrain).OfType<Node3D>().Single(n=>n.Name==name);
                    var bounds=AuditSubtreeBounds(subject,subject.GetParent<Node3D>().GlobalTransform);
                    var camera=_root.GetViewport().GetCamera3D()!;
                    var min=new Vector2(float.MaxValue,float.MaxValue);var max=new Vector2(float.MinValue,float.MinValue);
                    for(int i=0;i<8;i++)
                    {var screen=camera.UnprojectPosition(bounds.GetEndpoint(i));min=min.Min(screen);max=max.Max(screen);}
                    await Capture("glass-depth-"+name+"-visible");string visible=_screenshots[^1];
                    try{subject.Visible=false;await Capture("glass-depth-"+name+"-hidden-control");}
                    finally{subject.Visible=true;}
                    using var before=Image.LoadFromFile(visible);using var after=Image.LoadFromFile(_screenshots[^1]);
                    var logical=_root.GetViewport().GetVisibleRect().Size;
                    var scale=new Vector2(before.GetWidth()/logical.X,before.GetHeight()/logical.Y);
                    var roi=new Rect2(min*scale,(max-min)*scale).Grow(10);
                    int x0=Math.Clamp((int)roi.Position.X,0,before.GetWidth()-1),x1=Math.Clamp((int)roi.End.X,0,before.GetWidth());
                    int y0=Math.Clamp((int)roi.Position.Y,0,before.GetHeight()-1),y1=Math.Clamp((int)roi.End.Y,0,before.GetHeight());
                    int changed=0;float maximum=0;
                    for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)
                    {
                        var a=before.GetPixel(x,y);var b=after.GetPixel(x,y);
                        float delta=Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)))*255;
                        maximum=Math.Max(maximum,delta);if(delta>=6)changed++;
                    }
                    Event("glass-depth-pixel-witness",new{name,changed,maximum,roi=new{x0,x1,y0,y1},
                        scope="visible-image contrast only; recognition and art approval require image review"});
                    Check(changed>=200&&maximum>=10,name+" visibly affects rendered pixels through the glass at the normal battle camera");
                    await Capture("glass-depth-"+name+"-restored");
                }
            }
            finally{terrain.SetProcess(processing);}
            await Frames(90);await Capture("glass-buoy-depth-world-moving-restored");
        }
        finally
        {
            Input(0,default);
            if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
            await SelectGraphics(original);await SelectLocalLighting(lighting);
        }
    }

    private async Task LostAtSeaShapeStudy()
    {
        Event("glass-buoy-shape-policy",new{ordinaryInputs=true,stateMutation=false,artApproval=false,
            brief="Fable Glass Buoy: massing and three values before decorative finishing"});
        var original=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            foreach(var (preset,time,label) in new[]{(GraphicsPreset.High,StageTime.Midday,"high-midday"),
                (GraphicsPreset.High,StageTime.Night,"high-night"),(GraphicsPreset.Low,StageTime.Midday,"low-midday")})
            {
                if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
                if(!_headless){await SelectGraphics(preset);await SelectLocalLighting(time);}
                _root.StartLocal(new SessionSettings(Stage:BlastStages.LostAtSea,Bots:7,SuddenDeath:false),1);
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"Glass Buoy shape study enters "+label,150);
                var arena=_root.GetChildren().OfType<ArenaView>().Single();
                var terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
                Check(AuditDescendants(terrain).OfType<Node3D>().Count(n=>n.Name.ToString().StartsWith("GlassBuoyCornerFloat"))==4,
                    "shape study consumes the rebuilt pontoon and four fat corner floats");
                var ocean=terrain.GetChildren().OfType<MeshInstance3D>().Single(n=>n.Name=="ObservationSea");
                Check(((ShaderMaterial)ocean.MaterialOverride).Shader==ArtMaterials.WaterShaderFor(GraphicsQuality.Current),
                    "real shared premium ocean remains part of arena terrain on "+label);
                CheckClockwiseMeshes(AuditDescendants(terrain).OfType<MeshInstance3D>()
                    .Where(n=>n.Name=="SlipperyVioletGlass"||n.Name.ToString().StartsWith("GlassBuoy")||n.Name=="ObservationSea")
                    .Select(n=>(n.Mesh,n.Name.ToString(),n.Name=="SlipperyVioletGlass"||n.Name=="GlassBuoyRimDeck"||n.Name=="ObservationSea")));
                var intrusions=SceneryIntrusions(terrain);
                Check(intrusions.Count==0,"wreck, manta and bell buoy stay outside the player's standing volume");
                CheckFraming(BlastStages.LostAtSea,GetWindow().Size);
                await WaitState(s=>s.GetProperty("hazards").EnumerateArray().Any(h=>h.GetProperty("x").GetSingle() is >=3 and <18&&h.GetProperty("z").GetSingle() is >=3 and <18),
                    "ordinary eight-player combat produces a real blast on the glass",600);
                Check(State.GetProperty("players").GetArrayLength()==8,"shape study retains the actual eight-player battle roster");
                await Capture("glass-buoy-shape-"+label+"-combat");
                if(!_headless&&time==StageTime.Midday&&preset==GraphicsPreset.High)
                {
                    var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
                    var (h,v)=(camera.HOffset,camera.VOffset);
                    try
                    {
                        camera.HOffset=0;camera.VOffset=0;
                        camera.Position=new(terrain.Width+7,16,terrain.Depth+14);
                        camera.LookAt(new Vector3(terrain.Width/2f,-.45f,terrain.Depth/2f));
                        await Capture("glass-buoy-shape-oblique-inspection");
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

    private async Task LostAtSeaTour()
    {
        Event("lost-at-sea-policy",new{ordinaryInputs=true,stateMutation=false,reference="official guide p107",surface="slippery violet glass with nonslip silver rim"});
        var original=_root.Preferences.Graphics;var lighting=_root.Preferences.StageLighting;
        try
        {
            _root.Navigate("Setup");await Frames(5);
            var menu=_root.FindChildren("*","",true,false).OfType<ArcadeMenu>().Single();
            menu.Configure(new SessionSettings(Stage:BlastStages.LostAtSea),1);await Frames(8);
            Check(menu.Settings.Stage==BlastStages.LostAtSea,"native setup selects Lost at Sea");await Capture("lost-at-sea-menu-miniature");
            _root.StartLocal(new SessionSettings(Stage:BlastStages.LostAtSea,Bots:0,SuddenDeath:false),4);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"four-player observation deck starts",150);
            Check(State.GetProperty("players").GetArrayLength()==4,"original four-corner opening is supported");
            await Capture("lost-at-sea-four-player-opening");_root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.LostAtSea,Bots:0,SuddenDeath:false),8);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player observation deck starts",150);
            Check(State.GetProperty("players").GetArrayLength()==8,"observation deck supports eight ordinary rim starts");
            var arena=_root.GetChildren().OfType<ArenaView>().Single();
            var terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
            Check(AuditDescendants(terrain).OfType<Node3D>().Count(n=>n.Name.ToString().StartsWith("CoralOrangeTower"))==4,"actual deck renders four tall orange towers");
            Check(AuditDescendants(arena).OfType<Node3D>().Count(n=>n.Name.ToString().StartsWith("ObservationCargo"))==32,"actual accepted cargo renders as thirty-two X-braced crates");
            Check(AuditDescendants(terrain).OfType<MeshInstance3D>().Any(n=>n.Name=="SlipperyVioletGlass"),"actual play surface uses original violet observation glass");
            CheckClockwiseMeshes(AuditDescendants(terrain).OfType<MeshInstance3D>()
                .Where(n=>n.Name=="SlipperyVioletGlass"||n.Name.ToString().StartsWith("GlassBuoy")||n.Name=="ObservationSea")
                .Select(n=>(n.Mesh,n.Name.ToString(),n.Name=="SlipperyVioletGlass"||n.Name=="GlassBuoyRimDeck"||n.Name=="ObservationSea")));
            CheckFraming(BlastStages.LostAtSea,GetWindow().Size);await Capture("lost-at-sea-eight-player-opening");
            foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High})
            {
                if(!_headless)await SelectGraphics(preset);
                await Capture("lost-at-sea-"+GraphicsQuality.ToWire(preset));
            }
            var school=AuditDescendants(terrain).OfType<Node3D>().First(n=>n.Name=="MantaRay");
            var fishBefore=school.Position;await Frames(30);
            Check(school.Position!=fishBefore,"the large manta mass moves beneath the observation deck");await Capture("lost-at-sea-marine-life-motion");
            if(!_headless)foreach(var time in new[]{StageTime.Morning,StageTime.Afternoon,StageTime.Night})
            {await SelectLocalLighting(time);await Capture("lost-at-sea-lighting-"+StageLighting.ToWire(time));}
            _root.Back();await Frames(3);

            _root.StartLocal(new SessionSettings(Stage:BlastStages.LostAtSea,Bots:0,SuddenDeath:false),1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"slippery glass exploration starts",150);
            await GrappleWalk(0,2,8);
            Input(0,new PlayerInput(0,1));await Frames(8);Input(0,default);await Frames(3);
            float rimZ=Player.GetProperty("z").GetSingle();await Frames(12);
            Check(Math.Abs(Player.GetProperty("z").GetSingle()-rimZ)<.01f,"ordinary release stops immediately on the silver rim");
            await GrappleWalk(0,2,8);Input(0,new PlayerInput(1,0));
            await WaitState(_=>Player.GetProperty("x").GetSingle()>=8,"ordinary movement enters the violet glass",100);
            float releaseX=Player.GetProperty("x").GetSingle();Input(0,default);await Frames(10);
            Check(Player.GetProperty("x").GetSingle()>releaseX+.5f,"actual authority coasts after stick release on glass");
            await Capture("lost-at-sea-real-glass-coast");await Frames(45);
            float stopped=Player.GetProperty("x").GetSingle();await Frames(8);
            Check(Math.Abs(Player.GetProperty("x").GetSingle()-stopped)<.02f,"neutral glass friction eventually settles actual movement");

            Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
            Check(State.GetProperty("bombs").GetArrayLength()==1,"ordinary bomb placement is stable on glass");
            Input(0,new PlayerInput(1,0,A:true));await Frames(2);Input(0,default);
            await WaitState(s=>s.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==1),"ordinary primary kick launches the glass bomb",30);
            var moving=State.GetProperty("bombs")[0];float bombX=moving.GetProperty("x").GetSingle(),fuse=moving.GetProperty("fuse").GetSingle();
            await Frames(8);
            Check(State.GetProperty("bombs")[0].GetProperty("x").GetSingle()>bombX+1&&State.GetProperty("bombs")[0].GetProperty("fuse").GetSingle()<fuse,"kicked glass bomb keeps momentum and its real fuse");
            await Capture("lost-at-sea-kicked-bomb-glass-slide");
            await WaitState(s=>s.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==0&&b.GetProperty("x").GetSingle()>=18),
                "the actual kicked glass shot comes to rest in the outer rim/boundary region",75);
            await Capture("lost-at-sea-kicked-bomb-resting-at-rim",0);
            _root.Back();await Frames(3);

            _root.StartLocal(new SessionSettings(Stage:BlastStages.LostAtSea,Bots:7,SuddenDeath:false),1);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player slippery CPU battle starts",150);
            bool bombs=false,blasts=false;
            for(int tick=0;tick<300&&_root.ScreenName=="Match";tick++)
            {bombs|=State.GetProperty("bombs").GetArrayLength()>0;blasts|=State.GetProperty("hazards").GetArrayLength()>0;await Frames(1);}
            Check(bombs&&blasts,"real eight-player CPUs navigate glass and generate bombs and explosions");await Capture("lost-at-sea-eight-player-combat");
            _root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:BlastStages.LostAtSea,Bots:0,RoundSeconds:60,Ghosts:false),3);
            await WaitState(s=>s.GetProperty("meteors").GetArrayLength()>0,"observation-deck Hurry publishes a fireball warning",180);
            Check(State.GetProperty("hurryMode").GetString()=="meteors"&&State.GetProperty("closedLeft").GetInt32()==0,"slippery observation deck preserves meteor Hurry");
            await Capture("lost-at-sea-fireball-warning");
            await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"actual fireball reaches the glass court",80);
            await Capture("lost-at-sea-fireball-impact");
        }
        finally
        {
            Input(0,default);Input(1,default);
            if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
            if(!_headless){await SelectGraphics(original);await SelectLocalLighting(lighting);}
        }
    }

    private async Task LostAtSeaNetworkTour()
    {
        foreach(var mode in new[]{NetworkMode.CreatePrivate,NetworkMode.CreatePeer})
        {
            var settings=new SessionSettings(Stage:BlastStages.LostAtSea,Bots:0,SuddenDeath:false)
                {Hosting=mode==NetworkMode.CreatePeer?"peer":"server",Advanced=new AdvancedRules{BlockPercent=0}};
            await _root.StartOnlineAsync(new Uri(_server),"Sea host",settings,mode);
            var host=(NetworkSession)_root.ActiveSession!;
            await using var observer=await NetworkSession.ConnectAsync(new Uri(_server),"Sea observer",settings,NetworkMode.JoinCode,host.JoinCode);
            Action pump=()=>observer.Advance(1d/30);GetTree().ProcessFrame+=pump;
            try
            {
                var clock=System.Diagnostics.Stopwatch.StartNew();
                while(host.Party?.CanStart!=true&&clock.Elapsed.TotalSeconds<20){await Frames(1);await Task.Delay(16);}
                Check(host.Party?.CanStart==true,mode+" observation room admits two compatible humans");host.RequestStart();
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,mode+" starts the real glass authority",900);
                Check(_root.LatestStage!.Value.GetProperty("id").GetString()==BlastStages.LostAtSea,mode+" renders the actual observation-deck stage");
                await MoveTo(2.5f,8.5f,240);Input(0,new PlayerInput(1,0));
                clock.Restart();
                while(Player.GetProperty("x").GetSingle()<8&&clock.Elapsed.TotalSeconds<8){await Frames(1);await Task.Delay(16);}
                Check(Player.GetProperty("x").GetSingle()>=8,mode+" ordinary input crosses the rim onto glass");
                Input(0,default);float released=Player.GetProperty("x").GetSingle();clock.Restart();
                while(Player.GetProperty("x").GetSingle()<released+.35f&&clock.Elapsed.TotalSeconds<3){await Frames(1);await Task.Delay(16);}
                Check(Player.GetProperty("x").GetSingle()>released+.3f,mode+" authoritative glass inertia survives actual network input release");
                bool stageSeen=false,coastSeen=false;clock.Restart();
                while(clock.Elapsed.TotalSeconds<5&&!(stageSeen&&coastSeen))
                {
                    await Frames(1);await Task.Delay(16);
                    foreach(var frame in observer.DrainMessages())
                    {
                        var kind=frame.GetProperty("t").GetString();var data=frame.GetProperty("d");
                        if(kind=="stage")stageSeen|=data.GetProperty("id").GetString()==BlastStages.LostAtSea;
                        if(kind=="state")coastSeen|=data.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId&&p.GetProperty("x").GetSingle()>released+.3f);
                    }
                }
                Check(stageSeen&&coastSeen,mode+" second client observes the real stage and inertial ground movement");
                await Capture("lost-at-sea-network-"+mode.ToString().ToLowerInvariant());
            }
            finally{Input(0,default);GetTree().ProcessFrame-=pump;if(_root.ActiveSession!=null){_root.Back();await Frames(3);}}
        }
    }
}

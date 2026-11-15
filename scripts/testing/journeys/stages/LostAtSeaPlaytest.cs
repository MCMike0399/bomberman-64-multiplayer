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
            _root.StartLocal(new SessionSettings(Stage:BlastStages.LostAtSea,Bots:0,SuddenDeath:false),8);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player observation deck starts",150);
            Check(State.GetProperty("players").GetArrayLength()==8,"observation deck supports eight ordinary rim starts");
            var arena=_root.GetChildren().OfType<ArenaView>().Single();
            var terrain=AuditDescendants(arena).OfType<TempleStageView>().Single();
            Check(AuditDescendants(terrain).OfType<Node3D>().Count(n=>n.Name.ToString().StartsWith("CoralOrangeTower"))==4,"actual deck renders four tall orange towers");
            Check(AuditDescendants(arena).OfType<Node3D>().Count(n=>n.Name.ToString().StartsWith("ObservationCargo"))==32,"actual accepted cargo renders as thirty-two X-braced crates");
            Check(AuditDescendants(terrain).OfType<MeshInstance3D>().Any(n=>n.Name=="SlipperyVioletGlass"),"actual play surface uses original violet observation glass");
            CheckFraming(BlastStages.LostAtSea,GetWindow().Size);await Capture("lost-at-sea-eight-player-opening");
            foreach(var preset in new[]{GraphicsPreset.Low,GraphicsPreset.Medium,GraphicsPreset.High})
            {
                if(!_headless)await SelectGraphics(preset);
                await Capture("lost-at-sea-"+GraphicsQuality.ToWire(preset));
            }
            var school=AuditDescendants(terrain).OfType<Node3D>().First(n=>n.Name.ToString().StartsWith("ObservationFishSchool"));
            var fishBefore=school.Position;await Frames(30);
            Check(school.Position!=fishBefore,"real marine scenery swims beneath the observation deck");await Capture("lost-at-sea-marine-life-motion");
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

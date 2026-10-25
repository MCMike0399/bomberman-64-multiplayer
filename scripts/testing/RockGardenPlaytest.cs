using Godot;
using Bomber.Client;
using Bomber.Game;
using Bomber.Game.Simulation;
using BlastCircuit.Arena;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task RockGardenTour()
    {
        var stage=BlastStages.Get(BlastStages.RockGarden);
        Input(0,default);Input(1,default);
        _root.StartLocal(new SessionSettings(Stage:stage.Id,Bots:0,SuddenDeath:false),4);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"garden four-player battle starts",150);
        Check(State.GetProperty("players").GetArrayLength()==4,"garden quadrant starts support ordinary four-player battle");
        Check(_root.FindChildren("*","",true,false).OfType<Node3D>().Count(n=>n.Name.ToString().StartsWith("GardenColumn"))==5,"actual garden renders five round columns");
        Check(_root.FindChildren("*","",true,false).OfType<Node3D>().Count(n=>n.Name.ToString().StartsWith("GardenBoulder"))==16,"actual garden renders sixteen destructible boulders");
        CheckFraming(stage.Id,GetWindow().Size);
        await Capture("rock-garden-four-player-opening");
        _root.Back();await Frames(3);

        _root.StartLocal(new SessionSettings(Stage:stage.Id,Bots:0,SuddenDeath:false),8);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"garden eight-player adaptation starts",150);
        Check(State.GetProperty("players").EnumerateArray().Select(p=>(p.GetProperty("x").GetSingle(),p.GetProperty("z").GetSingle())).Distinct().Count()==8,"eight seats have distinct supported starts");
        await Capture("rock-garden-eight-player-opening");_root.Back();await Frames(3);

        _root.StartLocal(new SessionSettings(Stage:stage.Id,Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"garden exploration reaches practice fight",150);
        await GrappleWalk(0,5,8);await GrappleWalk(0,8,8);await GrappleWalk(0,8,10);await GrappleWalk(0,13,10);
        Check(Player.GetProperty("y").GetSingle()==0,"central paving remains a flat continuous floor");
        await Capture("rock-garden-central-paving");
        await GrappleWalk(0,17,10);Input(0,new PlayerInput(1,0));await Frames(55);Input(0,default);
        Check(Player.GetProperty("x").GetSingle()<18&&Player.GetProperty("alive").GetBoolean()&&!Player.GetProperty("ghost").GetBoolean(),"visible boundary stops actual movement instead of dropping player into void");
        var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
        if(!_headless)
        {
            camera.Position=new(14,9,19);camera.LookAt(new Vector3(9.5f,.3f,9.5f));
            await Capture("rock-garden-stone-and-turf-detail");camera.Transform=pose;
        }
        _root.Back();await Frames(3);

        // A normal battle: ordinary placement, retreat, real fuse, destruction,
        // visible loot, then collection. Guaranteed drops are explicit QA settings.
        _root.StartLocal(new SessionSettings(Stage:stage.Id,Bots:0,SuddenDeath:false,Ghosts:false)
            {Advanced=new AdvancedRules{DropPercent=100,DisabledItems="remote,red,bounce,fast,heart,focus,tiny,big,haste,slow"}},2);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"garden loot battle starts",150);
        byte[] Bits()=>Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!);
        bool Covered((int X,int Z) cell){var bits=Bits();int i=cell.Z*stage.Width+cell.X;return (bits[i/8]&(1<<(i%8)))!=0;}
        var start=(X:(int)Player.GetProperty("x").GetSingle(),Z:(int)Player.GetProperty("z").GetSingle());
        var reachable=LootReachable(stage,start,Covered,6);
        var opening=reachable.OrderBy(c=>c.Value).SelectMany(c=>new[]{(1,0),(-1,0),(0,1),(0,-1)}.Select(d=>(From:c.Key,Cover:(X:c.Key.X+d.Item1,Z:c.Key.Z+d.Item2))))
            .First(c=>stage.At(c.Cover.X,c.Cover.Z).IsPassable&&Covered(c.Cover));
        Task Route((int X,int Z) target)=>GrappleWalk(0,target.X,target.Z);
        await Route(opening.From);
        Input(0,new PlayerInput(opening.Cover.X-opening.From.X,opening.Cover.Z-opening.From.Z));await Frames(1);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
        Check(State.GetProperty("bombs").GetArrayLength()==1,"ordinary garden primary tap places one bomb");
        var retreat=LootReachable(stage,opening.From,Covered,5).Where(c=>(c.Key.X-opening.From.X)*(c.Key.X-opening.From.X)+(c.Key.Z-opening.From.Z)*(c.Key.Z-opening.From.Z)>=9).OrderBy(c=>c.Value).First().Key;
        await Route(retreat);
        await WaitState(s=>s.GetProperty("bombs").GetArrayLength()==0&&s.GetProperty("hazards").GetArrayLength()==0,"garden opening completes fuse and explosion",180);
        Check(!Covered(opening.Cover)&&Player.GetProperty("alive").GetBoolean(),"real garden explosion destroys boulder and retreat survives");
        var reward=State.GetProperty("items").EnumerateArray().First(i=>i.GetProperty("cx").GetInt32()==opening.Cover.X&&i.GetProperty("cz").GetInt32()==opening.Cover.Z);
        await Capture("rock-garden-boulder-destroyed-reward");
        long serial=Player.GetProperty("pickupSerial").GetInt64();await Route(opening.Cover);
        Check(Player.GetProperty("pickupSerial").GetInt64()>serial,"ordinary garden walking collects the revealed upgrade");
        await Capture("rock-garden-upgrade-collected");_root.Back();await Frames(3);

        _root.StartLocal(new SessionSettings(Stage:stage.Id,Bots:0,RoundSeconds:60,SuddenDeath:true,Ghosts:false),3);
        await WaitState(s=>s.GetProperty("meteors").GetArrayLength()>0,"garden sudden death publishes falling fireball warning",180);
        Check(State.GetProperty("hurryMode").GetString()=="meteors"&&State.GetProperty("closedLeft").GetInt32()==0,"garden fireballs replace column closure");
        var target=State.GetProperty("meteors")[0];
        await Capture("rock-garden-fireball-warning");
        Input(0,new PlayerInput(1,0));await Frames(25);Input(0,default);
        await Capture("rock-garden-falling-fireball");
        await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"garden fireball reaches real impact",60);
        Check(Player.GetProperty("alive").GetBoolean(),"player escapes fixed fireball target through ordinary movement");
        Check(_root.FindChildren("*","",true,false).OfType<BlastCircuit.Arena.Effects.MeteorView>().Any(),"falling fireball has a visible production model");
        await Capture("rock-garden-fireball-impact");_root.Back();await Frames(3);

        _root.StartLocal(new SessionSettings(Stage:stage.Id,Bots:7,SuddenDeath:false),1);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"eight-player garden CPU battle starts",150);
        bool bomb=false,blast=false;
        for(int i=0;i<300&&_root.ScreenName=="Match";i++)
        {
            bomb|=State.GetProperty("bombs").GetArrayLength()>0;blast|=State.GetProperty("hazards").GetArrayLength()>0;await Frames(1);
        }
        Check(bomb&&blast,"garden CPU battle navigates and produces real bombs and blasts");
        await Capture("rock-garden-eight-player-combat");_root.Back();await Frames(3);
    }
}

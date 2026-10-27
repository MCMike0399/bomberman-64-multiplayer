using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Rendering;
using Godot;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task RegionalCoverTour()
    {
        var previousPreset=_root.Preferences.Graphics;
        if(!_headless)await SelectGraphics(GraphicsPreset.High);
        if((_scenario is "cover-layout" or "cover-regression")&&!_headless)await ResizeWindow(new Vector2I(1600,1000));
        foreach(var (id,budget) in new[]{(BlastStages.SunkenTemple,90),(BlastStages.RockGarden,64),
            (BlastStages.UpAndDown,36),(BlastStages.TopRules,48),(BlastStages.BlizzardBattle,64),(BlastStages.GreedyTrap,64)})
        {
            Input(0,default);Input(1,default);
            _root.StartLocal(new SessionSettings(Stage:id,Bots:0,SuddenDeath:false,Ghosts:false){Advanced=new AdvancedRules{DropPercent=100,DisabledItems="remote,red,bounce,heart,focus,tiny,big,haste,slow"}},2);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"regional cover battle starts: "+id,150);
            var stage=BlastStages.Get(id);byte[] bits=Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!);
            bool Covered((int X,int Z) c){int index=c.Z*stage.Width+c.X;return (bits[index/8]&(1<<(index%8)))!=0;}
            string initialCover=Convert.ToBase64String(bits);
            int count=bits.Sum(b=>System.Numerics.BitOperations.PopCount((uint)b));
            Check(count==budget,"normal battle supplies its authored regional cover budget: "+id);
            Check(State.GetProperty("items").GetArrayLength()==0,"normal battle rewards must be revealed through real destruction: "+id);
            Event("regional-cover-layout",new {stage=id,seed=64,blocks=count,preset=_root.Preferences.Graphics.ToString(),lighting=_root.Preferences.StageLighting.ToString(),camera="ordinary battle camera",humans=2,bots=0,practice=false});
            await Capture("regional-cover-"+id);
            if(id==BlastStages.SunkenTemple)
            {
                await GrappleWalk(0,12,13);await GrappleWalk(0,8,13);await GrappleWalk(0,8,20);
                Check(Player.GetProperty("y").GetSingle()==0,"protected stair and court lanes lead to the lower reward area");
                await Capture("regional-court-clear-route");
                var reachable=LootReachable(stage,((int)Player.GetProperty("x").GetSingle(),(int)Player.GetProperty("z").GetSingle()),Covered,6);
                var opening=reachable.SelectMany(from=>new[]{(1,0),(-1,0),(0,1),(0,-1)}.Select(d=>(From:from.Key,Cover:(X:from.Key.X+d.Item1,Z:from.Key.Z+d.Item2),Distance:from.Value)))
                    .Where(c=>c.Cover.Z>=21&&stage.At(c.Cover.X,c.Cover.Z).Kind==CellKind.Floor&&stage.At(c.Cover.X,c.Cover.Z).Tier==0&&Covered(c.Cover)&&stage.CanCross(c.From.X,c.From.Z,c.Cover.X,c.Cover.Z))
                    .OrderBy(c=>c.Distance).First();
                var retreat=LootReachable(stage,opening.From,Covered,6).Where(c=>stage.At(c.Key.X,c.Key.Z).Tier==0&&
                    (c.Key.X-opening.From.X)*(c.Key.X-opening.From.X)+(c.Key.Z-opening.From.Z)*(c.Key.Z-opening.From.Z)>=9).OrderBy(c=>c.Value).First().Key;
                await GrappleWalk(0,opening.From.X,opening.From.Z);
                Input(0,new PlayerInput(opening.Cover.X-opening.From.X,opening.Cover.Z-opening.From.Z));await Frames(1);
                Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
                Check(State.GetProperty("bombs").GetArrayLength()==1,"ordinary bomb is planted beside a lower-court cluster");
                await GrappleWalk(0,retreat.X,retreat.Z);
                await WaitState(s=>s.GetProperty("bombs").GetArrayLength()==0&&s.GetProperty("hazards").GetArrayLength()==0,"lower-court opening completes its real fuse and blast",150);
                Check(Player.GetProperty("alive").GetBoolean(),"court has a usable retreat route from the farming bomb");
                bits=Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!);
                Check(!Covered(opening.Cover),"real explosion opens the lower-court cover cluster");
                var reward=State.GetProperty("items").EnumerateArray().First(i=>i.GetProperty("type").GetString() is "fire" or "bomb");
                await Capture("regional-court-revealed-reward");
                int fire=Player.GetProperty("fire").GetInt32(),bombs=Player.GetProperty("bombs").GetInt32();
                await GrappleWalk(0,reward.GetProperty("cx").GetInt32(),reward.GetProperty("cz").GetInt32());
                Check(Player.GetProperty("fire").GetInt32()>fire||Player.GetProperty("bombs").GetInt32()>bombs,"visiting the court yields an actual stacking upgrade");
                await Capture("regional-court-collected-upgrade");
            }
            _root.Back();await Frames(3);
            _root.StartLocal(new SessionSettings(Stage:id,Bots:0,SuddenDeath:false,Ghosts:false){Advanced=new AdvancedRules{DropPercent=100,DisabledItems="remote,red,bounce,heart,focus,tiny,big,haste,slow"}},2,seed:9876);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"alternate seeded cover starts: "+id,150);
            Check(Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!).Sum(b=>System.Numerics.BitOperations.PopCount((uint)b))==budget,"alternate seed retains the full cover budget: "+id);
            Check(State.GetProperty("softBlocks").GetString()!=initialCover,"a different seed generates a different full-density layout: "+id);
            Event("regional-cover-layout",new {stage=id,seed=9876,blocks=budget,preset=_root.Preferences.Graphics.ToString(),lighting=_root.Preferences.StageLighting.ToString(),camera="ordinary battle camera",humans=2,bots=0,practice=false});
            await Capture("regional-cover-"+id+"-seed-9876");
            _root.Back();await Frames(3);
        }
        var pinnedSeed=_root.LocalMatchSeedOverride;
        try
        {
            _root.LocalMatchSeedOverride=null;
            string? previous=null;
            for(int attempt=0;attempt<3;attempt++)
            {
                _root.StartLocal(new SessionSettings(Stage:BlastStages.BlizzardBattle,Bots:0),2);
                await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"ordinary unpinned local start reaches fight",150);
                string opening=State.GetProperty("softBlocks").GetString()!;
                if(previous!=null)Check(opening!=previous,"ordinary local starts choose fresh layouts without a supplied seed");
                previous=opening;_root.Back();await Frames(3);
            }
        }
        finally {_root.LocalMatchSeedOverride=pinnedSeed;}
        if(!_headless)await SelectGraphics(previousPreset);
    }
}

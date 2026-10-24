using System.Reflection;
using System.Text.Json;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Ai;
using Burbuja.Realtime.Abstractions;

// Simulation-only gameplay measurements. Unlike the old benchmark's point samples,
// these windows observe every eligible tick, including commands with zero movement.
static class GameplaySoakChecks
{
    const int TickRate=30;
    const int IdleLimitTicks=12*TickRate;
    sealed class Motion
    {
        public float X,Z,PreviousX,PreviousZ;
        public int Idle,OpenIdle,Blocked;
        public bool Eligible;
        public long Pickup;
        public bool Alive;
        public bool Falling;
    }
    sealed class Result
    {
        public string Stage {get;init;}="";
        public string Difficulty {get;init;}="";
        public int Bots {get;init;}
        public int Seed {get;init;}
        public int Ticks {get;set;}
        public int ActiveTicks {get;set;}
        public int EligibleBotTicks {get;set;}
        public int MovingBotTicks {get;set;}
        public int SafeWaitBotTicks {get;set;}
        public int NoSafeExitBotTicks {get;set;}
        public int BlockedIntentBotTicks {get;set;}
        public int LongestIdleTicks {get;set;}
        public int LongestOpenIdleTicks {get;set;}
        public int LongestBlockedIntentTicks {get;set;}
        public string? LongestOpenIdleBot {get;set;}
        public int LongestOpenIdleAtTick {get;set;}
        public string LongestOpenIdleState {get;set;}="";
        public int Bombs {get;set;}
        public int OpponentPressureBombs {get;set;}
        public int CoverPressureBombs {get;set;}
        public int CoverDestroyed {get;set;}
        public int Pickups {get;set;}
        public int TerrainDrops {get;set;}
        public int Deaths {get;set;}
        public int RoundsCompleted {get;set;}
        public int[] Wins {get;set;}=[];
        public string EndPhase {get;set;}="";
    }
    public static void Run(string[] args)
    {
        if(args.Contains("--terrain-regression"))
        {
            var result=Measure(BlastStages.Get("sunken-temple"),"hard",7,64,1800);
            Console.WriteLine(JsonSerializer.Serialize(result));
            if(result.LongestOpenIdleTicks>IdleLimitTicks)throw new Exception("Terrain support change stalled a stock temple CPU with a safe exit.");
            Console.WriteLine("PASS stock temple terrain support retains bot liveness");return;
        }
        if(args.Contains("--idle-regression"))
        {
            var result=Measure(BlastStages.Get("rock-garden"),"easy",7,64,1800);
            Console.WriteLine(JsonSerializer.Serialize(result));
            if(result.LongestOpenIdleTicks>IdleLimitTicks)throw new Exception("Low-magnitude steering stalled with a safe exit.");
            Console.WriteLine("PASS status-item stock seed retains bot liveness");return;
        }
        int ticks=IntArgument(args,"--ticks=",2700);
        int fairnessChecks=CheckFairness();
        var results=new List<Result>();
        foreach(string stage in new[]{"rock-garden","up-and-down","sunken-temple","top-rules"})
        foreach(string difficulty in new[]{"easy","medium","hard"})
        foreach(int bots in new[]{3,7})
        foreach(int seed in new[]{64,9876})
            results.Add(Measure(BlastStages.Get(stage),difficulty,bots,seed,ticks));
        string json=JsonSerializer.Serialize(new {
            tickRate=TickRate,idleLimitTicks=IdleLimitTicks,fairnessChecks,
            eligibility="Fight/Hurry, alive, non-ghost, not held, stunned, airborne, falling, or in kick recovery; anchor progress >= .12 cell",
            openIdle="stationary consecutive eligible ticks with at least one visible unoccupied, uncovered, bomb-free adjacent terrain edge safe for .9 seconds; includes zero movement commands",
            pressure="new bomb geometrically reaches a currently visible living opponent or soft cover; describes opportunity, not damage",
            results
        },new JsonSerializerOptions{WriteIndented=true});
        string? output=args.FirstOrDefault(a=>a.StartsWith("--output="))?[9..];
        if(output!=null){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,json+"\n");}
        Console.WriteLine(json);
        var failures=results.Where(r=>r.LongestOpenIdleTicks>IdleLimitTicks).ToArray();
        if(failures.Length>0)throw new Exception("Sustained eligible stationary bot with a safe visible exit: "+string.Join("; ",failures.Select(r=>$"{r.Stage}/{r.Difficulty}/{r.Bots}/{r.Seed} {r.LongestOpenIdleBot} {r.LongestOpenIdleTicks} ticks at {r.LongestOpenIdleAtTick}")));
        if(results.Any(r=>r.EligibleBotTicks==0||r.MovingBotTicks==0||r.Bombs==0))throw new Exception("A matrix scenario never produced active bot movement and bomb activity");
        Console.WriteLine($"PASS {results.Count} gameplay soak scenarios; longest open stationary window {results.Max(r=>r.LongestOpenIdleTicks)/(double)TickRate:F2}s <= {IdleLimitTicks/TickRate}s");
    }
    static int CheckFairness()
    {
        int checks=0;
        foreach(string stage in new[]{"rock-garden","up-and-down","sunken-temple","top-rules"})
        {
            string? reference=null;
            foreach(string difficulty in new[]{"easy","medium","hard"})
            {
                var f=new Fixture(BlastStages.Get(stage),7,9876,difficulty);f.Step();
                string initial=Field<SoftBlockGrid>(f.Session,"_softBlocks").ToBase64()+JsonSerializer.Serialize(f.Session.AiTestPlayers.Select(p=>new{p.X,p.Y,p.Z,p.BombCapacity,p.FireLevel,p.HasHeart,p.HasRed,p.HasRemote,p.InvincibleTimer}));
                if(reference!=null&&reference!=initial)throw new Exception("Difficulty changes starting arena or physical player resources: "+stage);
                reference=initial;
            }
            checks++;
        }
        var medium=new Fixture(Fixture.Flat(),3,64,"medium");var normal=new Fixture(Fixture.Flat(),3,64,"normal");
        for(int tick=0;tick<600;tick++)
        {
            medium.Step();normal.Step();
            if(!medium.Session.AiTestPlayers.Select(p=>(p.X,p.Y,p.Z,p.Alive,p.Input)).SequenceEqual(normal.Session.AiTestPlayers.Select(p=>(p.X,p.Y,p.Z,p.Alive,p.Input))))throw new Exception("Medium and legacy Normal aliases diverge");
        }
        checks++;
        return checks;
    }
    static int IntArgument(string[] args,string key,int fallback)=>int.TryParse(args.FirstOrDefault(a=>a.StartsWith(key))?[key.Length..],out int n)?n:fallback;
    static T Field<T>(BlastSession session,string name)=>(T)typeof(BlastSession).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(session)!;
    static bool Eligible(BlastPlayer p)=>p.Alive&&!p.Ghost&&p.CarriedById==null&&p.StunTimer<=0&&!p.Airborne&&!p.Falling&&p.KickWindupRemaining<=0&&p.KickRecoveryRemaining<=0;
    static Result Measure(StageLayout stage,string difficulty,int bots,int seed,int ticks)
    {
        var fixture=new Fixture(stage,bots,seed,difficulty);
        var session=fixture.Session;
        var soft=Field<SoftBlockGrid>(session,"_softBlocks");
        var hazards=Field<List<BlastHazard>>(session,"_hazards");
        var graph=NavigationGraph.For(stage);
        var danger=new TimedDangerField(graph,stage);
        var knownBombs=new HashSet<string>();
        var motion=new Dictionary<string,Motion>();
        var result=new Result{Stage=stage.Id,Difficulty=difficulty,Bots=bots,Seed=seed};
        bool previousActive=false;
        int previousCover=CountCover(soft,stage);
        for(int tick=0;tick<ticks&&fixture.State!=LobbyState.Ended;tick++)
        {
            bool active=session.AiTestPhase is RoundPhase.Fight or RoundPhase.Hurry;
            fixture.Step();result.Ticks++;
            bool nowActive=session.AiTestPhase is RoundPhase.Fight or RoundPhase.Hurry;
            foreach(var player in session.AiTestPlayers)
            {
                if(!motion.TryGetValue(player.Id,out var observed))motion[player.Id]=observed=new Motion{X=player.X,Z=player.Z,PreviousX=player.X,PreviousZ=player.Z,Pickup=player.PickupSerial,Alive=player.Alive};
                if(active&&observed.Alive&&!player.Alive)result.Deaths++;
                if(active&&player.Falling&&!observed.Falling)result.TerrainDrops++;
                observed.Alive=player.Alive;observed.Falling=player.Falling;
            }
            if(active&&!nowActive)result.RoundsCompleted++;
            if(!active||!nowActive)
            {
                foreach(var m in motion.Values){m.Eligible=false;m.Idle=m.OpenIdle=m.Blocked=0;}
                previousActive=false;continue;
            }
            result.ActiveTicks++;
            // Round restart replaces the grid; never observe stale cover from the previous round.
            if(!previousActive)soft=Field<SoftBlockGrid>(session,"_softBlocks");
            danger.Build(soft,session.AiTestBombs,hazards);
            int cover=CountCover(soft,stage);
            if(previousActive)result.CoverDestroyed+=Math.Max(0,previousCover-cover);
            previousCover=cover;previousActive=true;
            foreach(var bomb in session.AiTestBombs)if(knownBombs.Add(bomb.Id))
            {
                result.Bombs++;
                if(session.AiTestPlayers.Any(p=>p.Id!=bomb.OwnerId&&p.Alive&&!p.Ghost&&BlastGeometry.Contains(stage,soft,bomb.X,bomb.Tier,bomb.Z,bomb.BlastRadius,p.X,p.Y,p.Z,bomb.IsRed)))result.OpponentPressureBombs++;
                bool reachesCover=false;
                for(int z=0;z<stage.Height&&!reachesCover;z++)for(int x=0;x<stage.Width;x++)
                    if(soft.IsAlive(x,z)&&BlastGeometry.Contains(stage,soft,bomb.X,bomb.Tier,bomb.Z,bomb.BlastRadius,x+.5f,stage.At(x,z).Tier,z+.5f,bomb.IsRed)){reachesCover=true;break;}
                if(reachesCover)result.CoverPressureBombs++;
            }
            foreach(var p in session.AiTestPlayers)
            {
                var m=motion[p.Id];
                if(p.PickupSerial>m.Pickup)result.Pickups+=(int)(p.PickupSerial-m.Pickup);
                m.Pickup=p.PickupSerial;
                bool moved=(p.X-m.PreviousX)*(p.X-m.PreviousX)+(p.Z-m.PreviousZ)*(p.Z-m.PreviousZ)>=.005f*.005f;
                m.PreviousX=p.X;m.PreviousZ=p.Z;
                if(!Eligible(p)){m.Eligible=false;m.Idle=m.OpenIdle=m.Blocked=0;continue;}
                result.EligibleBotTicks++;
                if(!m.Eligible){m.X=p.X;m.Z=p.Z;m.Eligible=true;moved=false;}
                if(moved)result.MovingBotTicks++;
                bool progress=(p.X-m.X)*(p.X-m.X)+(p.Z-m.Z)*(p.Z-m.Z)>=.12f*.12f;
                bool open=HasSafeExit(p,session,stage,graph,soft,danger);
                if(progress){m.X=p.X;m.Z=p.Z;m.Idle=m.OpenIdle=m.Blocked=0;}
                else
                {
                    m.Idle++;m.OpenIdle=open?m.OpenIdle+1:0;
                    m.Blocked=p.Input.Length>.1f&&!moved?m.Blocked+1:0;
                    if(p.Input.Length>.1f&&!moved)result.BlockedIntentBotTicks++;
                    else if(open&&!moved&&p.Input.Length<=.1f)result.SafeWaitBotTicks++;
                    if(!open&&!moved)result.NoSafeExitBotTicks++;
                }
                result.LongestIdleTicks=Math.Max(result.LongestIdleTicks,m.Idle);
                result.LongestBlockedIntentTicks=Math.Max(result.LongestBlockedIntentTicks,m.Blocked);
                if(m.OpenIdle>result.LongestOpenIdleTicks){result.LongestOpenIdleTicks=m.OpenIdle;result.LongestOpenIdleBot=p.Id;result.LongestOpenIdleAtTick=tick;result.LongestOpenIdleState=$"position=({p.X:F3},{p.Z:F3}) escapeNode={p.BotEscapeNode} input=({p.Input.X:F3},{p.Input.Z:F3}) alive={session.AiTestPlayers.Count(other=>other.Alive&&!other.Ghost)} neighbors={DescribeNeighborhood(p,stage,soft)} bodies={string.Join(";",session.AiTestPlayers.Where(other=>other.Id!=p.Id&&other.Alive&&!other.Ghost&&Math.Abs(other.X-p.X)+Math.Abs(other.Z-p.Z)<3).Select(other=>$"{other.Id}@{other.X:F2},{other.Z:F2}"))}";}
            }
        }
        result.Wins=session.AiTestPlayers.OrderBy(p=>p.Slot).Select(p=>p.Wins).ToArray();result.EndPhase=session.AiTestPhase.ToString();
        Console.Error.WriteLine($"soak {stage.Id} {difficulty} bots={bots} seed={seed}: openIdle={result.LongestOpenIdleTicks/30d:F2}s idle={result.LongestIdleTicks/30d:F2}s bombs={result.Bombs} pressure={result.OpponentPressureBombs} pickups={result.Pickups} deaths={result.Deaths}");
        return result;
    }
    static string DescribeNeighborhood(BlastPlayer p,StageLayout stage,SoftBlockGrid soft)
    {
        var descriptions=new List<string>();
        for(int z=(int)p.Z-1;z<=(int)p.Z+1;z++)for(int x=(int)p.X-1;x<=(int)p.X+1;x++)
            descriptions.Add($"{x},{z}:{(stage.At(x,z).IsPassable?"floor":"wall")}/h{stage.At(x,z).Tier}/soft{soft.IsAlive(x,z)}");
        return string.Join(";",descriptions);
    }
    static int CountCover(SoftBlockGrid soft,StageLayout stage){int count=0;for(int z=0;z<stage.Height;z++)for(int x=0;x<stage.Width;x++)if(soft.IsAlive(x,z))count++;return count;}
    static bool HasSafeExit(BlastPlayer p,BlastSession session,StageLayout stage,NavigationGraph graph,SoftBlockGrid soft,TimedDangerField danger)
    {
        int cell=graph.Cell(p.X,p.Z,p.Y);if(cell<0)return false;
        for(int d=0;d<4;d++)
        {
            int next=graph.Edges[cell*4+d];if(next<0)continue;
            int x=graph.X(next),z=graph.Z(next);
            if(soft.IsAlive(x,z)||!danger.Safe(next,0,.9f)||session.AiTestBombs.Any(b=>!b.Exploded&&b.CellX==x&&b.CellZ==z))continue;
            if(session.AiTestPlayers.Any(other=>other.Id!=p.Id&&other.Alive&&!other.Ghost&&other.CarriedById==null&&Math.Abs(other.Y-graph.Height[next])<.8f&&(other.X-x-.5f)*(other.X-x-.5f)+(other.Z-z-.5f)*(other.Z-z-.5f)<.85f*.85f))continue;
            return true;
        }
        return false;
    }
}

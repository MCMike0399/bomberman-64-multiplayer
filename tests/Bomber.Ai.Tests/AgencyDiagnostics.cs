using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Ai;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

// Measures what a player notices about a CPU: shuffling one cell back and forth,
// standing still through a Blizzard gust, trailing a walking target, or real pursuit.
static class AgencyDiagnostics
{
    internal sealed record Result(string Stage,string Difficulty,int Seed,string Scenario,int ActiveTicks,
        int CellChanges,int PingPongs,int LongestPingPongStreak,int DistinctCells,int Bombs,int HumanPressureBombs,
        int FirstPressureTick,int LongestPressureGapTicks,int LongestStillTicks,
        int GustTicks,int GustMovingTicks,int CalmTicks,int CalmMovingTicks,int GustBombs,int BotDeaths,int LongestPingPongEndTick);
    internal sealed record SweepResult(string Difficulty,bool Moving,double MeanHits,int MinHits,
        float MedianGapSeconds,float P90GapSeconds,float MaxGapSeconds,int Deaths);

    static T Field<T>(BlastSession session,string name)=>(T)typeof(BlastSession).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(session)!;
    static Vector2 Wind(BlastSession session)=>Field<StageLayout>(session,"_stage").Id==BlastStages.BlizzardBattle?
        BlizzardRules.Wind(Field<float>(session,"_weatherTime"),Field<int>(session,"_weatherSeed"),Field<int>(session,"_roundNum")):Vector2.Zero;
    static float Dist2(float ax,float az,float bx,float bz)=>(ax-bx)*(ax-bx)+(az-bz)*(az-bz);

    /// <summary>One bot against one idle, renewed-invulnerable player on a stock arena:
    /// the reported "neutral" situation where nothing forces the CPU to react.</summary>
    internal static Result IdleHuman(string stageId,string level,int seed,int ticks=1800,bool trace=false,int? startingBombs=null)
    {
        var arena=BlastStages.Get(stageId);var f=new Fixture(arena,2,seed,level,startingBombs:startingBombs);
        f.Session.AiTestAddHuman("cpu-0",0);
        var spawn=arena.Spawns[0];f.Session.AiTestPosition("cpu-0",spawn.X+.5f,spawn.Z+.5f);f.Step(61);
        var human=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
        var bot=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");
        return Observe(f,arena,level,seed,"idle-human",ticks,bot,human,trace);
    }

    /// <summary>Ordinary all-CPU match observed from the first bot slot.</summary>
    internal static Result AllBots(string stageId,string level,int seed,int bots,int ticks=1800,bool trace=false,int? startingBombs=null)
    {
        var arena=BlastStages.Get(stageId);var f=new Fixture(arena,bots,seed,level,startingBombs:startingBombs);f.Step(61);
        var bot=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
        return Observe(f,arena,level,seed,$"{bots}-bots",ticks,bot,null,trace);
    }

    static Result Observe(Fixture f,StageLayout arena,string level,int seed,string scenario,int ticks,BlastPlayer bot,BlastPlayer? human,bool trace)
    {
        var soft=Field<SoftBlockGrid>(f.Session,"_softBlocks");var graph=NavigationGraph.For(arena);
        var known=new HashSet<string>(f.Session.AiTestBombs.Select(b=>b.Id));var cells=new HashSet<int>();
        int active=0,changes=0,pingPongs=0,streak=0,longestStreak=0,bombs=0,pressure=0,first=-1,last=0,gap=0,still=0,longestStill=0;
        int gust=0,gustMoving=0,calm=0,calmMoving=0,gustBombs=0,deaths=0,streakEnd=0;
        int previousCell=graph.Cell(bot.X,bot.Z,bot.Y),beforeCell=-1;float px=bot.X,pz=bot.Z;
        for(int tick=0;tick<ticks;tick++)
        {
            if(human!=null){human.InvincibleTimer=2;human.Input=default;}
            if(f.Session.AiTestPhase is not (RoundPhase.Fight or RoundPhase.Hurry))break;
            bool eligible=bot.Alive&&!bot.Ghost&&bot.CarriedById==null&&bot.StunTimer<=0&&!bot.Airborne&&!bot.Falling;
            var wind=Wind(f.Session);
            f.Step();active++;
            if(!bot.Alive||bot.Ghost){deaths++;break;}
            float moved=MathF.Sqrt(Dist2(bot.X,bot.Z,px,pz));px=bot.X;pz=bot.Z;
            // Commanded progress: a gust alone moves an idle body too.
            bool moving=bot.Input.Length>.05f&&moved>.02f;
            if(eligible)
            {
                if(wind!=Vector2.Zero){gust++;if(moving)gustMoving++;}else{calm++;if(moving)calmMoving++;}
                if(!moving){still++;longestStill=Math.Max(longestStill,still);}else still=0;
            }
            // A ping-pong returns to the cell visited two moves ago; a streak of them
            // without any bomb is the visible one-step-forward, one-step-back shuffle.
            int cell=graph.Cell(bot.X,bot.Z,bot.Y);
            if(cell>=0&&cell!=previousCell)
            {
                changes++;cells.Add(cell);
                if(cell==beforeCell){pingPongs++;streak++;if(streak>longestStreak){longestStreak=streak;streakEnd=tick;}}else streak=0;
                beforeCell=previousCell;previousCell=cell;
            }
            foreach(var bomb in f.Session.AiTestBombs)if(bomb.OwnerId==bot.Id&&known.Add(bomb.Id))
            {
                bombs++;streak=0;
                if(wind!=Vector2.Zero)gustBombs++;
                if(human!=null&&BlastGeometry.Contains(arena,soft,bomb.X,bomb.Tier,bomb.Z,bomb.BlastRadius,human.X,human.Y,human.Z,bomb.IsRed))
                {pressure++;if(first<0)first=tick;gap=Math.Max(gap,tick-last);last=tick;}
            }
            if(trace&&(tick%6==0||Environment.GetEnvironmentVariable("TRACE_ALL")=="1"))Console.WriteLine($"T{tick,5} cell=({graph.X(cell)},{graph.Z(cell)}) pos=({bot.X:F2},{bot.Z:F2}) in=({bot.Input.X:F2},{bot.Input.Z:F2}) a={bot.Input.A} "+
                $"wind=({wind.X:F2},{wind.Y:F2}) bombs={bot.ActiveBombs}/{bot.BombCapacity} fire={bot.FireLevel} stun={bot.StunTimer:F1} others="+
                string.Join(" ",f.Session.AiTestPlayers.Where(p=>p!=bot).Select(p=>$"({p.X:F1},{p.Z:F1})")));
        }
        if(human!=null)gap=Math.Max(gap,active-last);
        return new(arena.Id,level,seed,scenario,active,changes,pingPongs,longestStreak,cells.Count,bombs,pressure,first,gap,longestStill,
            gust,gustMoving,calm,calmMoving,gustBombs,deaths,streakEnd);
    }

    /// <summary>Bombs placed by every CPU over the first <paramref name="ticks"/> of an
    /// all-CPU match. A mirrored standoff produces almost none.</summary>
    internal static int AllBotBombs(string stageId,string level,int seed,int bots,int ticks)
    {
        var f=new Fixture(BlastStages.Get(stageId),bots,seed,level);f.Step(61);
        var known=new HashSet<string>();
        for(int tick=0;tick<ticks&&f.Session.AiTestPhase is RoundPhase.Fight or RoundPhase.Hurry;tick++)
        {f.Step();foreach(var bomb in f.Session.AiTestBombs)known.Add(bomb.Id);}
        return known.Count;
    }
    /// <summary>The fixed open-arena pressure fixture is one deterministic sample: small
    /// timing changes move its single longest gap by seconds. Variant zero is that
    /// fixture; the others vary the target's start cell and patrol phase.</summary>
    internal static SweepResult PressureSweep(string level,bool moving,int? only=null,int? startingBombs=null)
    {
        var gaps=new List<int>();var hits=new List<int>();int deaths=0;
        for(int variant=0;variant<24;variant++)
        {
            if(only is not null&&only!=variant)continue;
            int hx=variant==0?9:7+variant%4,hz=variant==0?6:3+variant/4%6,phase=variant*7%160;
            var arena=Fixture.Flat(spawns:[(3,6),(hx,hz)]);var f=new Fixture(arena,2,64,level,startingBombs:startingBombs);
            f.Session.AiTestAddHuman("cpu-1",1);f.Session.AiTestPosition("cpu-1",hx+.5f,hz+.5f);f.Step(61);
            var human=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");var bot=f.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
            var soft=new SoftBlockGrid(arena.Width,arena.Height);var known=new HashSet<string>();
            int pressure=0,last=0,gap=0;bool alive=true;
            for(int tick=0;tick<900;tick++)
            {
                // Same ordinary 35% square patrol and renewed invulnerability as PressureChecks.
                human.InvincibleTimer=2;int leg=(tick+phase)/40%4;
                human.Input=moving?new(leg==0?.35f:leg==2?-.35f:0,leg==1?.35f:leg==3?-.35f:0,false,false,false):default;
                f.Step();
                if(alive&&!bot.Alive){deaths++;alive=false;}
                foreach(var bomb in f.Session.AiTestBombs)if(bomb.OwnerId==bot.Id&&known.Add(bomb.Id)&&
                    BlastGeometry.Contains(arena,soft,bomb.X,bomb.Tier,bomb.Z,bomb.BlastRadius,human.X,human.Y,human.Z,bomb.IsRed))
                {pressure++;gap=Math.Max(gap,tick-last);last=tick;}
                if(only is not null&&tick%10==0)Console.WriteLine($"t={tick} bot=({bot.X:F2},{bot.Z:F2}) stun={bot.StunTimer:F1} human=({human.X:F2},{human.Z:F2}) hits={pressure}");
            }
            gaps.Add(Math.Max(gap,900-last));hits.Add(pressure);
        }
        gaps.Sort();
        return new(level,moving,hits.Average(),hits.Min(),gaps[gaps.Count/2]/30f,gaps[(int)(gaps.Count*.9)]/30f,gaps[^1]/30f,deaths);
    }

    public static void Run(string[] args)
    {
        string? output=args.FirstOrDefault(a=>a.StartsWith("--output="))?[9..];
        void Write(object value)
        {
            string json=JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true});
            if(output!=null){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,json+"\n");}
        }
        string? variant=args.FirstOrDefault(a=>a.StartsWith("--variant="))?[10..];
        if(args.Contains("--standoff"))
        {
            foreach(string stage in new[]{"top-rules","up-and-down","greedy-trap"})foreach(string level in new[]{"easy","medium","hard"})foreach(int seed in new[]{64,9876})
                Console.WriteLine($"{stage} {level} {seed}: {AllBotBombs(stage,level,seed,4,600)} bombs in 20s");
            return;
        }
        if(args.Contains("--sweep"))
        {
            if(variant!=null){var parts=variant.Split(',');PressureSweep(parts[0],true,int.Parse(parts[1]));return;}
            var sweep=new List<SweepResult>();
            foreach(string level in new[]{"easy","medium","hard"})foreach(bool moving in new[]{false,true})sweep.Add(PressureSweep(level,moving));
            foreach(var r in sweep)Console.WriteLine(JsonSerializer.Serialize(r));
            Write(sweep);return;
        }
        // --trace=stage,level,seed[,ticks[,bots]] prints one bot's decisions over time.
        string? traceArg=args.FirstOrDefault(a=>a.StartsWith("--trace="))?[8..];
        if(traceArg!=null)
        {
            var parts=traceArg.Split(',');int traceTicks=parts.Length>3?int.Parse(parts[3]):900;
            var r=parts.Length>4?AllBots(parts[0],parts[1],int.Parse(parts[2]),int.Parse(parts[4]),traceTicks,true):IdleHuman(parts[0],parts[1],int.Parse(parts[2]),traceTicks,true);
            Console.WriteLine(JsonSerializer.Serialize(r));return;
        }
        var results=Matrix();
        Write(results);
        Console.WriteLine($"{"stage",-16}{"level",-7}{"seed",-6}{"scenario",-11}{"flips",6}{"run",5}{"cells",6}{"bombs",6}{"hits",5}{"first",6}{"gap",6}{"still",6}{"gust%",7}{"calm%",7}{"dead",5}");
        foreach(var r in results)
            Console.WriteLine($"{r.Stage,-16}{r.Difficulty,-7}{r.Seed,-6}{r.Scenario,-11}{r.PingPongs,6}{r.LongestPingPongStreak,5}{r.DistinctCells,6}{r.Bombs,6}{r.HumanPressureBombs,5}"+
                $"{r.FirstPressureTick/30f,6:F1}{r.LongestPressureGapTicks/30f,6:F1}{r.LongestStillTicks/30f,6:F1}"+
                $"{(r.GustTicks>0?100f*r.GustMovingTicks/r.GustTicks:-1),7:F0}{(r.CalmTicks>0?100f*r.CalmMovingTicks/r.CalmTicks:-1),7:F0}{r.BotDeaths,5}");
    }

    /// <summary>Every stock battle arena, level and both soak seeds: one bot facing an idle
    /// player, and the first of four CPUs in an ordinary match. 60 seconds each.</summary>
    internal static List<Result> Matrix(int? startingBombs=null)
    {
        var results=new List<Result>();
        foreach(string stage in new[]{"sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap"})
        foreach(string level in new[]{"easy","medium","hard"})
        foreach(int seed in new[]{64,9876})
        {
            results.Add(IdleHuman(stage,level,seed,startingBombs:startingBombs));
            results.Add(AllBots(stage,level,seed,4,startingBombs:startingBombs));
        }
        return results;
    }
}

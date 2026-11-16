using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Ai;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

// Current-loadout new-stage engagement. The human walks from the real spawn;
// no post-start positions, cover, bot resources or AI decisions are replaced.
static class StageEngagementChecks
{
    internal sealed record Result(string Stage,string Difficulty,int Seed,int StartingBombs,
        bool ReachedCourt,int WalkTicks,float StartHeight,float CourtHeight,bool FellFromCap,int GlassWalkTicks,
        float ReleaseCoastDistance,int ActiveTicks,int PressureBombs,int FirstPressureTick,int LongestPressureGapTicks,
        int BotDeaths,int BotCells,bool BotVisitedCourt,bool BotTraversedGlass);
    static T Field<T>(BlastSession session,string name)=>(T)typeof(BlastSession).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(session)!;

    public static int Run(string[] args)
    {
        var results=new List<Result>();
        foreach(string stage in new[]{BlastStages.FieldOfGrass,BlastStages.InTheGutter,BlastStages.SeaSick,BlastStages.LostAtSea})
        foreach(string level in new[]{"easy","medium","hard"})foreach(int seed in new[]{64,9876})
            results.Add(Measure(stage,level,seed));
        string json=JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true});
        string? output=args.FirstOrDefault(a=>a.StartsWith("--output="))?[9..];
        if(output!=null){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,json+"\n");}
        if(args.Contains("--stage-engagement"))Console.WriteLine(json);
        if(!args.Contains("--measure-only"))foreach(var r in results)
        {
            if(!r.ReachedCourt||!r.BotVisitedCourt||r.PressureBombs<1||r.FirstPressureTick<0||
                r.FirstPressureTick>=20*30||r.LongestPressureGapTicks>=20*30)
                throw new Exception($"New-stage court engagement failed: {JsonSerializer.Serialize(r)}");
            if(r.Stage==BlastStages.InTheGutter&&(!r.FellFromCap||r.StartHeight!=2||r.CourtHeight>=.15f))
                throw new Exception("Gutter engagement did not include an ordinary drop from its isolated spawn cap.");
            if(r.Stage==BlastStages.LostAtSea&&(r.GlassWalkTicks<3||r.ReleaseCoastDistance<.12f||!r.BotTraversedGlass))
                throw new Exception("Lost at Sea engagement bypassed ordinary glass traversal and release inertia.");
        }
        // Every mortal-bot row stays in the result and must engage. Repetition is
        // evaluated over both declared seeds: a bot killed after its first attack
        // cannot honestly be described as observed for the whole minute.
        if(!args.Contains("--measure-only"))foreach(var pair in results.GroupBy(r=>(r.Stage,r.Difficulty)))
            if(pair.Count()!=2||pair.Sum(r=>r.PressureBombs)<4)
                throw new Exception($"New-stage paired seeds did not produce repeated pressure: {pair.Key}");
        Console.WriteLine((args.Contains("--measure-only")?"MEASURE":"PASS")+" 24 new-stage ordinary-traversal engagement scenarios");
        return results.Count;
    }

    private static Result Measure(string stageId,string level,int seed)
    {
        var stage=BlastStages.Get(stageId);var fixture=new Fixture(stage,2,seed,level);
        fixture.Session.AiTestAddHuman("cpu-0",0);var spawn=stage.Spawns[0];
        fixture.Session.AiTestPosition("cpu-0",spawn.X+.5f,spawn.Z+.5f);fixture.Step(61);
        var human=fixture.Session.AiTestPlayers.Single(p=>p.Id=="cpu-0");
        var bot=fixture.Session.AiTestPlayers.Single(p=>p.Id=="cpu-1");
        int capacity=bot.BombCapacity;float startHeight=human.Y;
        var soft=Field<SoftBlockGrid>(fixture.Session,"_softBlocks");var graph=NavigationGraph.For(stage);
        var target=stageId switch
        {
            BlastStages.FieldOfGrass=>new Vector2(5.5f,3.5f),
            BlastStages.LostAtSea=>new Vector2(3.5f,9.5f),
            _=>new Vector2(9.5f,9.5f)
        };
        bool fell=false,botCourt=false,botGlass=false;int glass=0,walk=0;
        var botCells=new HashSet<int>();
        void Sample(bool walking=false)
        {
            fell|=human.Falling&&human.Y<startHeight;
            if(walking&&human.Input.Length>.05f&&SlipperyGlassRules.OnGlass(stage,human.X,human.Z,human.Y))glass++;
            botCourt|=bot.Y<.15f&&!bot.Falling&&!bot.Airborne;
            botGlass|=SlipperyGlassRules.OnGlass(stage,bot.X,bot.Z,bot.Y);
            botCells.Add(graph.Cell(bot.X,bot.Z,bot.Y));
        }
        for(;walk<600&&human.Alive&&bot.Alive;walk++)
        {
            human.InvincibleTimer=2;
            if(Vector2.Distance(new(human.X,human.Z),target)<.28f&&!human.Falling&&!human.Airborne)break;
            var waypoint=NextWaypoint(stage,graph,soft,human,target);
            var delta=waypoint-new Vector2(human.X,human.Z);float distance=delta.Length();
            float speed=Math.Min(4.5f,MathF.Sqrt(2*SlipperyGlassRules.Acceleration*distance)*.7f);
            var desired=distance>.01f?delta/distance*speed:Vector2.Zero;
            if(SlipperyGlassRules.OnGlass(stage,human.X,human.Z,human.Y))
                desired+=(desired-new Vector2(human.SlideVX,human.SlideVZ))*1.5f;
            if(stageId==BlastStages.InTheGutter&&human.Y<.15f)
                desired.X-=GutterRules.Current(Field<float>(fixture.Session,"_weatherTime"),Field<int>(fixture.Session,"_weatherSeed"),Field<int>(fixture.Session,"_roundNum"))*GutterRules.PlayerScale;
            if(desired.Length()>4.5f)desired=Vector2.Normalize(desired)*4.5f;
            human.Input=new(desired.X/4.5f,desired.Y/4.5f,false,false,false);fixture.Step();Sample(walking:true);
        }
        bool reached=Vector2.Distance(new(human.X,human.Z),target)<.4f&&!human.Falling&&!human.Airborne;
        float courtHeight=human.Y;
        var released=new Vector2(human.X,human.Z);
        for(int tick=0;tick<6&&human.Alive&&bot.Alive;tick++)
        {human.InvincibleTimer=2;human.Input=default;fixture.Step();Sample();}
        float coast=Vector2.Distance(released,new(human.X,human.Z));

        var known=new HashSet<string>(fixture.Session.AiTestBombs.Select(b=>b.Id));
        int active=0,pressure=0,first=-1,last=0,gap=0,deaths=0;
        for(int tick=0;tick<1800&&fixture.Session.AiTestPhase is RoundPhase.Fight or RoundPhase.Hurry;tick++)
        {
            human.InvincibleTimer=2;human.Input=default;fixture.Step();active++;Sample();
            foreach(var bomb in fixture.Session.AiTestBombs)
                if(bomb.OwnerId==bot.Id&&known.Add(bomb.Id)&&
                    BlastGeometry.Contains(stage,soft,bomb.X,bomb.LandingY??stage.SurfaceAt(bomb.X,bomb.Z,bomb.Tier),bomb.Z,
                        bomb.BlastRadius,human.X,human.Y,human.Z,bomb.IsRed))
                {pressure++;if(first<0)first=tick;gap=Math.Max(gap,tick-last);last=tick;}
            if(!bot.Alive||bot.Ghost){deaths++;break;}
        }
        gap=Math.Max(gap,active-last);
        return new(stageId,level,seed,capacity,reached,walk,startHeight,courtHeight,fell,glass,coast,
            active,pressure,first,gap,deaths,botCells.Count,botCourt,botGlass);
    }

    // The scripted human may plan a route, but only its ordinary analog intent
    // enters authority. The same directed terrain edges and real seeded cover
    // constrain both actors; no test-only teleport or invented climb is used.
    private static Vector2 NextWaypoint(StageLayout stage,NavigationGraph graph,SoftBlockGrid soft,BlastPlayer human,Vector2 target)
    {
        int start=graph.Cell(human.X,human.Z,human.Y),goal=graph.Cell(target.X,target.Y,0);
        if(start==goal)return target;
        var queue=new Queue<int>();var previous=new Dictionary<int,int>{{start,start}};queue.Enqueue(start);
        while(queue.TryDequeue(out int cell)&&!previous.ContainsKey(goal))
            for(int direction=0;direction<4;direction++)
            {
                int next=graph.Edges[cell*4+direction];
                if(next<0||previous.ContainsKey(next)||soft.IsAlive(graph.X(next),graph.Z(next)))continue;
                previous[next]=cell;queue.Enqueue(next);
            }
        if(!previous.ContainsKey(goal))return new(human.X,human.Z);
        int step=goal;while(previous[step]!=start)step=previous[step];
        return new(graph.X(step)+.5f,graph.Z(step)+.5f);
    }
}

using System.Reflection;
using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;
using Burbuja.Realtime.Abstractions;

internal static class LootAvailabilityChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static int Run()=>new[]{BlastStages.SunkenTemple,BlastStages.RockGarden,BlastStages.UpAndDown}.Sum(RunFor);
    private static int RunFor(string id)
    {
        var stage=BlastStages.Get(id);
        int covers=0,drops=0;var kinds=new Dictionary<string,int>();
        for(int seed=0;seed<32;seed++)
        {
            var a=Match(seed,id);var b=Match(seed,id);
            var zones=Zones(a);var replay=Zones(b);
            Assert(zones.Count>=Math.Min(16,stage.MaxSoftBlocks)&&zones.Count<=stage.MaxSoftBlocks,"Arena cover count lost its authored range.");
            Assert(zones.SequenceEqual(replay),"Seeded arena cover layout diverged.");
            covers+=zones.Count;
            for(int zone=0;zone<stage.Spawns.Length&&stage.OpeningCoverPerSpawn>0;zone++)
            {
                var assigned=zones.Where(kv=>kv.Value==zone).Select(kv=>kv.Key).ToArray();
                Assert(assigned.Length>=stage.OpeningCoverPerSpawn,"A spawn area has fewer than its authored opening opportunities to open cover.");
                Assert(assigned.Count(cell=>TerrainDistance(stage,stage.Spawns[zone],cell)<=6)>=stage.OpeningCoverPerSpawn,"A spawn's opening crates require a distant terrace detour.");
                var approachMatch=Match(seed,id);
                var approach=OpeningApproach(stage,Grid(approachMatch),stage.Spawns[zone],assigned.ToHashSet());
                Assert(approach is not null,"A spawn cannot approach its opening cover along clear floor.");
                var context=(ISessionContext)typeof(LocalMatch).GetField("_context",Private)!.GetValue(approachMatch)!;
                var from=approach!.Value.From;var cover=approach.Value.Cover;
                typeof(BlastSession).GetMethod("DestroyBlocksInBlast",Private)!.Invoke(Session(approachMatch),
                    [context,from.X+.5f,from.Z+.5f,1.3f,false,stage.SurfaceAt(from.X+.5f,from.Z+.5f)]);
                Assert(!Grid(approachMatch).IsAlive(cover.X,cover.Z),"Ordinary starting blast from reachable floor did not open nearby cover.");
                approachMatch.Tick();approachMatch.Tick();

            }
            // Run the real destruction/visibility/drop path, isolating one crate per tiny blast.
            // This bypasses only bomb setup, so no player skill or bot behavior skews the sample.
            foreach(var cell in zones.Keys) {Destroy(a,cell,stage);Destroy(b,cell,stage);}
            a.Tick();a.Tick();b.Tick();b.Tick();
            var items=Items(a);Assert(items.GetRawText()==Items(b).GetRawText(),"Seeded actual cover drops diverged.");
            Assert(zones.Keys.All(cell=>!Grid(a).IsAlive(cell.X,cell.Z)),"Actual blast path left cover alive.");
            foreach(var item in items.EnumerateArray())
            {string kind=item.GetProperty("type").GetString()!;kinds[kind]=kinds.GetValueOrDefault(kind)+1;drops++;}
        }
        int openingDrops=0;
        double ordinaryDropRate=(double)(drops-openingDrops)/(covers-openingDrops);
        Assert(ordinaryDropRate is >.50 and <.60,$"Ordinary cover drop rate {ordinaryDropRate:P1} should stay near 55% to limit item clutter.");
        Assert(kinds.Count==AdvancedRules.ItemTypes.Length&&kinds["bomb"]>kinds["remote"]&&kinds["fire"]>kinds["heart"],"Arena drops lost their diverse, useful weighting.");
        Console.WriteLine($"PASS {stage.Name} loot: {covers/32d:F2} covers, {drops/32d:F2} actual drops per round across 32 seeds; {ordinaryDropRate:P1} ordinary drop rate; opening cover follows the stage policy");
        foreach(bool reverse in new[]{false,true})
        {
            var match=Match(832,id);var zones=Zones(match);
            var opening=zones.GroupBy(kv=>kv.Value).Select(group=>group.First()).ToArray();
            foreach(var entry in reverse?opening.Reverse():opening)Destroy(match,entry.Key,stage);
            match.Tick();match.Tick();var items=Items(match);
            Assert(items.GetArrayLength()<8,"Opening crates still bypass the uniform chance.");

            string before=items.GetRawText();foreach(var entry in opening)Destroy(match,entry.Key,stage);
            match.Tick();match.Tick();Assert(items.GetArrayLength()==Items(match).GetArrayLength(),"Destroying missing cover duplicated a reward.");
        }
        Console.WriteLine($"PASS {stage.Name} uniform opening rolls obey reversed destruction order without duplicate drops");
        return 2;
    }
    static LocalMatch Match(int seed,string id)=>new(new MatchOptions{Stage=id,HumanPlayers=8,BotPlayers=0,Seed=seed});
    static BlastSession Session(LocalMatch match)=>(BlastSession)typeof(LocalMatch).GetField("_session",Private)!.GetValue(match)!;
    static SoftBlockGrid Grid(LocalMatch match)=>(SoftBlockGrid)typeof(BlastSession).GetField("_softBlocks",Private)!.GetValue(Session(match))!;
    static Dictionary<(int X,int Z),int> Zones(LocalMatch match)=>(Dictionary<(int X,int Z),int>)typeof(BlastSession).GetField("_coverLootZones",Private)!.GetValue(Session(match))!;
    static void Destroy(LocalMatch match,(int X,int Z) cell,StageLayout stage)
    {
        var context=(ISessionContext)typeof(LocalMatch).GetField("_context",Private)!.GetValue(match)!;
        typeof(BlastSession).GetMethod("DestroyBlocksInBlast",Private)!.Invoke(Session(match),[context,cell.X+.5f,cell.Z+.5f,.1f,false,stage.SurfaceAt(cell.X+.5f,cell.Z+.5f)]);
    }
    static JsonElement Items(LocalMatch match){using var doc=JsonDocument.Parse(match.StateJson!);return doc.RootElement.GetProperty("d").GetProperty("items").Clone();}
    static ((int X,int Z) From,(int X,int Z) Cover)? OpeningApproach(StageLayout stage,SoftBlockGrid grid,(int X,int Z) start,HashSet<(int X,int Z)> assigned)
    {
        var queue=new Queue<((int X,int Z) Cell,int Distance)>();queue.Enqueue((start,0));var seen=new HashSet<(int,int)>{start};
        while(queue.TryDequeue(out var next))
        {
            foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
            {
                var cell=(X:next.Cell.X+dx,Z:next.Cell.Z+dz);
                if(!stage.CanCross(next.Cell.X,next.Cell.Z,cell.X,cell.Z))continue;
                if(assigned.Contains(cell)&&Math.Abs(stage.SurfaceAt(cell.X+.5f,cell.Z+.5f)-stage.SurfaceAt(next.Cell.X+.5f,next.Cell.Z+.5f))<.01f)
                    return (next.Cell,cell);
                if(next.Distance<5&&!grid.IsAlive(cell.X,cell.Z)&&seen.Add(cell))queue.Enqueue((cell,next.Distance+1));
            }
        }
        return null;
    }
    static int TerrainDistance(StageLayout stage,(int X,int Z) start,(int X,int Z) target)
    {
        var queue=new Queue<((int X,int Z) Cell,int Distance)>();queue.Enqueue((start,0));var seen=new HashSet<(int,int)>{start};
        while(queue.TryDequeue(out var next))
        {
            if(next.Cell==target)return next.Distance;
            foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
            {
                var cell=(X:next.Cell.X+dx,Z:next.Cell.Z+dz);
                if(stage.CanCross(next.Cell.X,next.Cell.Z,cell.X,cell.Z)&&seen.Add(cell))queue.Enqueue((cell,next.Distance+1));
            }
        }
        return int.MaxValue;
    }
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
}

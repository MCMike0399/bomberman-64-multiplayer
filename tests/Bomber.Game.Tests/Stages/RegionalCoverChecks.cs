using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Ai;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;

internal static class RegionalCoverChecks
{
    public static int Run()
    {
        int checks=0;
        foreach(string id in new[]{BlastStages.SunkenTemple,BlastStages.RockGarden,BlastStages.UpAndDown,BlastStages.TopRules,BlastStages.BlizzardBattle,BlastStages.GreedyTrap})
        {
            var fingerprints=new HashSet<string>();var regions=new Dictionary<string,HashSet<string>>();int minimum=int.MaxValue,maximum=0;
            for(int seed=0;seed<32;seed++)
            {
                var stage=BlastStages.Get(id);var options=new MatchOptions{Stage=id,HumanPlayers=8,BotPlayers=0,Seed=seed};
                var a=new LocalMatch(options);var b=new LocalMatch(options);
                string Encoded(LocalMatch match){using var d=JsonDocument.Parse(match.StateJson!);return d.RootElement.GetProperty("d").GetProperty("softBlocks").GetString()!;}
                string encoded=Encoded(a);Assert(encoded==Encoded(b),"Cover is not deterministic for "+id);fingerprints.Add(encoded);
                byte[] bits=Convert.FromBase64String(encoded);
                bool Covered(int x,int z)=>x>=0&&z>=0&&x<stage.Width&&z<stage.Height&&(bits[(z*stage.Width+x)/8]&(1<<((z*stage.Width+x)%8)))!=0;
                var cover=new List<(int X,int Z)>();
                for(int x=0;x<stage.Width;x++)for(int z=0;z<stage.Height;z++)if(Covered(x,z))cover.Add((x,z));
                minimum=Math.Min(minimum,cover.Count);maximum=Math.Max(maximum,cover.Count);
                foreach(var spawn in stage.Spawns)foreach(var (dx,dz) in new[]{(0,0),(1,0),(-1,0),(0,1),(0,-1)})Assert(!Covered(spawn.X+dx,spawn.Z+dz),"Spawn clearance blocked on "+id);
                foreach(var cell in cover){Assert(stage.At(cell.X,cell.Z).Kind==CellKind.Floor,"Cover occupies non-floor.");Assert(!stage.ClearCells.Contains(cell),"Reserved route blocked.");foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})Assert(stage.At(cell.X+dx,cell.Z+dz).Kind!=CellKind.Ramp,"Ramp approach blocked.");}
                if(id==BlastStages.SunkenTemple)
                {
                    var court=cover.Where(c=>stage.At(c.X,c.Z).Tier==0).ToArray();
                    Assert(court.Length>=cover.Count*.5,"Pyramid still puts most cover above the lower courtyard.");
                    Assert(court.Count(c=>c.X<12)>=18&&court.Count(c=>c.X>12)>=18,"Courtyard rewards miss a side pocket.");
                    Assert(court.Count(c=>c.Z>=21)>=20,"Front of the courtyard still lacks reward clusters.");
                    Assert(cover.All(c=>stage.At(c.X,c.Z).Tier<4),"Upper summit is cluttered with farming blocks.");
                    foreach(int x in new[]{8,12,16})for(int z=15;z<=25;z++)Assert(!Covered(x,z),"Court's approach lane is blocked.");
                    for(int x=5;x<=19;x++)Assert(!Covered(x,20),"Court crossing lane is blocked.");
                    Assert(cover.Count is >=80 and <=90,"Pyramid lost its cover budget.");
                }
                else if(id==BlastStages.RockGarden)
                {
                    Assert(cover.Count==64,"Garden must have four generous boulder groves.");
                    Assert(cover.Count(c=>Math.Abs(c.X-9)+Math.Abs(c.Z-9)<=3)>=3,"Garden lost its central boulder cluster.");
                    foreach(int lane in new[]{2,8,10,16})for(int n=1;n<18;n++)
                        Assert(!Covered(n,lane)&&!Covered(lane,n),"Garden perimeter or center routes are blocked.");
                    foreach(int z in new[]{5,13})for(int x=5;x<=13;x++)
                        Assert(!Covered(x,z),"Garden inner spawn avenue is blocked.");
                }
                else if(id==BlastStages.UpAndDown)
                {
                    Assert(cover.Count==36,"Up & Down lost its expanded pier and low-deck caches.");
                    Assert(cover.Count(c=>stage.At(c.X,c.Z).Tier==0)==12,"Low reward deck must receive twelve cover blocks.");
                    Assert(cover.Count(c=>c.X<10&&stage.At(c.X,c.Z).Tier==2)==12&&cover.Count(c=>c.X>13&&stage.At(c.X,c.Z).Tier==2)==12,"Middle piers must each receive twelve blocks.");
                    Assert(cover.All(c=>stage.At(c.X,c.Z).Below is null&&stage.At(c.X,c.Z).Tier<4),"Cover obstructs a bridge or upper route.");

                }
                else
                {
                    int budget=id==BlastStages.TopRules?48:64;
                    Assert(cover.Count==budget,$"Expanded cover budget missing on {id}: expected {budget}, got {cover.Count}.");
                }
                if(id is BlastStages.UpAndDown or BlastStages.BlizzardBattle or BlastStages.GreedyTrap)
                    foreach(var area in stage.CoverAreas)
                    {
                        if(!regions.TryGetValue(area.Name,out var layouts))regions[area.Name]=layouts=[];
                        layouts.Add(string.Join(';',cover.Where(c=>area.Contains(c.X,c.Z,stage.At(c.X,c.Z)))));
                    }
                CheckProtectedRoutes(stage,Covered);
                foreach(int percent in new[]{0,25,50,75})
                {
                    var scaled=new LocalMatch(options with {Advanced=new AdvancedRules{BlockPercent=percent}});
                    int actual=Convert.FromBase64String(Encoded(scaled)).Sum(b=>System.Numerics.BitOperations.PopCount((uint)b));
                    Assert(actual==(int)Math.Round(cover.Count*percent/100d),$"Block setting {percent}% did not scale {id}'s full budget.");
                    checks++;
                }
                var practice=new LocalMatch(options with {Practice=true});
                Assert(Convert.FromBase64String(Encoded(practice)).All(b=>b==0),"Battle cover leaked into practice on "+id);
                checks+=2;
            }
            foreach(var region in regions)Assert(region.Value.Count>=16,$"Region {id}/{region.Key} still stamps a fixed or near-fixed layout.");
            Assert(fingerprints.Count>=24,
                "Every battle map must vary its full-density layout across seeds.");
            Console.WriteLine($"PASS {id}: 32 seeded layouts, {fingerprints.Count} patterns, {minimum}–{maximum} blocks, clear spawns and routes");
        }
        var random=new Random(287);var counts=new Dictionary<string,int>();
        for(int i=0;i<10000;i++){string type=PowerUpRules.ChooseDrop(random);counts[type]=counts.GetValueOrDefault(type)+1;}
        Assert(counts["fire"] is >2700 and <3300&&counts["bomb"] is >2700 and <3300,"Fire and Bomb are not the dominant individual drops.");
        Assert((counts["fire"]+counts["bomb"])/10000d is >.58 and <.62&&counts.Count==AdvancedRules.ItemTypes.Length,"Basic upgrades are not a clear majority while preserving all equipment.");
        Console.WriteLine("PASS Fire and Bomb make up about 60% of the varied drop pool");return checks+1;
    }
    // These are product travel contracts, independent of the authored candidate sets.
    // In particular, a bridge's ground route must remain usable at its own height.
    static void CheckProtectedRoutes(StageLayout stage,Func<int,int,bool> covered)
    {
        string id=stage.Id;
        var targets=new List<(int X,int Z,float Y)>();
        void Clear(int x,int z)=>Assert(!covered(x,z),$"Protected route blocked at {id}/{x},{z}.");
        if(id==BlastStages.UpAndDown)
        {
            foreach(var c in stage.Underpasses)Clear(c.X,c.Z);
            for(int x=0;x<stage.Width;x++)for(int z=0;z<stage.Height;z++)
                if(stage.At(x,z).Tier>=4)Clear(x,z);
            foreach(int z in Enumerable.Range(7,17))foreach(int x in new[]{5,18})Clear(x,z);
            for(int z=12;z<=23;z++)Clear(11,z);
            foreach(int z in new[]{7,14,21})for(int x=3;x<=20;x++)Clear(x,z);
            targets.AddRange([(5,18,2),(18,18,2),(11,18,0),(11,23,0),(11,7,4)]);
        }
        else if(id==BlastStages.TopRules)
        {
            for(int x=1;x<stage.Width-1;x++)for(int z=1;z<stage.Height-1;z++)
                if(stage.At(x,z).Tier>0||x is 2 or 4 or 10 or 16||z is 6 or 8 or 10)Clear(x,z);
            targets.AddRange([(4,4,0),(10,4,0),(4,12,0),(10,12,0),(9,8,2)]);
        }
        else if(id==BlastStages.BlizzardBattle)
        {
            foreach(int lane in new[]{1,9,17})for(int n=1;n<=17;n++)
                {Clear(lane,n);Clear(n,lane);}
            targets.Add((9,9,0));
        }
        else if(id==BlastStages.GreedyTrap)
        {
            for(int n=1;n<=17;n++){Clear(2,n);Clear(16,n);Clear(n,2);Clear(n,16);}
            for(int x=1;x<=17;x++)for(int z=1;z<=17;z++)
                if(stage.At(x,z).Tier==1||x==9||z==9)Clear(x,z);
            targets.AddRange([(9,9,0),(9,5,1),(9,13,1)]);
        }
        var graph=NavigationGraph.For(stage);
        foreach(var spawn in stage.Spawns)
        {
            int start=graph.Cell(spawn.X+.5f,spawn.Z+.5f);
            var found=new HashSet<int>{start};var queue=new Queue<int>();queue.Enqueue(start);
            while(queue.TryDequeue(out int cell))for(int d=0;d<4;d++)
            {
                int next=graph.Edges[cell*4+d];
                if(next<0||covered(graph.X(next),graph.Z(next))||!found.Add(next))continue;
                queue.Enqueue(next);
            }
            Assert(found.Any(n=>Math.Abs(graph.X(n)-spawn.X)+Math.Abs(graph.Z(n)-spawn.Z)>=4),
                $"Cover seals the starting escape area on {id}/{spawn}.");
            foreach(var (x,z,y) in targets)Assert(found.Contains(graph.Cell(x+.5f,z+.5f,y)),
                $"Cover cuts off {id}'s route from {spawn} to {x},{z} at height {y}.");
        }
    }
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
}

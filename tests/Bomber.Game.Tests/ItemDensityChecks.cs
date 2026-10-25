using System.Reflection;
using Bomber.Game;
using Bomber.Game.Simulation;
using Burbuja.Realtime.Abstractions;
using static GameFixtures;

internal static class ItemDensityChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly string[] Stages=[BlastStages.SunkenTemple,BlastStages.RockGarden,BlastStages.UpAndDown,BlastStages.TopRules];
    static bool Upgrade(string type)=>type is "fire" or "bomb";
    static bool Effect(string type)=>PowerUpRules.IsAbility(type)||PowerUpRules.IsStatus(type);

    public static int Run()
    {
        var counts=new Dictionary<string,int>();var random=new Random(1729);
        for(int i=0;i<100000;i++)
        {
            string type=PowerUpRules.ChooseDrop(random);
            counts[type]=counts.GetValueOrDefault(type)+1;
        }
        int upgrades=counts.Where(p=>Upgrade(p.Key)).Sum(p=>p.Value);
        int effects=counts.Where(p=>Effect(p.Key)).Sum(p=>p.Value);
        int modes=counts.Where(p=>PowerUpRules.IsBombType(p.Key)).Sum(p=>p.Value);
        Assert(AdvancedRules.ItemTypes.All(counts.ContainsKey),"The weighted pool lost an enabled item type.");
        Assert(upgrades>effects&&effects>modes,"Drop priority must be stack upgrades, then buffs/debuffs, then bomb types.");
        Assert(upgrades is >58000 and <62000&&effects is >28000 and <32000&&modes is >8000 and <12000,"Loot group shares left the 60/30/10 target.");
        Assert(new AdvancedRules().DropPercent==40&&AdvancedRules.FromSettings(new Dictionary<string,string>()).DropPercent==40,"Local and omitted online defaults must both use 40% drops.");
        foreach(string stage in new[]{BlastStages.SunkenTemple,BlastStages.CircuitGarden})
        {
            var seen=new HashSet<string>();
            for(int seed=0;seed<64;seed++)
            {
                var options=new MatchOptions{Stage=stage,Practice=true,HumanPlayers=1,BotPlayers=0,Seed=seed};
                var a=new LocalMatch(options);var b=new LocalMatch(options);
                var items=Items(a);int count=items.Count;
                Assert(count==6,"Default practice cache must contain exactly six authored pickups.");
                Assert(items.Count(i=>Upgrade(i.Type))>items.Count(i=>Effect(i.Type))&&items.Count(i=>Effect(i.Type))>items.Count(i=>PowerUpRules.IsBombType(i.Type)),"Practice cache lost the category priority.");
                Assert(items.Select(i=>(i.CellX,i.CellZ,i.Type)).SequenceEqual(Items(b).Select(i=>(i.CellX,i.CellZ,i.Type))),"Practice sampling is not deterministic.");
                Assert(items.All(i=>BlastStages.Get(stage).TrainingItems.Contains((i.CellX,i.CellZ,i.Type))),"Practice item escaped an authored location.");
                foreach(var item in items)seen.Add(item.Type);
                Step(a,675);Assert(Items(a).Count==0,"Sparse practice cache must expire without refilling.");
            }
            Assert(AdvancedRules.ItemTypes.All(seen.Contains),"Practice sampling permanently hides an item type.");
            foreach(string type in AdvancedRules.ItemTypes)
            {
                var only=new AdvancedRules{DisabledItems=string.Join(',',AdvancedRules.ItemTypes.Where(t=>t!=type))};
                var match=new LocalMatch(new MatchOptions{Stage=stage,Practice=true,HumanPlayers=1,BotPlayers=0,Advanced=only});
                Assert(Items(match).Count>0&&Items(match).All(i=>i.Type==type),"A restricted practice pool lost its requested item.");
            }
            var empty=new LocalMatch(new MatchOptions{Stage=stage,Practice=true,HumanPlayers=1,BotPlayers=0,Advanced=new(){DisabledItems=string.Join(',',AdvancedRules.ItemTypes)}});
            Assert(Items(empty).Count==0,"Empty practice pool spawned an item.");
            Console.WriteLine($"PASS {stage}: sparse, seeded practice caches preserve priorities, all types, filtering and expiration across 64 seeds");
        }
        Console.WriteLine($"PASS loot priority across 100000 draws: {upgrades} upgrades, {effects} buffs/debuffs, {modes} bomb types");
        return 4;
    }

    // An upper-bound clutter probe: destroy every actual authored block through the
    // authority blast path, without collection, expiry or player/bot skill in the sample.
    public static void Sweep()
    {
        foreach(int percent in new[]{30,35,40,45,55})foreach(string id in Stages)
        {
            var totals=new List<int>();int covers=0,upgrades=0,effects=0,modes=0,earlyUpgrade=0,emptyFour=0;
            for(int seed=0;seed<128;seed++)
            {
                var match=new LocalMatch(new MatchOptions{Stage=id,HumanPlayers=8,BotPlayers=0,Seed=seed,Advanced=new(){DropPercent=percent}});
                var session=Session(match);var stage=BlastStages.Get(id);
                var grid=(SoftBlockGrid)typeof(BlastSession).GetField("_softBlocks",Private)!.GetValue(session)!;
                var cells=new List<(int X,int Z)>();
                for(int z=0;z<stage.Height;z++)for(int x=0;x<stage.Width;x++)if(grid.IsAlive(x,z))cells.Add((x,z));
                new Random(seed).Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cells));
                var context=(ISessionContext)typeof(LocalMatch).GetField("_context",Private)!.GetValue(match)!;
                var destroy=typeof(BlastSession).GetMethod("DestroyBlocksInBlast",Private)!;
                int early=0;
                foreach(var cell in cells)
                {
                    destroy.Invoke(session,[context,cell.X+.5f,cell.Z+.5f,.1f,false,stage.SurfaceAt(cell.X+.5f,cell.Z+.5f)]);
                    early++;
                    if(early==4&&Items(match).Count==0)emptyFour++;
                    if(early==8&&Items(match).Any(i=>Upgrade(i.Type)))earlyUpgrade++;
                }
                covers+=cells.Count;totals.Add(Items(match).Count);
                upgrades+=Items(match).Count(i=>Upgrade(i.Type));effects+=Items(match).Count(i=>Effect(i.Type));modes+=Items(match).Count(i=>PowerUpRules.IsBombType(i.Type));
            }
            totals.Sort();int total=totals.Sum();
            Console.WriteLine($"DENSITY {percent}% {id}: blocks={covers/128d:F1} drops={total/128d:F2} p95={totals[121]} rate={total/(double)covers:P1} upgrades={upgrades/128d:F2} effects={effects/128d:F2} modes={modes/128d:F2} upgrade-in-first8={earlyUpgrade/128d:P1} empty-first4={emptyFour/128d:P1}");
        }
    }
    static BlastSession Session(LocalMatch match)=>(BlastSession)typeof(LocalMatch).GetField("_session",Private)!.GetValue(match)!;
    static List<BlastItem> Items(LocalMatch match)=>(List<BlastItem>)typeof(BlastSession).GetField("_items",Private)!.GetValue(Session(match))!;
}

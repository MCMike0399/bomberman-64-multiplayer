using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;

internal static class RegionalCoverChecks
{
    public static int Run()
    {
        int checks=0;
        foreach(string id in new[]{BlastStages.SunkenTemple,BlastStages.RockGarden,BlastStages.UpAndDown})
        {
            var fingerprints=new HashSet<string>();int minimum=int.MaxValue,maximum=0;
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
                    int west=cover.Count(c=>c.X<=6),east=cover.Count(c=>c.X>=16),center=cover.Count-west-east;
                    Assert(Math.Abs(west-east)<=3&&center>=cover.Count*.4,"Rock Garden clusters are unbalanced or miss the pillar center.");
                    foreach(int z in new[]{6,8})for(int x=0;x<stage.Width;x++)Assert(!Covered(x,z),"Rock Garden crossing lane is blocked.");
                    Assert(cover.Count is >=100 and <=110,"Rock Garden lost its expanded cover budget.");
                }
                else
                {
                    Assert(cover.Count(c=>stage.At(c.X,c.Z).Tier==0)>=cover.Count*.7,"Up & Down is overfilled on narrow upper terrain.");
                    Assert(cover.All(c=>stage.At(c.X,c.Z).Tier<2),"Up & Down summit is cluttered.");
                    foreach(bool east in new[]{false,true})foreach(bool south in new[]{false,true})Assert(cover.Count(c=>stage.At(c.X,c.Z).Tier==0&&(east?c.X>11:c.X<11)&&(south?c.Z>9:c.Z<9))>=12,"An outer platform lacks its reward pocket.");
                    Assert(cover.Count is >=75 and <=96,"Up & Down lost its expanded cover budget.");
                }
                checks++;
            }
            Assert(fingerprints.Count>=24,"Regional budgets produce repetitive layouts instead of seeded variation.");
            Console.WriteLine($"PASS {id}: 32 varied regional layouts, {minimum}–{maximum} blocks, clear spawns and routes");
        }
        var random=new Random(287);var counts=new Dictionary<string,int>();
        for(int i=0;i<10000;i++){string type=PowerUpRules.ChooseDrop(random);counts[type]=counts.GetValueOrDefault(type)+1;}
        Assert(counts["fire"] is >2700 and <3300&&counts["bomb"] is >2700 and <3300,"Fire and Bomb are not the dominant individual drops.");
        Assert((counts["fire"]+counts["bomb"])/10000d is >.58 and <.62&&counts.Count==12,"Basic upgrades are not a clear majority while preserving all equipment.");
        Console.WriteLine("PASS Fire and Bomb make up about 60% of the varied drop pool");return checks+1;
    }
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
}

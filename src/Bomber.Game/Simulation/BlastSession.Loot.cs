using Burbuja.Realtime.Abstractions;
namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private readonly Dictionary<(int X,int Z),int> _coverLootZones = [];

    // Seed nearby cover before filling the rest of the arena. Terrain distance keeps crates on
    // each spawn's reachable terrace, and a shuffle avoids the old west-to-east cap bias.
    private void PlaceRegionalCover(Random random,List<(int X,int Z)> candidates)
    {
        random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(candidates));
        var distances = _stage.Spawns.Select(SpawnDistances).ToArray();
        int Distance(int zone,(int X,int Z) cell) => distances[zone][cell.Z*_stage.Width+cell.X];
        int Nearest((int X,int Z) cell) => Enumerable.Range(0,distances.Length).MinBy(zone=>Distance(zone,cell));
        int target = Math.Min(_stage.MaxSoftBlocks,Math.Max(_stage.Spawns.Length*2,(int)Math.Round(candidates.Count*_stage.SoftBlockDensity)));
        target=(int)Math.Round(target*_advanced.BlockPercent/100d);
        void Place((int X,int Z) cell,int zone)
        {
            candidates.Remove(cell);_softBlocks.Set(cell.X,cell.Z,true);_coverLootZones.Add(cell,zone);
        }
        for(int pass=0;pass<2;pass++)
            for(int zone=0;zone<distances.Length&&_coverLootZones.Count<target;zone++)
            {
                if(candidates.Count==0)break;
                var cell=candidates.MinBy(cell=>Distance(zone,cell));
                if(Distance(zone,cell)!=int.MaxValue)Place(cell,zone);
            }
        // Give each map's authored farming areas a stable share of the budget.
        // Nearby opening cover already placed above counts toward its area's quota.
        var areas=_stage.CoverAreas;
        double totalWeight=areas.Sum(a=>a.Share);
        var exact=areas.Select(a=>target*a.Share/totalWeight).ToArray();
        var quotas=exact.Select(q=>(int)Math.Floor(q)).ToArray();
        foreach(int index in Enumerable.Range(0,areas.Count).OrderByDescending(i=>exact[i]-quotas[i]).Take(target-quotas.Sum()))quotas[index]++;
        for(int index=0;index<areas.Count;index++)
        {
            var area=areas[index];
            bool Includes((int X,int Z) cell)=>area.Contains(cell.X,cell.Z,_stage.At(cell.X,cell.Z));
            int remaining=Math.Max(0,quotas[index]-_coverLootZones.Keys.Count(Includes));
            var selected=candidates.Where(Includes).Select(cell=>(Cell:cell,Key:area.Priority(cell.X,cell.Z,random)))
                .OrderBy(entry=>entry.Key).Take(remaining).Select(entry=>entry.Cell).ToArray();
            foreach(var cell in selected)
            {
                if(_coverLootZones.Count>=target)break;
                Place(cell,Nearest(cell));
            }
        }
        // Small areas may not fit their share after safety exclusions. Redistribute
        // that remainder among eligible cells; never relax a spawn/ramp/path exclusion.
        foreach(var cell in candidates.ToArray())
        {
            if(_coverLootZones.Count>=target)break;
            Place(cell,Nearest(cell));
        }
    }

    private int[] SpawnDistances((int X,int Z) spawn)
    {
        var result=Enumerable.Repeat(int.MaxValue,_stage.Width*_stage.Height).ToArray();
        var queue=new Queue<(int X,int Z)>();queue.Enqueue(spawn);result[spawn.Z*_stage.Width+spawn.X]=0;
        while(queue.TryDequeue(out var cell))
            foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
            {
                var next=(X:cell.X+dx,Z:cell.Z+dz);
                if(!_stage.CanCross(cell.X,cell.Z,next.X,next.Z))continue;
                int index=next.Z*_stage.Width+next.X;
                if(result[index]!=int.MaxValue)continue;
                result[index]=result[cell.Z*_stage.Width+cell.X]+1;queue.Enqueue(next);
            }
        return result;
    }

    private void DropCoverItem(ISessionContext context,int x,int z)
    {
        if(context.Random.NextDouble()<_advanced.DropPercent/100d)
            DropItem(context,x,z,_stage.At(x,z).Tier);
    }
}

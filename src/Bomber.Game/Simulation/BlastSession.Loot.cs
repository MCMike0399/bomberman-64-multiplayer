using Burbuja.Realtime.Abstractions;
namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private readonly Dictionary<(int X,int Z),int> _coverLootZones = [];
    private readonly HashSet<int> _claimedLootZones = [];

    // Seed nearby cover before filling the rest of the arena. Terrain distance keeps crates on
    // each spawn's reachable terrace, and a shuffle avoids the old west-to-east cap bias.
    private void PlaceTempleCover(Random random,List<(int X,int Z)> candidates)
    {
        random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(candidates));
        var distances = _stage.Spawns.Select(SpawnDistances).ToArray();
        int Distance(int zone,(int X,int Z) cell) => distances[zone][cell.Z*_stage.Width+cell.X];
        int Nearest((int X,int Z) cell) => Enumerable.Range(0,distances.Length).MinBy(zone=>Distance(zone,cell));
        int target = Math.Min(_stage.MaxSoftBlocks,Math.Max(_stage.Spawns.Length*2,(int)Math.Round(candidates.Count*_stage.SoftBlockDensity)));
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
        // Every spawn area gets one useful opening reward, independently of destruction order.
        // Later crates retain the varied weighted pool; one reward cannot max out an inventory.
        if(_coverLootZones.TryGetValue((x,z),out int zone)&&_claimedLootZones.Add(zone))
            DropItem(context,x,z,_stage.At(x,z).Tier,zone%2==0?"fire":"bomb");
        else if(context.Random.NextDouble()<PowerUpRules.DropChance)
            DropItem(context,x,z,_stage.At(x,z).Tier);
    }
}

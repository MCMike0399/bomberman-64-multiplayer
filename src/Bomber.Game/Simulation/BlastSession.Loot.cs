// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
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
        int target = Math.Min(_stage.MaxSoftBlocks,Math.Max(_stage.Spawns.Length*_stage.OpeningCoverPerSpawn,(int)Math.Round(candidates.Count*_stage.SoftBlockDensity)));
        target=(int)Math.Round(target*_advanced.BlockPercent/100d);
        void Place((int X,int Z) cell,int zone)
        {
            candidates.Remove(cell);_softBlocks.Set(cell.X,cell.Z,true);_coverLootZones.Add(cell,zone);
        }
        for(int pass=0;pass<_stage.OpeningCoverPerSpawn;pass++)
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

    // ─── items ────────────────────────────────────────────────────────────────

    private IEnumerable<(int X,int Z,string Type)> PracticeItems(Random random)
    {
        var candidates=_stage.TrainingItems.Where(item=>_advanced.Allows(item.Type)).ToArray();
        // Small, explicitly restricted pools remain useful for targeted practice.
        if(candidates.Length<=6)return candidates;
        var result=new List<(int X,int Z,string Type)>();
        foreach(var (types,budget) in new[]{
            (candidates.Where(i=>i.Type is "fire" or "bomb").ToArray(),3),
            (candidates.Where(i=>PowerUpRules.IsAbility(i.Type)||PowerUpRules.IsStatus(i.Type)).ToArray(),2),
            (candidates.Where(i=>PowerUpRules.IsBombType(i.Type)).ToArray(),1)})
        {
            random.Shuffle(types);result.AddRange(types.Take(budget));
        }
        return result;
    }

    private void DropItem(ISessionContext context, int cx, int cz, int tier, string? guaranteedType = null)
    {
        var type = guaranteedType ?? PowerUpRules.ChooseDrop(context.Random,_advanced);
        if(type is null || !_advanced.Allows(type))return;
        _items.Add(new BlastItem { Id = NextItemId(), CellX = cx, CellZ = cz, Tier = tier, Type = type, LifeRemaining=_advanced.ItemSeconds });
    }

    private void TickItems(ISessionContext context)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if(item.LifeRemaining>=0){item.LifeRemaining-=(float)context.TickDelta.TotalSeconds;if(item.LifeRemaining<=0){_items.RemoveAt(i);continue;}}
            item.OwnerLockRemaining=Math.Max(0,item.OwnerLockRemaining-(float)context.TickDelta.TotalSeconds);
            // Check if any player (or ghost) walks over it.
            foreach (var p in _players.Values)
            {
                if (!p.Alive || p.PendingRoundStart) continue;
                if (p.Ghost)
                {
                    // Ghost touching an item destroys it without effect.
                    if (p.Tier == item.Tier && Dist2(p.X, p.Z, item.X, item.Z) < PlayerRadius * PlayerRadius)
                    {
                        _items.RemoveAt(i);
                        goto nextItem;
                    }
                    continue;
                }
                if (!p.Alive || p.Tier != item.Tier || !PowerUpRules.CanCollect(p,item.Type) || item.DroppedBy==p.Id&&item.OwnerLockRemaining>0) continue;
                if (Dist2(p.X, p.Z, item.X, item.Z) < (PlayerRadius + 0.4f) * (PlayerRadius + 0.4f))
                {
                    var replaced=PowerUpRules.Exchange(p,item.Type);
                    _items.RemoveAt(i);
                    if(replaced!=null)DropEquipment(p,replaced);
                    goto nextItem;
                }
            }
            nextItem:;
        }
    }

    private static void ApplyItem(BlastPlayer p,string type) => PowerUpRules.Apply(p,type);
    private void DropEquipment(BlastPlayer player,string type)
    {
        _items.Add(new BlastItem {Id=NextItemId(),CellX=(int)MathF.Floor(player.X),CellZ=(int)MathF.Floor(player.Z),Tier=player.Tier,Type=type,LifeRemaining=_advanced.ItemSeconds,DroppedBy=player.Id,OwnerLockRemaining=PowerUpRules.SwapPickupDelay});
    }

    // ─── soft blocks ─────────────────────────────────────────────────────────

    private void PlaceSoftBlocks(Random rng)
    {
        _softBlocks = new SoftBlockGrid(_stage.Width, _stage.Height);
        _coverLootZones.Clear();
        if (_stage.FixedSoftBlocks is { } fixedBlocks)
        {
            foreach(var cell in fixedBlocks) _softBlocks.Set(cell.Item1,cell.Item2,true);
            return;
        }
        if (_practice) return;
        var spawnCells = new HashSet<(int, int)>(_stage.Spawns.SelectMany(s =>
            new[] { (s.X, s.Z), (s.X + 1, s.Z), (s.X - 1, s.Z), (s.X, s.Z + 1), (s.X, s.Z - 1) }));

        var placed = 0;
        var candidates = new List<(int X,int Z)>();
        for (var x = 0; x < _stage.Width; x++)
            for (var z = 0; z < _stage.Height; z++)
            {
                var cell = _stage.At(x, z);
                if (cell.Kind != CellKind.Floor) continue;
                if (spawnCells.Contains((x, z))) continue;
                if (_stage.ClearCells.Contains((x, z))) continue;
                if (new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => _stage.At(x + d.Item1, z + d.Item2).Kind == CellKind.Ramp)) continue;
                if (_stage.CoverAreas.Count>0) { candidates.Add((x,z)); continue; }
                if (placed < _stage.MaxSoftBlocks && rng.NextDouble() < _stage.SoftBlockDensity)
                { _softBlocks.Set(x, z, true); placed++; }
            }
        if (_stage.CoverAreas.Count>0) PlaceRegionalCover(rng,candidates);
    }
}

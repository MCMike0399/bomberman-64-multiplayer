// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.

namespace Bomber.Game.Simulation.Stages;

/// <summary>An authored farming area: budget share plus optional cluster centers.
/// Shares describe this game's tuning, not reconstructed original-game probabilities.</summary>
public sealed record CoverArea(string Name,double Share,int X0,int X1,int Z0,int Z1,int MinTier=0,int MaxTier=5)
{
    public IReadOnlyList<(int X,int Z)> Centers {get;init;}=[];
    public bool Contains(int x,int z,CellDef cell)=>x>=X0&&x<=X1&&z>=Z0&&z<=Z1&&cell.Tier>=MinTier&&cell.Tier<=MaxTier;
    public double Priority(int x,int z,Random random)
    {
        // Weighted sampling without replacement clusters cells, while retaining
        // variation instead of stamping an identical wall into every round.
        double distance=Centers.Count==0?0:Centers.Min(c=>(x-c.X)*(x-c.X)+(z-c.Z)*(z-c.Z));
        return -Math.Log(Math.Max(random.NextDouble(),1e-12))*(1+distance/4);
    }
}

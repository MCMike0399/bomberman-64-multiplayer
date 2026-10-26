// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Bomber.Game.Simulation.Rules;

namespace Bomber.Game.Simulation.Stages;

/// <summary>The static layout of one stage.</summary>
public sealed class StageLayout
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Width { get; init; }   // columns (X)
    public int Height { get; init; }  // rows (Z)
    public CellDef[,] Cells { get; init; } = new CellDef[0, 0];
    public IEnumerable<(int X,int Z)> Underpasses => Enumerable.Range(0,Width).SelectMany(x=>Enumerable.Range(0,Height).Where(z=>Cells[x,z].Below is not null).Select(z=>(X:x,Z:z)));
    public const float DeckThickness=CellDef.DeckThickness;
    public (int X, int Z)[] Spawns { get; init; } = [];
    // Fixed layouts are a local, assembly-internal fixture seam; no network message can set them.
    public IReadOnlyList<(int X,int Z,string Type)> TrainingItems { get; init; } = [];
    internal IReadOnlyList<(int X,int Z,string Type)> InitialItems { get; init; } = [];
    internal IReadOnlySet<(int X,int Z)>? FixedSoftBlocks { get; init; }
    public IReadOnlyList<CoverArea> CoverAreas {get;init;}=[];
    public double SoftBlockDensity { get; init; } = .45;
    public int MaxSoftBlocks { get; init; } = int.MaxValue;
    /// <summary>Physical top of breakable cover above its support; shared by flight and presentation.</summary>
    public float SoftBlockHeight {get;init;}=BombThrowRules.CoverHeight;
    public int OpeningCoverPerSpawn { get; init; } = 2;
    public bool RisingWater { get; init; }
    public bool DecksBlockBlasts {get;init;}=true;
    public IReadOnlySet<(int X, int Z)> ClearCells { get; init; } = new HashSet<(int, int)>();

    public CellDef At(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return CellDef.Void;
        return Cells[x, z];
    }

    /// <summary>Select support from the entity's elevation, allowing a small step tolerance at deck edges.</summary>
    public CellDef AtHeight(int x, int z, float y)
    {
        var cell = At(x, z);
        return cell.Below is { } below && y < cell.Tier - .25f ? below : cell;
    }

    /// <summary>Continuous ground elevation in gameplay units; rendering scales it uniformly.</summary>
    public float SurfaceAt(float x, float z) => Surface(At((int)MathF.Floor(x), (int)MathF.Floor(z)), x, z);
    public float SurfaceAt(float x, float z, float y) => Surface(AtHeight((int)MathF.Floor(x), (int)MathF.Floor(z), y), x, z);

    /// <summary>A rising body meets the underside of a suspended slab, not its upper floor.</summary>
    public float Ceiling(float x,float z,float radius,float previousY,float nextY,float bodyHeight)
    {
        float result=nextY;
        for(int cx=(int)MathF.Floor(x-radius);cx<=(int)MathF.Floor(x+radius);cx++)
        for(int cz=(int)MathF.Floor(z-radius);cz<=(int)MathF.Floor(z+radius);cz++)
        {
            var cell=At(cx,cz);if(cell.Below is null)continue;
            float dx=x-Math.Clamp(x,cx,cx+1f),dz=z-Math.Clamp(z,cz,cz+1f);
            if(dx*dx+dz*dz>=radius*radius)continue;
            float bottom=cell.Tier-CellDef.DeckThickness;
            if(previousY+bodyHeight<=bottom+.001f && nextY+bodyHeight>bottom)
                result=Math.Min(result,bottom-bodyHeight);
        }
        return result;
    }

    public bool DeckOverlaps(int x,int z,float bottom,float height)
    {
        var cell=At(x,z);
        return cell.Below is not null && bottom<cell.Tier-.001f && bottom+height>cell.Tier-CellDef.DeckThickness;
    }

    private static float Surface(CellDef cell, float x, float z)
    {
        var cx = (int)MathF.Floor(x); var cz = (int)MathF.Floor(z);
        if (cell.Kind != CellKind.Ramp) return cell.Tier;
        var progress = cell.RampDX != 0 ? (cell.RampDX > 0 ? x - cx : 1 - (x - cx))
            : (cell.RampDZ > 0 ? z - cz : 1 - (z - cz));
        return cell.RampFromTier + Math.Clamp(progress, 0, 1) * (cell.RampToTier - cell.RampFromTier);
    }

    /// <summary>A shared edge is traversable only when its entire surface is continuous.
    /// This prevents side-entry into stairs and climbing vertical terrace walls.</summary>
    public bool CanCross(int ax, int az, int bx, int bz, float y = float.PositiveInfinity)
    {
        if (Math.Abs(ax - bx) + Math.Abs(az - bz) != 1) return ax == bx && az == bz;
        var a = AtHeight(ax, az, y); var b = AtHeight(bx, bz, y);
        if (!a.IsPassable || !b.IsPassable) return false;
        for (var i = 1; i <= 3; i++)
        {
            var u = i * .25f;
            var x = ax != bx ? Math.Max(ax, bx) : ax + u;
            var z = az != bz ? Math.Max(az, bz) : az + u;
            var dx = (bx - ax) * .001f; var dz = (bz - az) * .001f;
            if (Math.Abs(Surface(a, x - dx, z - dz) - Surface(b, x + dx, z + dz)) > .01f) return false;
        }
        return true;
    }

    /// <summary>Movement can leave a higher edge; returning upward still needs a ramp.</summary>
    public bool CanWalk(int ax,int az,int bx,int bz,float y = float.PositiveInfinity)
    {
        if(CanCross(ax,az,bx,bz,y))return true;
        var a=AtHeight(ax,az,y);var b=AtHeight(bx,bz,y);
        if(Math.Abs(ax-bx)+Math.Abs(az-bz)!=1||!a.IsPassable||!b.IsPassable)return false;
        for(int i=1;i<=3;i++)
        {
            float u=i*.25f,x=ax!=bx?Math.Max(ax,bx):ax+u,z=az!=bz?Math.Max(az,bz):az+u;
            float dx=(bx-ax)*.001f,dz=(bz-az)*.001f;
            if(Surface(b,x+dx,z+dz)>Surface(a,x-dx,z-dz)+.01f)return false;
        }
        return true;
    }

    public bool IsHardCell(int x, int z) => At(x, z).Kind == CellKind.Pillar;
}

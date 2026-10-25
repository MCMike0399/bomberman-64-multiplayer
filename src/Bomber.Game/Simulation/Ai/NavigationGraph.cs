using System.Runtime.CompilerServices;

namespace Bomber.Game.Simulation.Ai;

/// <summary>Height-aware directed walk edges. A bridge and its underpass are distinct nodes.</summary>
internal sealed class NavigationGraph
{
    private static readonly ConditionalWeakTable<StageLayout, NavigationGraph> Cache = new();
    private readonly StageLayout _stage;
    private readonly int _plane;
    public int PlaneCount=>_plane;
    public int Width { get; }
    public int Count { get; }
    public int[] Edges { get; }
    public bool[] Walkable { get; }
    public float[] Height { get; }
    public static NavigationGraph For(StageLayout stage) => Cache.GetValue(stage, s => new(s));
    private NavigationGraph(StageLayout stage)
    {
        _stage = stage; Width = stage.Width; _plane = stage.Width * stage.Height;
        bool layered = stage.Cells.Cast<CellDef>().Any(c => c.Below is { IsPassable: true });
        Count = _plane * (layered ? 2 : 1);
        Edges = new int[Count * 4]; Array.Fill(Edges, -1);
        Walkable = new bool[Count]; Height = new float[Count];
        for (int node = 0; node < Count; node++)
        {
            int x = X(node), z = Z(node);
            var cell = node < _plane ? stage.At(x,z) : stage.At(x,z).Below;
            Walkable[node] = cell?.IsPassable == true;
            Height[node] = node < _plane ? stage.SurfaceAt(x+.5f,z+.5f) : stage.SurfaceAt(x+.5f,z+.5f,float.NegativeInfinity);
        }
        ReadOnlySpan<int> dx = [0,-1,1,0], dz = [-1,0,0,1];
        for (int node = 0; node < Count; node++)
        {
            if (!Walkable[node]) continue;
            int x = X(node), z = Z(node); float y = Height[node];
            for (int d = 0; d < 4; d++)
                if (stage.CanWalk(x,z,x+dx[d],z+dz[d],y))
                    Edges[node*4+d] = Cell(x+dx[d]+.5f,z+dz[d]+.5f,y);
        }
    }
    public int X(int node) => node % Width;
    public int Z(int node) => node % _plane / Width;
    public int Cell(float x,float z,float y=float.PositiveInfinity)
    {
        int cx=(int)MathF.Floor(x),cz=(int)MathF.Floor(z);
        if(cx<0||cz<0||cx>=Width||cz>=_stage.Height)return -1;
        int node=cz*Width+cx;
        var top=_stage.At(cx,cz);
        if(top.Below is not null && ReferenceEquals(_stage.AtHeight(cx,cz,y),top.Below))
            return top.Below.IsPassable ? node+_plane : -1;
        return node;
    }
}

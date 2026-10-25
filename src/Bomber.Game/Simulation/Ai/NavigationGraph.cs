using System.Runtime.CompilerServices;

namespace Bomber.Game.Simulation.Ai;

/// <summary>Directed surface graph. Bridge decks and the floor beneath have distinct nodes.</summary>
internal sealed class NavigationGraph
{
    private static readonly ConditionalWeakTable<StageLayout, NavigationGraph> Cache = new();
    private readonly StageLayout _stage;
    public int Width { get; }
    public int PlaneCount { get; }
    public int Count { get; }
    public int[] Edges { get; }
    public bool[] Walkable { get; }
    public float[] Height { get; }
    public static NavigationGraph For(StageLayout stage) => Cache.GetValue(stage, s => new(s));
    public int X(int node) => node % Width;
    public int Z(int node) => node % PlaneCount / Width;
    private NavigationGraph(StageLayout stage)
    {
        _stage = stage; Width = stage.Width; PlaneCount = Width * stage.Height;
        Count = PlaneCount * (stage.Underpasses.Count == 0 ? 1 : 2);
        Edges = new int[Count * 4]; Array.Fill(Edges, -1);
        Walkable = new bool[Count]; Height = new float[Count];
        ReadOnlySpan<int> dx = [0, -1, 1, 0], dz = [-1, 0, 0, 1];
        for (int node = 0; node < Count; node++)
        {
            int x = X(node), z = Z(node);
            Walkable[node] = node < PlaneCount ? stage.At(x, z).IsPassable : stage.HasUnderpass(x, z);
            Height[node] = node < PlaneCount ? stage.SurfaceAt(x + .5f, z + .5f) : 0;
            if (!Walkable[node]) continue;
            float ceiling = Height[node] + .25f;
            for (int d = 0; d < 4; d++)
                if (stage.CanWalk(x, z, x + dx[d], z + dz[d], ceiling))
                    Edges[node * 4 + d] = Cell(x + dx[d] + .5f, z + dz[d] + .5f,
                        stage.SurfaceAt(x + dx[d] + .5f, z + dz[d] + .5f, ceiling));
        }
    }
    public int Cell(float x, float z, float y = float.PositiveInfinity)
    {
        int cx = (int)MathF.Floor(x), cz = (int)MathF.Floor(z);
        if (cx < 0 || cx >= Width || cz < 0 || cz >= _stage.Height) return -1;
        return cz * Width + cx + (_stage.HasLowerSupport(x, z, y) ? PlaneCount : 0);
    }
}

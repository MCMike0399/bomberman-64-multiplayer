using System.Runtime.CompilerServices;

namespace Bomber.Game.Simulation.Ai;

/// <summary>Immutable cardinal walk edges use exactly the authority's ramp/cliff continuity rules.</summary>
internal sealed class NavigationGraph
{
    private static readonly ConditionalWeakTable<StageLayout, NavigationGraph> Cache = new();
    public int Width { get; }
    public int Count { get; }
    public int[] Edges { get; }
    public bool[] Walkable { get; }
    public float[] Height { get; }
    public static NavigationGraph For(StageLayout stage) => Cache.GetValue(stage, s => new(s));
    private NavigationGraph(StageLayout stage)
    {
        Width=stage.Width;Count=stage.Width*stage.Height;Edges=new int[Count*4];Array.Fill(Edges,-1);Walkable=new bool[Count];Height=new float[Count];
        ReadOnlySpan<int> dx=[0,-1,1,0];ReadOnlySpan<int> dz=[-1,0,0,1];
        for(int z=0;z<stage.Height;z++)for(int x=0;x<stage.Width;x++)
        {
            int cell=z*Width+x;Walkable[cell]=stage.At(x,z).IsPassable;Height[cell]=stage.SurfaceAt(x+.5f,z+.5f);
            if(!Walkable[cell])continue;
            for(int d=0;d<4;d++)if(stage.CanCross(x,z,x+dx[d],z+dz[d]))Edges[cell*4+d]=(z+dz[d])*Width+x+dx[d];
        }
    }
    public int Cell(float x,float z)
    {
        int cx=(int)MathF.Floor(x),cz=(int)MathF.Floor(z);return cx>=0&&cx<Width&&cz>=0&&cz*Width<Count?cz*Width+cx:-1;
    }
}

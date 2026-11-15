// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation.Stages;

public static partial class BlastStages
{
    public const string InTheGutter="in-the-gutter";

    private static StageLayout BuildInTheGutter()
    {
        const int size=21;
        var cells=Fill(size,size,CellDef.Floor0);
        for(int n=0;n<size;n++)
        {
            cells[0,n]=cells[size-1,n]=new CellDef{Kind=CellKind.Pillar,ObstacleHeight=2.1f};
            cells[n,0]=new CellDef{Kind=CellKind.Pillar,ObstacleHeight=1.05f};
            cells[n,size-1]=new CellDef{Kind=CellKind.Pillar,ObstacleHeight=.35f};
        }
        // Four original-relative elevated starts. Enlarged square caps support
        // the continuous body radius; dropping into the shallow court is safe.
        foreach(int x in new[]{4,15})foreach(int z in new[]{4,15})
            for(int dx=0;dx<2;dx++)for(int dz=0;dz<2;dz++)
                cells[x+dx,z+dz]=new CellDef{Kind=CellKind.Floor,Tier=2};
        cells[10,10]=new CellDef{Kind=CellKind.Pillar,ObstacleHeight=1.1f};
        return new StageLayout
        {
            Id=InTheGutter,Name="In the Gutter",Width=size,Height=size,Cells=cells,
            Spawns=[(4,4),(16,4),(4,16),(16,16),(10,2),(18,10),(10,18),(2,10)],
            SoftBlockDensity=1,MaxSoftBlocks=48,OpeningCoverPerSpawn=0,
            CoverAreas=[new("northwest-channel",.25,2,8,2,8,0,0),new("northeast-channel",.25,12,18,2,8,0,0),
                new("southwest-channel",.25,2,8,12,18,0,0),new("southeast-channel",.25,12,18,12,18,0,0)],
            ClearCells=Enumerable.Range(1,size-2).SelectMany(x=>Enumerable.Range(1,size-2).Select(z=>(X:x,Z:z)))
                .Where(c=>c.X is 1 or 9 or 10 or 11 or 19 || c.Z is 1 or 9 or 10 or 11 or 19).ToHashSet(),
        };
    }
}

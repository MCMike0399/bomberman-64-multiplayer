// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation.Stages;

public static partial class BlastStages
{
    public const string BlizzardBattle="blizzard-battle";

    private static StageLayout BuildBlizzardBattle()
    {
        const int size=19;
        var cells=Fill(size,size,CellDef.Floor0);
        for(int n=0;n<size;n++)
        {
            cells[0,n]=cells[size-1,n]=new CellDef{Kind=CellKind.Pillar,ObstacleHeight=2.2f};
            cells[n,0]=new CellDef{Kind=CellKind.Pillar,ObstacleHeight=1.05f};
            // Low near rim keeps the whole court readable from the battle camera.
            cells[n,size-1]=new CellDef{Kind=CellKind.Pillar,ObstacleHeight=.35f};
        }
        // Sample each quadrant independently. Only the perimeter loop and
        // central cross are reserved; individual spires are never mirrored.
        return new StageLayout
        {
            Id=BlizzardBattle,Name="Blizzard Battle",Width=size,Height=size,Cells=cells,
            Spawns=[(2,2),(16,2),(2,16),(16,16),(9,1),(9,17),(1,8),(17,10)],
            SoftBlockDensity=1,MaxSoftBlocks=64,OpeningCoverPerSpawn=0,SoftBlockHeight=1.3f,
            CoverAreas=[new("northwest-bank",.25,1,8,1,8),new("northeast-bank",.25,10,17,1,8),
                new("southwest-bank",.25,1,8,10,17),new("southeast-bank",.25,10,17,10,17)],
            ClearCells=Enumerable.Range(1,17).SelectMany(x=>Enumerable.Range(1,17).Select(z=>(X:x,Z:z)))
                .Where(c=>c.X is 1 or 9 or 17 || c.Z is 1 or 9 or 17).ToHashSet(),
        };
    }
}

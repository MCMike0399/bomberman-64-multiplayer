// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation;

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
        (int X,int Z)[] cones=[(6,2),(10,3),(9,4),(6,5),(10,5),(11,5),(14,7),(16,7),
            (5,8),(2,10),(6,10),(7,12),(15,12),(14,14),(10,15),(12,15)];
        return new StageLayout
        {
            Id=BlizzardBattle,Name="Blizzard Battle",Width=size,Height=size,Cells=cells,
            Spawns=[(2,2),(16,2),(2,16),(16,16),(9,1),(9,17),(1,8),(17,10)],
            SoftBlockDensity=1,MaxSoftBlocks=16,OpeningCoverPerSpawn=0,SoftBlockHeight=1.3f,
            CoverAreas=[new("ice-cones",1,1,17,1,17)],
            ClearCells=Enumerable.Range(1,17).SelectMany(x=>Enumerable.Range(1,17).Select(z=>(X:x,Z:z)))
                .Where(c=>!cones.Contains(c)).ToHashSet(),
        };
    }
}

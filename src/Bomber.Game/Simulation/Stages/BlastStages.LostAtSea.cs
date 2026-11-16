// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation.Stages;

public static partial class BlastStages
{
    public const string LostAtSea="lost-at-sea";

    // Official guide p107: flat slippery violet glass, grippy silver rim,
    // four orange towers in a diamond and central breakable shipping crates.
    private static StageLayout BuildLostAtSea()
    {
        const int size=21;
        var cells=Fill(size,size,CellDef.Floor0);
        for(int n=0;n<size;n++)
            cells[n,0]=cells[n,size-1]=cells[0,n]=cells[size-1,n]=new(){Kind=CellKind.Pillar,ObstacleHeight=.55f};
        foreach(var (x,z) in new[]{(10,6),(6,10),(14,10),(10,14)})
            cells[x,z]=new(){Kind=CellKind.Pillar,ObstacleHeight=2.1f};
        return new StageLayout
        {
            Id=LostAtSea,Name="Lost at Sea",Width=size,Height=size,Cells=cells,
            Spawns=[(2,2),(18,2),(2,18),(18,18),(10,2),(2,10),(18,10),(10,18)],
            SoftBlockDensity=1,MaxSoftBlocks=32,OpeningCoverPerSpawn=0,
            CoverAreas=[new("northwest-cargo",.25,5,9,5,9),new("northeast-cargo",.25,11,15,5,9),
                new("southwest-cargo",.25,5,9,11,15),new("southeast-cargo",.25,11,15,11,15)],
            ClearCells=Enumerable.Range(1,19).SelectMany(n=>new[]{(3,n),(17,n),(n,3),(n,17),(10,n),(n,10)}).ToHashSet()
        };
    }
}

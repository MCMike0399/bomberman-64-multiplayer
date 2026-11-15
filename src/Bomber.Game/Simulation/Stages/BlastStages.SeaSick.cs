// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation.Stages;

public static partial class BlastStages
{
    public const string SeaSick="sea-sick";

    // A pirate deck with one central mast and two shorter forward posts.
    // Side galleries and eight starts are original expansion tuning. Rocking is
    // presentation only: there is no current, ice, drift or altered gravity.
    private static StageLayout BuildSeaSick()
    {
        const int size=19;
        var cells=Fill(size,size,CellDef.Floor0);
        for(int x=0;x<size;x++)for(int z=0;z<size;z++)
        {
            bool gallery=x is <=2 or >=16;
            if(gallery)cells[x,z]=CellDef.Floor1;
            if(x is 0 or 18||z is 0 or 18)
                cells[x,z]=new CellDef{Kind=CellKind.Pillar,Tier=gallery?1:0,ObstacleHeight=.5f};
        }
        foreach(int z in new[]{4,5,13,14})
        {
            cells[3,z]=CellDef.Ramp0To1(-1,0);
            cells[15,z]=CellDef.Ramp0To1(1,0);
        }
        cells[9,8]=new CellDef{Kind=CellKind.Pillar,ObstacleHeight=5.2f};
        cells[5,11]=cells[13,11]=new CellDef{Kind=CellKind.Pillar,ObstacleHeight=1.4f};
        return new StageLayout
        {
            Id=SeaSick,Name="Sea Sick",Width=size,Height=size,Cells=cells,
            Spawns=[(4,2),(14,2),(4,16),(14,16),(1,4),(17,4),(1,14),(17,14)],
            SoftBlockDensity=1,MaxSoftBlocks=48,OpeningCoverPerSpawn=1,
            CoverAreas=[new("port-forward",.25,4,8,2,8),new("starboard-forward",.25,10,14,2,8),
                new("port-aft",.25,4,8,10,16),new("starboard-aft",.25,10,14,10,16)],
            ClearCells=Enumerable.Range(1,17).SelectMany(x=>Enumerable.Range(1,17).Select(z=>(X:x,Z:z)))
                .Where(c=>c.X is <=3 or >=15 or 9||c.Z is 4 or 5 or 9 or 13 or 14).ToHashSet(),
        };
    }
}

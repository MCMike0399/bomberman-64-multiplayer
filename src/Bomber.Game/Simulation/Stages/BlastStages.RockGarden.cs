// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Bomber.Game.Simulation.Rules;

namespace Bomber.Game.Simulation.Stages;

public static partial class BlastStages
{
    public const string RockGarden = "rock-garden";
    public const float MeteorWarningSeconds=1.2f;

    // Reference composition: an enclosed lawn, five isolated round columns,
    // a central paved cross and sparse boulders. Units/extra seats are our tuning.
    private static StageLayout BuildRockGarden()
    {
        const int size=19;
        var cells=Fill(size,size,CellDef.Floor0);
        for(int n=0;n<size;n++)
        {
            cells[n,0]=cells[n,size-1]=CellDef.Pillar;
            cells[0,n]=cells[size-1,n]=CellDef.Pillar;
        }
        foreach(var (x,z) in new[]{(9,3),(3,9),(9,9),(15,9),(9,15)})
            cells[x,z]=new CellDef{Kind=CellKind.Pillar,ObstacleHeight=x==9&&z==9?1.35f:BombThrowRules.PillarHeight};
        return new StageLayout
        {
            Id=RockGarden,Name="Rock Garden",Width=size,Height=size,Cells=cells,
            // First four seats retain the screenshot's quadrant arrangement.
            Spawns=[(5,5),(13,5),(5,13),(13,13),(2,2),(16,2),(2,16),(16,16)],
            SoftBlockDensity=1,MaxSoftBlocks=16,OpeningCoverPerSpawn=1,
            CoverAreas=[
                new("central-paving",.25,6,12,6,12){Centers=[(7,9),(9,7),(11,9),(9,11)]},
                new("northwest",.1875,1,8,1,8){Centers=[(4,3),(4,6)]},
                new("northeast",.1875,10,17,1,8){Centers=[(14,3),(14,6)]},
                new("southwest",.1875,1,8,10,17){Centers=[(4,12),(6,15)]},
                new("southeast",.1875,10,17,10,17){Centers=[(14,12),(12,15)]}],
            // Continuous routes around each side of the center column.
            ClearCells=Enumerable.Range(1,17).SelectMany(x=>Enumerable.Range(1,17).Select(z=>(X:x,Z:z)))
                .Where(c=>c.X is 8 or 10||c.Z is 8 or 10||
                    c.X is >=6 and <=12&&c.Z is >=6 and <=12&&Math.Abs(c.X-9)+Math.Abs(c.Z-9)>3).ToHashSet(),
        };
    }
}

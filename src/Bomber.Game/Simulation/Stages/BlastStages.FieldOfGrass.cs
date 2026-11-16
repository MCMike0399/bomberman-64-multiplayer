// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation.Stages;

public static partial class BlastStages
{
    public const string FieldOfGrass="field-of-grass";

    // Official guide p103: a grassy valley between two continuous side slopes.
    // Dimensions, eight starts and regional randomized cover are project tuning.
    private static float StageLayoutSurface(CellDef cell) => cell.Kind==CellKind.Ramp
        ?cell.RampFromTier+(cell.RampToTier-cell.RampFromTier)*.5f:cell.Tier;

    private static StageLayout BuildFieldOfGrass()
    {
        const int width=21,depth=19;
        var cells=Fill(width,depth,CellDef.Floor0);
        for(int x=1;x<width-1;x++)for(int z=1;z<depth-1;z++)
            cells[x,z]=x switch
            {
                <=2 or >=18=>CellDef.Floor2,
                3=>CellDef.Ramp(1,2,-1,0),
                4=>CellDef.Ramp(0,1,-1,0),
                16=>CellDef.Ramp(0,1,1,0),
                17=>CellDef.Ramp(1,2,1,0),
                _=>CellDef.Floor0
            };
        for(int x=0;x<width;x++)foreach(int z in new[]{0,depth-1})
            {
                float support=StageLayoutSurface(cells[x,1]);
                int tier=(int)MathF.Floor(support);
                cells[x,z]=new(){Kind=CellKind.Pillar,Tier=tier,ObstacleHeight=support-tier+.65f};
            }
        for(int z=0;z<depth;z++)foreach(int x in new[]{0,width-1})
            cells[x,z]=new(){Kind=CellKind.Pillar,Tier=2,ObstacleHeight=.65f};
        return new StageLayout
        {
            Id=FieldOfGrass,Name="Field of Grass",Width=width,Height=depth,Cells=cells,
            Spawns=[(2,3),(18,3),(2,15),(18,15),(2,7),(18,7),(2,11),(18,11)],
            SoftBlockDensity=1,MaxSoftBlocks=28,OpeningCoverPerSpawn=0,SoftBlockHeight=1.05f,
            CoverAreas=[new("north-meadow",.5,6,14,2,8),new("south-meadow",.5,6,14,10,16)],
            // The central crossing and bank approaches remain readable escape routes.
            ClearCells=Enumerable.Range(1,17).SelectMany(z=>new[]{(5,z),(15,z)})
                .Concat(Enumerable.Range(1,19).Select(x=>(x,9))).ToHashSet()
        };
    }
}

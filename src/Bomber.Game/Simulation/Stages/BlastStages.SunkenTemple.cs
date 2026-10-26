// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation.Stages;

public static partial class BlastStages
{
    public const string SunkenTemple = "sunken-temple";

    private static StageLayout BuildSunkenTemple()
    {
        const int width = 25, height = 27;
        var cells = Fill(width, height, CellDef.Water);
        void Floor(int x0, int x1, int z0, int z1, int tier)
        {
            for (var x = x0; x <= x1; x++)
                for (var z = z0; z <= z1; z++) cells[x, z] = new CellDef { Kind = CellKind.Floor, Tier = tier };
        }
        Floor(1, 23, 3, 25, 1);
        Floor(3, 21, 3, 10, 2);
        Floor(5, 19, 3, 7, 3);
        Floor(8, 16, 3, 4, 4);
        Floor(10, 14, 1, 2, 5);
        Floor(5, 19, 15, 25, 0);
        for (var x = 11; x <= 13; x++)
        {
            cells[x, 11] = CellDef.Ramp(1, 2, 0, -1);
            cells[x, 8] = CellDef.Ramp(2, 3, 0, -1);
            cells[x, 5] = CellDef.Ramp(3, 4, 0, -1);
            cells[x, 3] = CellDef.Ramp(4,5,0,-1);
        }
        foreach (var x in new[] {5,6,7,8,9,15,16,17,18,19}) cells[x, 14] = CellDef.Ramp(0, 1, 0, -1);
        return new StageLayout
        {
            Id = SunkenTemple, Name = "Sunken Temple", Width = width, Height = height, Cells = cells, RisingWater = true,
            SoftBlockDensity = .4, MaxSoftBlocks = 90,
            CoverAreas=[
                new("west-court",.3,5,11,15,25,0,0){Centers=[(6,17),(6,23),(10,23)]},
                new("east-court",.3,13,19,15,25,0,0){Centers=[(18,17),(18,23),(14,23)]},
                new("west-walkway",.075,1,4,15,25,1,1){Centers=[(1,21),(4,24)]},
                new("east-walkway",.075,20,23,15,25,1,1){Centers=[(23,21),(20,24)]},
                new("side-terraces",.15,3,21,3,10,2,3){Centers=[(5,8),(19,8),(7,4),(17,4)]},
                new("base-approaches",.1,1,23,3,14,1,1){Centers=[(3,11),(21,11)]}],
            TrainingItems = [(10,12,"fire"),(9,12,"fire"),(14,12,"bomb"),(10,13,"remote"),(14,13,"red"),(12,13,"heart"),(16,12,"bounce"),(8,13,"focus"),(9,15,"tiny"),(15,15,"big"),(10,16,"haste"),(14,16,"slow")],
            ClearCells = Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, height).Select(z => (X:x,Z:z)))
                .Where(c => cells[c.X,c.Z].Tier>=4 || (c.X>=11&&c.X<=13&&c.Z<=13) || c.Z is 12 or 13 ||
                    c.Z>=15&&(c.X is 3 or 8 or 12 or 16 or 21 || c.Z==20)).ToHashSet(),
            Spawns = [(12,12),(7,12),(17,12),(4,9),(20,6),(6,6),(10,18),(14,18)],
        };
    }
}

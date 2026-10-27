// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation.Stages;

public static partial class BlastStages
{
    public const string UpAndDown = "up-and-down";

    // A suspended square ring and crown, two middle piers, and the low reward
    // deck. North/south pairs of purple flights connect each adjacent elevation.
    // Below preserves the real routes underneath the front ring and south bridge.
    private static StageLayout BuildUpAndDown()
    {
        const int width = 24, height = 26;
        var cells = Fill(width, height, CellDef.Water);
        void Floor(int x0, int x1, int z0, int z1, int tier)
        {
            for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++)
                cells[x, z] = new CellDef { Kind = CellKind.Floor, Tier = tier };
        }
        // The middle landing edges meet the inward lips of the side rails.
        // A sea cell here makes ordinary weighty drops miss the pier entirely.
        Floor(3, 6, 7, 23, 2);
        Floor(17, 20, 7, 23, 2);
        Floor(10, 13, 12, 23, 0);
        Floor(10, 13, 7, 10, 4);
        // Each three-cell flight rises two tiers continuously, rather than
        // replacing the long original slopes with single-cell stair cliffs.
        foreach (int z in new[] { 7, 14, 21 })
            for (int step = 0; step < 3; step++)
            {
                bool upper = z <= 10;
                float from = upper ? 2 + step * 2f / 3 : step * 2f / 3;
                float to = upper ? 2 + (step + 1) * 2f / 3 : (step + 1) * 2f / 3;
                cells[upper ? 7 + step : 9 - step, z] = CellDef.Ramp(from, to, upper ? 1 : -1, 0);
                cells[upper ? 16 - step : 14 + step, z] = CellDef.Ramp(from, to, upper ? -1 : 1, 0);
            }
        void Bridge(int x, int z) => cells[x, z] = new CellDef
        {
            Kind = CellKind.Floor, Tier = 4, Below = cells[x, z]
        };
        for (int x = 2; x <= 21; x++) { Bridge(x, 2); Bridge(x, 21); }
        for (int z = 3; z < 21; z++) { Bridge(2, z); Bridge(21, z); }
        for (int z = 3; z <= 6; z++) Bridge(11, z);
        for (int z = 11; z <= 20; z++) Bridge(13, z);
        // Paired shelves face a continuous middle aisle on each pier and the
        // low deck. Crossings beside every flight and the upper ring stay open.
        var cover=new HashSet<(int X,int Z)>();
        foreach(int x in new[]{4,6,17,19})
            foreach(int z in new[]{10,11,16,17,18,23})cover.Add((x,z));
        foreach(int x in new[]{10,12})
            foreach(int z in new[]{12,13,16,17,18,23})cover.Add((x,z));
        var clear = new HashSet<(int X, int Z)>();
        for (int x = 0; x < width; x++) for (int z = 0; z < height; z++)
            if (!cover.Contains((x,z)))
                clear.Add((x, z));
        return new StageLayout
        {
            Id = UpAndDown, Name = "Up & Down", Width = width, Height = height,
            Cells = cells, RisingWater = true,
            Spawns = [(2,2),(21,21),(21,2),(2,21),(7,2),(16,2),(7,21),(16,21)],
            // Three equal caches reward the descent without obstructing a return.
            OpeningCoverPerSpawn = 0, SoftBlockDensity = 1, MaxSoftBlocks = 36,
            CoverAreas = [
                new("lower-deck", 1d/3, 10, 13, 12, 23, 0, 0) { Centers = [(10,16),(12,18),(10,23)] },
                new("west-pier", 1d/3, 4, 6, 7, 23, 2, 2) { Centers = [(4,17),(6,23)] },
                new("east-pier", 1d/3, 17, 19, 7, 23, 2, 2) { Centers = [(17,17),(19,23)] }],
            ClearCells = clear,
        };
    }
}

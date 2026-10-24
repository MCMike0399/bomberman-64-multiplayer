// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation;

/// <summary>Static definitions for the temple and two legacy arena stages.</summary>
public static class BlastStages
{
    public const string SunkenTemple = "sunken-temple";
    public const string RockGarden = "rock-garden";
    public const string UpAndDown = "up-and-down";

    /// <summary>Returns the stage layout for a given id. Falls back to Sunken Temple for unknown ids.</summary>
    public static StageLayout Get(string id) => id switch
    {
        SunkenTemple => BuildSunkenTemple(),
        RockGarden => BuildRockGarden(),
        UpAndDown => BuildUpAndDown(),
        _ => BuildSunkenTemple(),
    };

    // ─── Rock Garden (23×15, all flat tier 0) ──────────────────────────────
    // Open edges: no boundary wall, walking off the grid eliminates.
    // Hard pillars provide cover. Eight spawns at corners, edges and center edges.
    // ─────────────────────────────────────────────────────────────────────────

    private static StageLayout BuildRockGarden()
    {
        const int W = 23, H = 15;
        var cells = Fill(W, H, CellDef.Floor0);

        // Hard pillars — arranged symmetrically for fair play.
        int[] px = [3, 19, 3, 19,  7, 15, 7, 15,  10, 12, 5, 11, 17];
        int[] pz = [2,  2, 12, 12,  2, 2, 12, 12,  5,  5,  7,  7,  7];
        for (var i = 0; i < px.Length; i++)
            cells[px[i], pz[i]] = CellDef.Pillar;

        // Spawns: corners + midpoints of each edge, integer grid-cell coords.
        (int X, int Z)[] spawns =
        [
            (1, 1), (21, 1), (1, 13), (21, 13),
            (11, 1), (11, 13), (1, 7),  (21, 7),
        ];

        return new StageLayout
        {
            Id = RockGarden, Name = "Rock Garden",
            Width = W, Height = H,
            Cells = cells, Spawns = spawns,SoftBlockDensity=.6,MaxSoftBlocks=110,
            CoverAreas=[
                new("pillar-center",.5,7,15,0,14){Centers=[(8,4),(14,4),(8,10),(14,10)]},
                new("west-pockets",.25,0,6,0,14){Centers=[(4,3),(4,11)]},
                new("east-pockets",.25,16,22,0,14){Centers=[(18,3),(18,11)]}],
            ClearCells=Enumerable.Range(0,W).SelectMany(x=>Enumerable.Range(0,H).Select(z=>(X:x,Z:z)))
                .Where(c=>c.Z is 6 or 8||c.X is 2 or 11 or 20).ToHashSet(),
        };
    }

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
        Floor(4, 6, 7, 23, 2);
        Floor(17, 19, 7, 23, 2);
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
        var clear = new HashSet<(int X, int Z)>();
        for (int x = 0; x < width; x++) for (int z = 0; z < height; z++)
            if (cells[x, z].Tier == 4 || cells[x, z].Below != null || x is 5 or 11 or 18)
                clear.Add((x, z));
        return new StageLayout
        {
            Id = UpAndDown, Name = "Up & Down", Width = width, Height = height,
            Cells = cells, RisingWater = true,
            Spawns = [(2,2),(21,21),(21,2),(2,21),(7,2),(16,2),(7,21),(16,21)],
            // Twelve reference-inspired crates belong below the exposed upper ring.
            // Reward detours replace this project's former 88-block terrace farm.
            OpeningCoverPerSpawn = 0, SoftBlockDensity = 1, MaxSoftBlocks = 12,
            CoverAreas = [
                new("lower-deck", 2d/3, 10, 13, 12, 23, 0, 0) { Centers = [(10,16),(12,18),(10,23)] },
                new("west-pier", 1d/6, 4, 6, 7, 23, 2, 2) { Centers = [(4,17),(6,23)] },
                new("east-pier", 1d/6, 17, 19, 7, 23, 2, 2) { Centers = [(17,17),(19,23)] }],
            ClearCells = clear,
        };
    }

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
            TrainingItems = [(10,12,"fire"),(9,12,"fire"),(14,12,"bomb"),(10,13,"remote"),(14,13,"red"),(12,13,"heart"),(16,12,"bounce"),(17,13,"fast"),(8,13,"focus"),(9,15,"tiny"),(15,15,"big"),(10,16,"haste"),(14,16,"slow")],
            ClearCells = Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, height).Select(z => (X:x,Z:z)))
                .Where(c => cells[c.X,c.Z].Tier>=4 || (c.X>=11&&c.X<=13&&c.Z<=13) || c.Z is 12 or 13 ||
                    c.Z>=15&&(c.X is 3 or 8 or 12 or 16 or 21 || c.Z==20)).ToHashSet(),
            Spawns = [(12,12),(7,12),(17,12),(4,9),(20,6),(6,6),(10,18),(14,18)],
        };
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private static CellDef[,] Fill(int w, int h, CellDef def)
    {
        var cells = new CellDef[w, h];
        for (var x = 0; x < w; x++)
            for (var z = 0; z < h; z++)
                cells[x, z] = def;
        return cells;
    }
}

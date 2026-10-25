// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation;

/// <summary>Authored arena layouts shared by local and online authority.</summary>
public static partial class BlastStages
{
    public const string SunkenTemple = "sunken-temple";
    public const string RockGarden = "rock-garden";
    public const float MeteorWarningSeconds=1.2f;
    public const string UpAndDown = "up-and-down";
    public const string TopRules = "top-rules";

    /// <summary>Returns the stage layout for a given id. Falls back to Sunken Temple for unknown ids.</summary>
    public static StageLayout Get(string id) => id switch
    {
        CircuitGarden => BuildCircuitGarden(),
        SunkenTemple => BuildSunkenTemple(),
        RockGarden => BuildRockGarden(),
        UpAndDown => BuildUpAndDown(),
        TopRules => BuildTopRules(),
        _ => BuildSunkenTemple(),
    };

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
        // Original-inspired paired item blocks: eight on the low deck, two on
        // each middle pier. Coordinates are calibrated to our larger grid.
        var cover=new HashSet<(int X,int Z)> {(4,11),(4,18),(19,11),(19,18)};
        foreach(int x in new[]{10,12})foreach(int z in new[]{13,16,18,23})cover.Add((x,z));
        var clear = new HashSet<(int X, int Z)>();
        for (int x = 0; x < width; x++) for (int z = 0; z < height; z++)
            if (!cover.Contains((x,z)))
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

    // A two-story castle: three longitudinal bridges and one transverse bridge.
    // Bridge cells have a separate walkable floor below, rather than solid risers.
    // Dimensions/extra four spawns are Blast Circuit tuning; see docs/top-rules-research.md.
    private static StageLayout BuildTopRules()
    {
        const int width = 19, height = 17;
        var cells = Fill(width, height, CellDef.Floor0);
        var underpasses = new HashSet<(int X, int Z)>();
        for (var x = 0; x < width; x++)
            cells[x, 0] = cells[x, height - 1] = CellDef.Pillar;
        for (var z = 0; z < height; z++)
            cells[0, z] = cells[width - 1, z] = CellDef.Pillar;
        for (var z = 0; z < height; z++) for (var x = 0; x < width; x++)
            if (cells[x,z].Kind == CellKind.Pillar)
                cells[x,z] = new CellDef { Kind = CellKind.Pillar,
                    ObstacleHeight = z == 0 ? 4.4f : z == height - 1 ? 2 : 4.1f - .4f * z / (height - 1) };
        // Upper perimeter joins all six bridge ends and the four stair heads.
        for (var x = 1; x < width - 1; x++)
            cells[x, 1] = cells[x, height - 2] = CellDef.Floor2;
        for (var z = 1; z < height - 1; z++)
            cells[1, z] = cells[width - 2, z] = CellDef.Floor2;
        for (var z = 2; z <= 14; z++)
            foreach (var x in new[] { 5, 9, 13 })
            { cells[x, z] = CellDef.Floor2; underpasses.Add((x, z)); }
        for (var x = 2; x <= 16; x++)
        { cells[x, 8] = CellDef.Floor2; underpasses.Add((x, 8)); }
        foreach (var x in new[] { 2, 16 })
        {
            for (int step = 0; step < 4; step++)
            {
                cells[x, 2 + step] = CellDef.Ramp(1.5f - step * .5f, 2 - step * .5f, 0, -1);
                cells[x, 11 + step] = CellDef.Ramp(step * .5f, .5f + step * .5f, 0, 1);
            }
        }
        foreach(var (x,z) in underpasses)cells[x,z]=new CellDef {Kind=CellKind.Floor,Tier=cells[x,z].Tier,Below=CellDef.Floor0};
        return new StageLayout
        {
            Id = TopRules, Name = "Top Rules", Width = width, Height = height, DecksBlockBlasts=false,
            Cells = cells,
            Spawns = [(5, 2), (13, 2), (5, 14), (13, 14), (9, 2), (9, 14), (1, 8), (17, 8)],
            SoftBlockDensity = .32, MaxSoftBlocks = 12,
            CoverAreas = [new("lower-west", .5, 3, 8, 3, 13, 0, 0),
                          new("lower-east", .5, 10, 15, 3, 13, 0, 0)],
            // Keep the entire upper network, stairs and lower through routes clear.
            ClearCells = Enumerable.Range(0, width).SelectMany(x => Enumerable.Range(0, height).Select(z => (X: x, Z: z)))
                .Where(c => cells[c.X, c.Z].Tier > 0 || cells[c.X, c.Z].Kind == CellKind.Ramp ||
                    c.X is 2 or 4 or 10 or 16 || c.Z is 6 or 8 or 10).ToHashSet(),
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
            TrainingItems = [(10,12,"fire"),(9,12,"fire"),(14,12,"bomb"),(10,13,"remote"),(14,13,"red"),(12,13,"heart"),(16,12,"bounce"),(8,13,"focus"),(9,15,"tiny"),(15,15,"big"),(10,16,"haste"),(14,16,"slow")],
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

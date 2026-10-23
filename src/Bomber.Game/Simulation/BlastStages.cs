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

    // ─── Up & Down (23×19, three tiers connected by ramps, water surround) ─
    // Outer two rows/cols are water. Tier 0 ring, tier 1 inner rectangle,
    // tier 2 small center platform. Four ramps at each tier boundary.
    // ─────────────────────────────────────────────────────────────────────────

    private static StageLayout BuildUpAndDown()
    {
        const int W=23,H=19;
        var cells=Fill(W,H,CellDef.Water);
        for(int x=2;x<=20;x++)for(int z=2;z<=16;z++)cells[x,z]=CellDef.Floor0;
        for(int x=7;x<=15;x++)for(int z=6;z<=12;z++)cells[x,z]=CellDef.Floor1;
        for(int x=10;x<=12;x++)for(int z=8;z<=10;z++)cells[x,z]=CellDef.Floor2;
        cells[11,5]=CellDef.Ramp0To1(0,1);cells[11,13]=CellDef.Ramp0To1(0,-1);
        cells[6,9]=CellDef.Ramp0To1(1,0);cells[16,9]=CellDef.Ramp0To1(-1,0);
        cells[11,7]=CellDef.Ramp1To2(0,1);cells[11,11]=CellDef.Ramp1To2(0,-1);
        cells[9,9]=CellDef.Ramp1To2(1,0);cells[13,9]=CellDef.Ramp1To2(-1,0);
        foreach(int x in new[]{3,19,6,16})foreach(int z in new[]{3,15})cells[x,z]=CellDef.Pillar;
        (int X,int Z)[] spawns=[(3,2),(19,2),(3,16),(19,16),(7,7),(15,11),(10,9),(12,9)];

        return new StageLayout
        {
            Id = UpAndDown, Name = "Up & Down",
            Width = W, Height = H,
            Cells = cells, Spawns = spawns,SoftBlockDensity=.6,MaxSoftBlocks=96,
            CoverAreas=[
                new("northwest-ring",.2,2,10,2,8,0,0){Centers=[(4,4),(8,3)]},
                new("northeast-ring",.2,12,20,2,8,0,0){Centers=[(18,4),(14,3)]},
                new("southwest-ring",.2,2,10,10,16,0,0){Centers=[(4,14),(8,15)]},
                new("southeast-ring",.2,12,20,10,16,0,0){Centers=[(18,14),(14,15)]},
                new("wide-platforms",.2,7,15,6,12,1,1){Centers=[(8,7),(14,11)]}],
            ClearCells=Enumerable.Range(0,W).SelectMany(x=>Enumerable.Range(0,H).Select(z=>(X:x,Z:z)))
                .Where(c=>c.X==11||c.Z==9||cells[c.X,c.Z].Tier>=2||
                    cells[c.X,c.Z].Tier==0&&(c.X is 5 or 17||c.Z is 5 or 13)).ToHashSet(),
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
            Id = SunkenTemple, Name = "Sunken Temple", Width = width, Height = height, Cells = cells,
            SoftBlockDensity = .4, MaxSoftBlocks = 90,
            CoverAreas=[
                new("west-court",.3,5,11,15,25,0,0){Centers=[(6,17),(6,23),(10,23)]},
                new("east-court",.3,13,19,15,25,0,0){Centers=[(18,17),(18,23),(14,23)]},
                new("west-walkway",.075,1,4,15,25,1,1){Centers=[(1,21),(4,24)]},
                new("east-walkway",.075,20,23,15,25,1,1){Centers=[(23,21),(20,24)]},
                new("side-terraces",.15,3,21,3,10,2,3){Centers=[(5,8),(19,8),(7,4),(17,4)]},
                new("base-approaches",.1,1,23,3,14,1,1){Centers=[(3,11),(21,11)]}],
            TrainingItems = [(10,12,"fire"),(9,12,"fire"),(14,12,"bomb"),(10,13,"remote"),(14,13,"red"),(12,13,"heart"),(16,12,"bounce"),(17,13,"fast"),(8,13,"focus")],
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

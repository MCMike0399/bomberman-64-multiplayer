// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation.Stages;

public static partial class BlastStages
{
    public const string TopRules = "top-rules";

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
}

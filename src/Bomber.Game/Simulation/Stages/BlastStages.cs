// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation.Stages;

/// <summary>Authored arena layouts shared by local and online authority.</summary>
public static partial class BlastStages
{
    /// <summary>Returns the stage layout for a given id. Falls back to Sunken Temple for unknown ids.</summary>
    public static StageLayout Get(string id) => id switch
    {
        CircuitGarden => BuildCircuitGarden(),
        SunkenTemple => BuildSunkenTemple(),
        RockGarden => BuildRockGarden(),
        UpAndDown => BuildUpAndDown(),
        TopRules => BuildTopRules(),
        BlizzardBattle => BuildBlizzardBattle(),
        GreedyTrap => BuildGreedyTrap(),
        _ => BuildSunkenTemple(),
    };

    // Each arena's id constant and builder live in its BlastStages.<Arena>.cs partial.

    /// <summary>Seconds between Hurry column closures; clients pace the advancing wall face with it.</summary>
    public static float HurryColumnSeconds(string id) => id == BlizzardBattle ? Rules.BlizzardRules.AvalancheStepSeconds : 1f;

    private static CellDef[,] Fill(int w, int h, CellDef def)
    {
        var cells = new CellDef[w, h];
        for (var x = 0; x < w; x++)
            for (var z = 0; z < h; z++)
                cells[x, z] = def;
        return cells;
    }
}

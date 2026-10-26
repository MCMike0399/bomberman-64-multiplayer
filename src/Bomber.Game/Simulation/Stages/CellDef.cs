// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.

namespace Bomber.Game.Simulation.Stages;

/// <summary>One grid cell in a stage layout.</summary>
public sealed class CellDef
{
    public CellKind Kind { get; init; }
    public int Tier { get; init; }
    /// <summary>Authored hard-wall height; null preserves existing pillar rules.</summary>
    public float? ObstacleHeight { get; init; }
    /// <summary>For ramps: the lower tier this ramp connects from.</summary>
    public float RampFromTier { get; init; }
    /// <summary>For ramps: the higher tier this ramp leads to.</summary>
    public float RampToTier { get; init; }
    /// <summary>For ramps: the column direction from low-tier to high-tier (−1, 0, or 1).</summary>
    public int RampDX { get; init; }
    /// <summary>For ramps: the row direction from low-tier to high-tier.</summary>
    public int RampDZ { get; init; }
    /// <summary>A suspended deck has a second surface (or open water) beneath it.
    /// Only the thin deck is solid; it must not become a wall down to the sea.</summary>
    public CellDef? Below { get; init; }
    public const float DeckThickness = .16f;

    public static readonly CellDef Floor0 = new() { Kind = CellKind.Floor, Tier = 0 };
    public static readonly CellDef Floor1 = new() { Kind = CellKind.Floor, Tier = 1 };
    public static readonly CellDef Floor2 = new() { Kind = CellKind.Floor, Tier = 2 };
    public static readonly CellDef Pillar = new() { Kind = CellKind.Pillar };
    public static readonly CellDef Void = new() { Kind = CellKind.Void };
    public static readonly CellDef Water = new() { Kind = CellKind.Water };

    public bool IsElimination => Kind is CellKind.Void or CellKind.Water;
    public bool IsPassable => Kind is CellKind.Floor or CellKind.Ramp;

    public static CellDef Ramp(float from, float to, int dx, int dz) =>
        new() { Kind = CellKind.Ramp, Tier = (int)MathF.Floor(from), RampFromTier = from, RampToTier = to, RampDX = dx, RampDZ = dz };

    public static CellDef Ramp0To1(int dx, int dz) =>
        new() { Kind = CellKind.Ramp, Tier = 0, RampFromTier = 0, RampToTier = 1, RampDX = dx, RampDZ = dz };

    public static CellDef Ramp1To2(int dx, int dz) =>
        new() { Kind = CellKind.Ramp, Tier = 1, RampFromTier = 1, RampToTier = 2, RampDX = dx, RampDZ = dz };
}

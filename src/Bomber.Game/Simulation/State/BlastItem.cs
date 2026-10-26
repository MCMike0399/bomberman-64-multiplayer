// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Bomber.Game.Simulation.Rules;

namespace Bomber.Game.Simulation.State;

/// <summary>A collectible power-up item.</summary>
public sealed class BlastItem
{
    public string Id { get; init; } = "";

    public int CellX { get; init; }
    public int CellZ { get; init; }
    public int Tier { get; init; }
    public string Type { get; init; } = "";
    public float LifeRemaining { get; set; } = PowerUpRules.ItemLifetime;
    public string? DroppedBy { get; init; }
    public float OwnerLockRemaining { get; set; }

    public float X => CellX + 0.5f;
    public float Z => CellZ + 0.5f;
}

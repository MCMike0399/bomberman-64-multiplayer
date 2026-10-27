// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.

namespace Bomber.Game.Simulation.State;

/// <summary>A short-lived spherical blast zone after an explosion.</summary>
public sealed class BlastHazard
{
    public float Y { get; init; }
    public string Id { get; init; } = "";

    public float X { get; init; }
    public float Z { get; init; }
    public int Tier { get; init; }
    public float Radius { get; init; }
    public float LifeTimer { get; set; } = 0.4f; // seconds it stays hazardous
    public bool IsRed { get; init; }
    public string? OwnerId { get; init; }
    internal SoftBlockGrid CoverAtIgnition { get; init; } = new(0, 0);
}

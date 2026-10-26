// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.

namespace Bomber.Game.Simulation.State;

/// <summary>One player's input as received from the client.</summary>
public readonly record struct InputState(float X, float Z, bool A, bool B, bool R, bool Aim = false, bool Stop = false)
{
    public float Length => MathF.Sqrt(X * X + Z * Z);
}

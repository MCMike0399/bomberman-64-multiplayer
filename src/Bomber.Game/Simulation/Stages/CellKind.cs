// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.

namespace Bomber.Game.Simulation.Stages;

public enum CellKind : byte
{
    Floor = 0,
    Pillar = 1,   // hard impassable, blocks explosions
    Void = 2,     // elimination on contact
    Water = 3,    // elimination on contact
    Ramp = 4,     // smooth height transition between two adjacent tiers
}

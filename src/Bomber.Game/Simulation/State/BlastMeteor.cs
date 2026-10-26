// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Bomber.Game.Simulation.Stages;

namespace Bomber.Game.Simulation.State;

// A published warning and falling fireball. Only authority advances its impact.
internal sealed class BlastMeteor
{
    public required string Id {get;init;}
    public float X {get;init;}
    public float Z {get;init;}
    public float Remaining {get;set;}=BlastStages.MeteorWarningSeconds;
}

// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Bomber.Game.Simulation.Rules;

namespace Bomber.Game.Simulation.State;

/// <summary>One bomb in the simulation.</summary>
public sealed class BlastBomb
{
    public string Id { get; init; } = "";

    public string? OwnerId { get; set; }
    public string? LastTouchedById { get; set; }
    public bool OwnerMayLeave { get; set; } // one-way permission to leave a new near-foot placement
    public float X { get; set; }
    public float Z { get; set; }
    public int Tier { get; set; }
    public int CellX { get; set; }  // grid cell (integer)
    public int CellZ { get; set; }

    public BombState State { get; set; } = BombState.Resting;
    public float VX { get; set; }    // velocity when rolling
    public float VZ { get; set; }
    public float KickHopRemaining { get; set; }
    public bool ResumeRollingAfterFall { get; set; } // terrain drop preserves an existing kick
    public float FlyVX { get; set; } // velocity when flying (XZ plane)
    public float FlyVZ { get; set; }
    public float FlyVY { get; set; } // vertical velocity (for arc)
    public float? LandingY { get; set; } // exact support after a thrown landing, including cover tops
    public float FlyY { get; set; }  // current Y above landing tier

    public int FireLevel { get; set; } = 1;   // 1–5
    public int Pumps { get; set; }             // 0–3 additional pumps
    public string Mode { get; set; } = "normal";
    public bool IsRed { get=>Mode=="red";set{if(value)Mode="red";else if(IsRed)Mode="normal";} }
    public bool IsRemote { get=>Mode=="remote";set{if(value)Mode="remote";else if(IsRemote)Mode="normal";} }
    public bool IsBouncy => Mode=="bounce";
    public float GravityScale => BombThrowRules.Weight(Pumps);
    public int Bounces { get; set; }
    public int RequestedPumps { get; set; }
    public float ChargeRemaining { get; set; }
    public float ChargeDuration { get; set; }

    public float FuseTimer { get; set; } = FuseSeconds;
    public bool Exploded { get; set; }

    public const float FuseSeconds = 2.5f;
    public const float RollSpeed = 12f;   // cells/sec while rolling
    public const float KickHopSeconds = .28f;
    public const float KickHopHeight = .18f;
    public const float ImpactHorizontalSpeed = 2.2f;
    public const float ImpactUpSpeed = 2.6f;
    public const float FlySpeed = 6f;    // cells/sec horizontal throw
    public const float Gravity = 9.8f;
    public const float ThrowAngle = 50f; // degrees
    public const float Radius = 0.35f;

    /// <summary>Effective blast radius: 0.9 cells per effective level plus 0.4; mega levels cap at eight.</summary>
    public int EffectiveLevel => Math.Clamp(FireLevel + Pumps, 1, Pumps > 0 ? PowerUpRules.MaximumMegaFire : PowerUpRules.MaximumNormalFire);
    public float BlastRadius => (EffectiveLevel * .9f + .4f) * (IsRed?1.25f:1f);
}

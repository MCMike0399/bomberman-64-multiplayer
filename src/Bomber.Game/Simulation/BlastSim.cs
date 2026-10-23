// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using System.Numerics;

namespace Bomber.Game.Simulation;

// ─── cell / stage ─────────────────────────────────────────────────────────────

public enum CellKind : byte
{
    Floor = 0,
    Pillar = 1,   // hard impassable, blocks explosions
    Void = 2,     // elimination on contact
    Water = 3,    // elimination on contact
    Ramp = 4,     // smooth height transition between two adjacent tiers
}

/// <summary>One grid cell in a stage layout.</summary>
public sealed class CellDef
{
    public CellKind Kind { get; init; }
    public int Tier { get; init; }
    /// <summary>For ramps: the lower tier this ramp connects from.</summary>
    public int RampFromTier { get; init; }
    /// <summary>For ramps: the higher tier this ramp leads to.</summary>
    public int RampToTier { get; init; }
    /// <summary>For ramps: the column direction from low-tier to high-tier (−1, 0, or 1).</summary>
    public int RampDX { get; init; }
    /// <summary>For ramps: the row direction from low-tier to high-tier.</summary>
    public int RampDZ { get; init; }

    public static readonly CellDef Floor0 = new() { Kind = CellKind.Floor, Tier = 0 };
    public static readonly CellDef Floor1 = new() { Kind = CellKind.Floor, Tier = 1 };
    public static readonly CellDef Floor2 = new() { Kind = CellKind.Floor, Tier = 2 };
    public static readonly CellDef Pillar = new() { Kind = CellKind.Pillar };
    public static readonly CellDef Void = new() { Kind = CellKind.Void };
    public static readonly CellDef Water = new() { Kind = CellKind.Water };

    public bool IsElimination => Kind is CellKind.Void or CellKind.Water;
    public bool IsPassable => Kind is CellKind.Floor or CellKind.Ramp;

    public static CellDef Ramp(int from, int to, int dx, int dz) =>
        new() { Kind = CellKind.Ramp, Tier = from, RampFromTier = from, RampToTier = to, RampDX = dx, RampDZ = dz };

    public static CellDef Ramp0To1(int dx, int dz) =>
        new() { Kind = CellKind.Ramp, Tier = 0, RampFromTier = 0, RampToTier = 1, RampDX = dx, RampDZ = dz };

    public static CellDef Ramp1To2(int dx, int dz) =>
        new() { Kind = CellKind.Ramp, Tier = 1, RampFromTier = 1, RampToTier = 2, RampDX = dx, RampDZ = dz };
}

/// <summary>The static layout of one stage.</summary>
public sealed class StageLayout
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Width { get; init; }   // columns (X)
    public int Height { get; init; }  // rows (Z)
    public CellDef[,] Cells { get; init; } = new CellDef[0, 0];
    public (int X, int Z)[] Spawns { get; init; } = [];
    // Fixed layouts are a local, assembly-internal fixture seam; no network message can set them.
    public IReadOnlyList<(int X,int Z,string Type)> TrainingItems { get; init; } = [];
    internal IReadOnlyList<(int X,int Z,string Type)> InitialItems { get; init; } = [];
    internal IReadOnlySet<(int X,int Z)>? FixedSoftBlocks { get; init; }
    public double SoftBlockDensity { get; init; } = .45;
    public int MaxSoftBlocks { get; init; } = int.MaxValue;
    public IReadOnlySet<(int X, int Z)> ClearCells { get; init; } = new HashSet<(int, int)>();

    public CellDef At(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return CellDef.Void;
        return Cells[x, z];
    }

    /// <summary>Continuous ground elevation. Heights are gameplay units; rendering may scale them uniformly.</summary>
    public float SurfaceAt(float x, float z)
    {
        var cx = (int)MathF.Floor(x); var cz = (int)MathF.Floor(z);
        var cell = At(cx, cz);
        if (cell.Kind != CellKind.Ramp) return cell.Tier;
        var progress = cell.RampDX != 0 ? (cell.RampDX > 0 ? x - cx : 1 - (x - cx))
            : (cell.RampDZ > 0 ? z - cz : 1 - (z - cz));
        return cell.RampFromTier + Math.Clamp(progress, 0, 1) * (cell.RampToTier - cell.RampFromTier);
    }

    /// <summary>A shared edge is traversable only when its entire surface is continuous.
    /// This prevents side-entry into stairs and climbing vertical terrace walls.</summary>
    public bool CanCross(int ax, int az, int bx, int bz)
    {
        if (Math.Abs(ax - bx) + Math.Abs(az - bz) != 1) return ax == bx && az == bz;
        var a = At(ax, az); var b = At(bx, bz);
        if (!a.IsPassable || !b.IsPassable) return false;
        for (var i = 1; i <= 3; i++)
        {
            var u = i * .25f;
            var x = ax != bx ? Math.Max(ax, bx) : ax + u;
            var z = az != bz ? Math.Max(az, bz) : az + u;
            var dx = (bx - ax) * .001f; var dz = (bz - az) * .001f;
            if (Math.Abs(SurfaceAt(x - dx, z - dz) - SurfaceAt(x + dx, z + dz)) > .01f) return false;
        }
        return true;
    }

    /// <summary>Movement can leave a higher edge; returning upward still needs a ramp.</summary>
    public bool CanWalk(int ax,int az,int bx,int bz)
    {
        if(CanCross(ax,az,bx,bz))return true;
        if(Math.Abs(ax-bx)+Math.Abs(az-bz)!=1||!At(ax,az).IsPassable||!At(bx,bz).IsPassable)return false;
        for(int i=1;i<=3;i++)
        {
            float u=i*.25f,x=ax!=bx?Math.Max(ax,bx):ax+u,z=az!=bz?Math.Max(az,bz):az+u;
            float dx=(bx-ax)*.001f,dz=(bz-az)*.001f;
            if(SurfaceAt(x+dx,z+dz)>SurfaceAt(x-dx,z-dz)+.01f)return false;
        }
        return true;
    }

    public bool IsHardCell(int x, int z) => At(x, z).Kind == CellKind.Pillar;
}

// ─── entity data ─────────────────────────────────────────────────────────────

public enum BombState : byte { Resting = 0, Rolling = 1, Carried = 2, Flying = 3 }
public enum RoundPhase : byte { Ready = 0, Fight = 1, Hurry = 2, Over = 3 }

/// <summary>One player in the simulation.</summary>
public sealed class BlastPlayer
{
    public string Id { get; init; } = "";
    public int Slot { get; init; }
    public int Appearance { get; set; }
    public bool IsBot { get; init; }
    public string DisplayName { get; init; } = "";

    // Per-tick body contact bookkeeping; not replicated presentation state.
    internal float BodyStartX,BodyStartZ,BodyYield;

    // Position and orientation.
    public float X { get; set; }
    public float Z { get; set; }
    public int Tier { get; set; }
    public float FacingX { get; set; } = 0;
    public float FacingZ { get; set; } = 1;

    // Vital state.
    public bool Alive { get; set; } = true;
    public bool Ghost { get; set; }

    // Timed state.
    public float StunTimer { get; set; }       // seconds remaining stunned
    public float InvincibleTimer { get; set; } // damage protection from a heart or respawn
    public float ImpactProtectionTimer { get; set; } // brief bomb-contact cooldown; never blocks blasts
    public string? KnockdownReason { get; set; }
    public string? CarriedById { get; set; }
    public float RecoveryTotal { get; set; }
    public float HoldRemaining { get; set; }
    public float StruggleCooldown { get; set; }
    public float RecoveryX { get; set; }
    public float RecoveryZ { get; set; }
    public bool Airborne { get; set; }
    public bool Falling { get; set; }
    public float AirVX { get; set; }
    public float AirVZ { get; set; }
    public float AirVY { get; set; }

    // A contact kick commits a direction for its brief planted anticipation.
    public string? KickBombId { get; set; }
    public float KickWindupRemaining { get; set; }
    public float KickRecoveryRemaining { get; set; }
    public float KickDirectionX { get; set; }
    public float KickDirectionZ { get; set; }

    // Carrying.
    public bool CarryFromPrimary { get; set; }
    public string? PrimaryBombCandidateId { get; set; }
    public float PrimaryHoldTimer { get; set; }
    public string? CarryingBombId { get; set; }   // bomb id if carrying a bomb
    public string? CarryingPlayerId { get; set; } // player id if carrying a stunned player

    // Deliberate throwing intent belongs to the carrier, never the client or target.
    public bool Aiming { get; set; }
    public bool AimSet { get; set; }
    public float AimX { get; set; }
    public float AimZ { get; set; } = 1;
    public float AimRange { get; set; } = BombThrowRules.DefaultAimRange;
    internal float AimCursorXAge,AimCursorZAge;
    internal int AimCursorXSign,AimCursorZSign;

    // Power-ups.
    public int BombCapacity { get; set; } = PowerUpRules.StartingBombs;  // max active bombs
    public int FireLevel { get; set; } = 1;      // 1–3 normal, pumping raises effective level
    public bool HasRemote { get; set; }
    public bool HasRed { get; set; }
    public long PickupSerial { get; set; }
    public string? LastPickup { get; set; }
    public long PumpSerial { get; set; }
    public bool HasHeart { get; set; }

    // Score.
    public int Wins { get; set; }

    // Ghost mechanics (latching onto a living player).
    public string? LatchedToId { get; set; }
    public float LatchTimer { get; set; }
    public int LatchReverseCount { get; set; }
    public float LatchReverseCooldown { get; set; }
    public float LatchPrevDX { get; set; }
    public float LatchPrevDZ { get; set; }

    // Input tracking.
    public InputState Input { get; set; }
    public bool PrevA { get; set; }
    public bool PrevB { get; set; }
    public bool PrevR { get; set; }

    // Bot AI state.
    public float BotThinkTimer { get; set; }
    public int BotGoalX { get; set; }
    public int BotGoalZ { get; set; }
    public bool BotPlacingBomb { get; set; }
    public bool BotBombTapPending { get; set; }

    // Spectator state: joined in progress, waits for next round.
    public bool PendingRoundStart { get; set; }

    public int ActiveBombs { get; set; } // count of live (non-resting-picked-up) bombs

    public float SurfaceY { get; set; }
    public float Y => SurfaceY;  // continuous authoritative surface elevation
}

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

    public int FireLevel { get; set; } = 1;   // 1–3
    public int Pumps { get; set; }             // 0–3 additional pumps
    public bool IsRed { get; set; }            // piercing blast
    public bool IsRemote { get; set; }         // detonated by owner's R button

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

    /// <summary>Effective blast radius: 0.9 cells per effective level plus 0.4; mega levels cap at six.</summary>
    public int EffectiveLevel => Math.Clamp(FireLevel + Pumps, 1, Pumps > 0 ? PowerUpRules.MaximumMegaFire : PowerUpRules.MaximumNormalFire);
    public float BlastRadius => EffectiveLevel * .9f + .4f;
}

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
}

/// <summary>A collectible power-up item.</summary>
public sealed class BlastItem
{
    public string Id { get; init; } = "";

    public int CellX { get; init; }
    public int CellZ { get; init; }
    public int Tier { get; init; }
    public string Type { get; init; } = "";  // "fire", "bomb", "remote", "red", "heart"

    public float X => CellX + 0.5f;
    public float Z => CellZ + 0.5f;
}

/// <summary>The mutable set of soft blocks still standing (one bool per grid cell).</summary>
public sealed class SoftBlockGrid
{
    private readonly bool[,] _alive;

    public SoftBlockGrid(int width, int height) => _alive = new bool[width, height];

    public bool IsAlive(int x, int z)
    {
        if (x < 0 || x >= _alive.GetLength(0) || z < 0 || z >= _alive.GetLength(1))
            return false;
        return _alive[x, z];
    }

    public void Set(int x, int z, bool value) => _alive[x, z] = value;

    /// <summary>Compact bitset as base64 (row-major).</summary>
    public string ToBase64()
    {
        var w = _alive.GetLength(0);
        var h = _alive.GetLength(1);
        var bits = new byte[(w * h + 7) / 8];
        for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
            {
                var idx = z * w + x;
                if (_alive[x, z])
                    bits[idx / 8] |= (byte)(1 << (idx % 8));
            }
        return Convert.ToBase64String(bits);
    }
}

/// <summary>One player's input as received from the client.</summary>
public readonly record struct InputState(float X, float Z, bool A, bool B, bool R, bool Aim = false)
{
    public float Length => MathF.Sqrt(X * X + Z * Z);
}

// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.

namespace Bomber.Game.Simulation;

/// <summary>Ghost latches that ride and steer a living victim.</summary>
public sealed partial class BlastSession
{
    // ─── ghost latch ─────────────────────────────────────────────────────────

    private void TryLatch(BlastPlayer ghost)
    {
        foreach (var p in _players.Values)
        {
            if (p.Ghost || !p.Alive || p.LatchedToId is not null) continue;
            if (p.Tier != ghost.Tier) continue;
            if (Dist2(ghost.X, ghost.Z, p.X, p.Z) > (PlayerRadius * 2) * (PlayerRadius * 2)) continue;

            // Already claimed by another ghost?
            if (_players.Values.Any(g => g.Ghost && g.LatchedToId == p.Id)) continue;

            ghost.LatchedToId = p.Id;
            ghost.LatchTimer = 4f;
            ghost.LatchReverseCount = 0;
            ghost.LatchPrevDX = p.FacingX;
            ghost.LatchPrevDZ = p.FacingZ;
            Present("ghost-latch",ghost.Id,p.Id,p.X,p.Y,p.Z);
            break;
        }
    }

    private void SynchronizeLatchedGhosts()
    {
        foreach(var ghost in _players.Values)
            if(ghost.LatchedToId is {} id&&_players.TryGetValue(id,out var victim))
            {ghost.X=victim.X;ghost.Z=victim.Z;ghost.SurfaceY=victim.Y;ghost.Tier=victim.Tier;}
    }

    private void TrackLatch(BlastPlayer ghost, BlastPlayer victim, float dt)
    {
        // Ghost moves with the victim.
        ghost.X = victim.X;
        ghost.Z = victim.Z;
        ghost.Tier = victim.Tier;
        ghost.SurfaceY = victim.Y;

        ghost.LatchTimer -= dt;

        // Track direction reversals for shake-off.
        ghost.LatchReverseCooldown -= dt;
        var vdx = victim.Input.X;
        var vdz = victim.Input.Z;
        if (vdx * vdx + vdz * vdz > 0.2f && ghost.LatchReverseCooldown <= 0)
        {
            if (vdx * ghost.LatchPrevDX + vdz * ghost.LatchPrevDZ < -0.5f)
            {
                ghost.LatchReverseCount++;
                ghost.LatchReverseCooldown = 0.15f;
            }
            ghost.LatchPrevDX = vdx;
            ghost.LatchPrevDZ = vdz;
        }

        // Shake-off: 6 reversals or 4 seconds elapsed.
        if (ghost.LatchReverseCount >= 6 || ghost.LatchTimer <= 0)
            DetachGhost(ghost);
    }

    private void DetachGhost(BlastPlayer ghost)
    {
        string reason=ghost.LatchReverseCount>=6?"shaken-off":"expired";
        var victim=ghost.LatchedToId;
        ghost.LatchedToId = null;
        ghost.LatchTimer = 0;
        ghost.LatchReverseCount = 0;
        ghost.StunTimer = 0.5f; // give the victim time to move away before another latch
        Present("ghost-detach",ghost.Id,victim,ghost.X,ghost.Y,ghost.Z,reason:reason);
    }

    private void DetachIfLatched(BlastPlayer p)
    {
        if (p.LatchedToId is not null) DetachGhost(p);
    }
}

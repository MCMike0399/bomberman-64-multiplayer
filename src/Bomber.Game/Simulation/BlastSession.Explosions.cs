// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Burbuja.Realtime.Abstractions;

namespace Bomber.Game.Simulation;

/// <summary>Fuses ending in spherical blasts, destructible cover and timed hazards.</summary>
public sealed partial class BlastSession
{
    // ─── explosions ───────────────────────────────────────────────────────────

    private void ExplodeBomb(ISessionContext context, BlastBomb bomb)
    {
        if (bomb.Exploded) return;
        bomb.Exploded = true;
        DecrementOwnerBombs(bomb.OwnerId);

        var r = bomb.BlastRadius;
        PresentBomb("blast", bomb, bomb.OwnerId, bomb.EffectiveLevel);

        // Create hazard sphere.
        _hazards.Add(new BlastHazard
        {
            Id = NextHazardId(),
            X = bomb.X, Z = bomb.Z, Tier = bomb.Tier, Y = BombY(bomb),
            Radius = r, IsRed = bomb.IsRed,
        });

        // Resolve visibility before destroying cover: the same explosion cannot remove a shield and
        // retroactively hit a player behind it. Chain candidates obey hard terrain/cover too.
        var victims = _players.Values.Where(p => p.InvincibleTimer <= 0 &&
            InBlast(bomb.X, bomb.Z, BombY(bomb), r, p.X, p.Z, p.Y, bomb.IsRed)).ToArray();
        var chains = _bombs.Where(other => !other.Exploded && other.State != BombState.Carried &&
            InBlast(bomb.X, bomb.Z, BombY(bomb), r + .5f, other.X, other.Z, BombY(other), bomb.IsRed)).ToArray();
        DestroyBlocksInBlast(context, bomb.X, bomb.Z, r, bomb.IsRed, BombY(bomb));
        foreach (var player in victims) HitPlayer(context, player, "explosion", bomb.OwnerId);
        foreach (var other in chains) ExplodeBomb(context, other);
    }

    private void DestroyBlocksInBlast(ISessionContext context, float bx, float bz, float r, bool red, float y)
    {
        var hits = new List<(int X, int Z)>();
        for (var x = Math.Max(0, (int)MathF.Floor(bx - r)); x <= Math.Min(_stage.Width - 1, (int)MathF.Floor(bx + r)); x++)
            for (var z = Math.Max(0, (int)MathF.Floor(bz - r)); z <= Math.Min(_stage.Height - 1, (int)MathF.Floor(bz + r)); z++)
                if (_softBlocks.IsAlive(x, z) && InBlast(bx, bz, y, r, x + .5f, z + .5f, _stage.SurfaceAt(x + .5f, z + .5f), red))
                    hits.Add((x, z));
        if(red && hits.Count>0 && _tutorialLesson?.Id=="red")_tutorialSignals.Add("red-cover");
        foreach (var (x, z) in hits)
        {
            _softBlocks.Set(x, z, false);
            DropCoverItem(context, x, z);
        }
    }

    private bool InBlast(float bx, float bz, float by, float radius, float x, float z, float y, bool red) =>
        BlastGeometry.Contains(_stage, _softBlocks, bx, by, bz, radius, x, y, z, red);

    // ─── hazards ──────────────────────────────────────────────────────────────

    private void TickHazards(ISessionContext context, float dt)
    {
        for (var i = _hazards.Count - 1; i >= 0; i--)
        {
            _hazards[i].LifeTimer -= dt;
            if (_hazards[i].LifeTimer <= 0)
                _hazards.RemoveAt(i);
        }
    }
}

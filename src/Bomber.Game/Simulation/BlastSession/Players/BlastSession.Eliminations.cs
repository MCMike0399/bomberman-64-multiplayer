// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Burbuja.Realtime.Abstractions;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.State;

namespace Bomber.Game.Simulation;

/// <summary>Hits, eliminations, practice respawns and ghost returns.</summary>
public sealed partial class BlastSession
{
    // ─── player hits ──────────────────────────────────────────────────────────

    private void HitPlayer(ISessionContext context, BlastPlayer p, string reason = "explosion", string? causedBy = null)
    {
        if (!p.Alive || p.Ghost || p.InvincibleTimer > 0) return;

        if (p.HasHeart)
        {
            p.HasHeart = false;
            if(_tutorialLesson is not null && p.Slot==0)_tutorialSignals.Add("heart-hit");
            p.InvincibleTimer = 2.0f;
            return;
        }

        Eliminate(causedBy, p, reason);
    }

    private void Eliminate(string? causedBy, BlastPlayer p, string reason)
    {
        if (!p.Alive) return;
        Present("death", p.Id, p.Id, p.X, p.Y, p.Z, reason: reason, causedBy: causedBy);
        if (_practice && _tutorialLesson?.Id!="ghost") { RespawnPracticePlayer(p); Present("respawn", p.Id, p.Id, p.X, p.Y, p.Z, reason: reason); return; }
        p.Alive = false;
        p.StatusEffect="none";p.StatusRemaining=0;
        DropCarriedBombs(p);

        // Drop one collected item power-up (designer choice).
        DropPowerUpOnDeath(p);

        // Detach any ghost latched to this player.
        foreach (var g in _players.Values)
            if (g.Ghost && g.LatchedToId == p.Id)
                DetachGhost(g);

        if (!_ghostsEnabled || p.Ghost || _phase == RoundPhase.Hurry)
            return; // sudden death never creates a new interactive ghost

        // Become a ghost.
        p.Ghost = true;
        p.Alive = true;
        ClearRecovery(p);
        p.InvincibleTimer = 2.0f;
        // Lose carried power-ups as ghost.
        p.BombType="normal";p.Ability="none";
        p.BombCapacity = 1; p.FireLevel = 1;
        if(reason is "water" or "fall") ReturnGhostToLand(p);
    }

    private void ReturnGhostToLand(BlastPlayer p)
    {
        var cells=Enumerable.Range(0,_stage.Width).SelectMany(x=>Enumerable.Range(0,_stage.Height).Select(z=>(X:x,Z:z)))
            .Where(c=>_stage.At(c.X,c.Z).IsPassable&&!_softBlocks.IsAlive(c.X,c.Z)&&
                !WaterUnsafe(c.X+.5f,c.Z+.5f))
            .OrderBy(c=>Dist2(p.X,p.Z,c.X+.5f,c.Z+.5f)).ToArray();
        if(cells.Length==0){p.Alive=false;return;}
        var cell=cells[0];p.X=cell.X+.5f;p.Z=cell.Z+.5f;
        p.SurfaceY=_stage.SurfaceAt(p.X,p.Z);p.Tier=(int)MathF.Round(p.SurfaceY);
        ClearKick(p);p.PrimaryBombCandidateId=null;p.PrimaryHoldTimer=0;
        Present("respawn",p.Id,p.Id,p.X,p.Y,p.Z,reason:"ghost");
    }

    /// <summary>Local training recovers through the ordinary elimination path. Online sessions never
    /// enable practice, and no client message can request a respawn or change authoritative position.</summary>
    private void RespawnPracticePlayer(BlastPlayer player)
    {
        DropCarriedBombs(player);
        foreach (var other in _players.Values)
        {
            if (other.CarryingPlayerId == player.Id) other.CarryingPlayerId = null;
            if (other.LatchedToId == player.Id) DetachGhost(other);
        }
        DetachIfLatched(player);
        var preferred = player.Slot % _stage.Spawns.Length;
        var candidates = Enumerable.Range(0, _stage.Spawns.Length)
            .Select(i => _stage.Spawns[(preferred + i) % _stage.Spawns.Length])
            .Where(s => _stage.At(s.X, s.Z).IsPassable && !_softBlocks.IsAlive(s.X, s.Z)).ToArray();
        var spawn = candidates.FirstOrDefault(s =>
            !_bombs.Any(b => !b.Exploded && InBlast(b.X,b.Z,BombY(b),b.BlastRadius + PlayerRadius,
                s.X+.5f,s.Z+.5f,_stage.SurfaceAt(s.X+.5f,s.Z+.5f),b.IsRed)) &&
            !_hazards.Any(h => InBlast(h.X,h.Z,h.Y,h.Radius + PlayerRadius,
                s.X+.5f,s.Z+.5f,_stage.SurfaceAt(s.X+.5f,s.Z+.5f),h.IsRed)),
            candidates.Length > 0 ? candidates[0] : _stage.Spawns[preferred]);
        player.X = spawn.X + .5f; player.Z = spawn.Z + .5f;
        player.SurfaceY = _stage.SurfaceAt(player.X,player.Z); player.Tier = (int)MathF.Round(player.SurfaceY);
        player.Alive = true; player.Ghost = false; player.PendingRoundStart = false;
        ClearRecovery(player); player.InvincibleTimer = 1.5f;
        player.CarryFromPrimary=false;player.PrimaryBombCandidateId=null;player.PrimaryHoldTimer=0;
        player.ClearInput(); player.PrevA = player.PrevB = player.PrevR = player.PrevStop = false;
        PowerUpRules.Reset(player,_advanced);
        player.ActiveBombs = _bombs.Count(b => b.OwnerId == player.Id && !b.Exploded);
        player.FacingX = 0; player.FacingZ = 1; player.BotThinkTimer = 0;
    }

    private void DropPowerUpOnDeath(BlastPlayer p)
    {
        string? type = null;
        if(p.Ability!="none")type=p.Ability;
        else if(p.BombType!="normal")type=p.BombType;
        else if (p.FireLevel > 1) type = "fire";
        else if (p.BombCapacity > PowerUpRules.StartingBombs) type = "bomb";

        if (type is null || !_advanced.Allows(type)) return;
        _items.Add(new BlastItem { Id = NextItemId(), CellX = (int)p.X, CellZ = (int)p.Z, Tier = p.Tier, Type = type, LifeRemaining=_advanced.ItemSeconds });
    }
}

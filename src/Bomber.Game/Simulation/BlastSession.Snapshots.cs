// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Burbuja.Realtime.Abstractions;
using System.Text.Json.Nodes;

namespace Bomber.Game.Simulation;

/// <summary>Stage, state and scoreboard messages sent to clients.</summary>
public sealed partial class BlastSession
{
    // ─── stage message ────────────────────────────────────────────────────────

    private void SendStage(ISessionContext context, string playerId)
    {
        var w = _stage.Width;
        var h = _stage.Height;
        var flat = new object[w * h];
        object Cell(CellDef c) => new
        {
            k = (int)c.Kind, t = c.Tier, oh = c.ObstacleHeight, rx = c.RampDX, rz = c.RampDZ,
            rf = c.RampFromTier, rt = c.RampToTier,
            below = c.Below is { } lower ? Cell(lower) : null,
        };
        for (int z = 0; z < h; z++) for (int x = 0; x < w; x++)
            flat[z*w+x] = Cell(_stage.At(x,z));

        context.Send(playerId, "stage", new
        {
            id = _stage.Id,
            name = _stage.Name,
            width = w,
            height = h,
            coverHeight = _stage.SoftBlockHeight,
            cells = flat,
            underpasses = _stage.Underpasses.Select(c => c.Z * w + c.X).Order().ToArray(),
            spawns = _stage.Spawns.Select(s => new[] { s.X, s.Z }),
            round = _roundNum,
            winsTarget = _winsTarget,
            roundSeconds = _roundSeconds,
        });
    }

    // ─── state broadcast ──────────────────────────────────────────────────────

    private void BroadcastState(ISessionContext context)
    {
        var playerList = _players.Values.Select(p => new
        {
            slot = p.Slot,
            appearance = p.Appearance,
            id = p.Id,
            name = p.DisplayName,
            bot = p.IsBot,
            x = MathF.Round(p.X, 2),
            y = MathF.Round(p.Y, 3),
            z = MathF.Round(p.Z, 2),
            fx = MathF.Round(p.FacingX, 2),
            fz = MathF.Round(p.FacingZ, 2),
            alive = p.Alive,
            ghost = p.Ghost,
            latchedTo=p.LatchedToId,
            carryFromPrimary=p.CarryFromPrimary,
            aiming=p.Aiming,aimSet=p.AimSet,aimX=p.AimX,aimZ=p.AimZ,aimRange=p.AimRange,
            primaryHoldProgress=p.PrimaryBombCandidateId is null?0:Math.Clamp(p.PrimaryHoldTimer/.25f,0,1),
            kickWindup=MathF.Round(p.KickWindupRemaining,3),
            kickRecovery=MathF.Round(p.KickRecoveryRemaining,3),
            stunned = p.StunTimer > 0,
            knockdownReason=p.KnockdownReason,
            recoveryRemaining=MathF.Round(p.CarriedById is not null?p.HoldRemaining:p.StunTimer,2),
            recoveryTotal=p.RecoveryTotal,carriedBy=p.CarriedById,carryingPlayerId=p.CarryingPlayerId,airborne=p.Airborne,falling=p.Falling,
            carrying = p.CarryingBombId is not null ? "bomb" : p.CarryingPlayerId is not null ? "player" : null,
            heart = p.HasHeart,
            fire = p.FireLevel,
            bombs = p.BombCapacity,
            activeBombs = p.ActiveBombs,
            pickupSerial = p.PickupSerial,
            lastPickup = p.LastPickup,
            pumpSerial = p.PumpSerial,
            bombType = p.BombType, ability = p.Ability,
            statusEffect=p.StatusEffect,statusRemaining=p.StatusRemaining,statusScale=p.StatusScale,maxFire=p.MaxFire,maxBombs=p.MaxBombs,
            red = p.HasRed,
            remote = p.HasRemote,
            wins = p.Wins,
            ack = context.LastSequence(p.Id),
        });

        var bombList = _bombs.Where(b => !b.Exploded).Select(b => new
        {
            id = b.Id,
            ownerId = b.OwnerId,
            ownerCanExit=b.OwnerMayLeave,
            carrierId = b.State == BombState.Carried ? _players.Values.FirstOrDefault(p=>p.CarryingBombId==b.Id)?.Id : null,
            effectiveLevel = b.EffectiveLevel,
            x = MathF.Round(b.X, 2),
            y = MathF.Round(BombY(b), 3),
            z = MathF.Round(b.Z, 2),
            level = b.FireLevel,
            pumps = b.Pumps,
            // Keep the existing peer-frame field; bomb travel always uses ordinary time.
            mode = b.Mode, bounces = b.Bounces, motionRate = 1f, gravityScale=b.GravityScale,
            charge = b.ChargeDuration>0?Math.Clamp(1-b.ChargeRemaining/b.ChargeDuration,0,1):0, charging = b.RequestedPumps>0,
            red = b.IsRed,
            remote = b.IsRemote,
            fuse = MathF.Round(b.FuseTimer, 2),
            state = (int)b.State,
        });

        var hazardList = _hazards.Select(h => new
        {
            id = h.Id,
            x = MathF.Round(h.X, 2),
            z = MathF.Round(h.Z, 2),
            y = MathF.Round(h.Y, 3),
            r = MathF.Round(h.Radius, 2),
            age = Math.Clamp(.4f - h.LifeTimer, 0, .4f),
            life = Math.Max(0, h.LifeTimer),
            red = h.IsRed,
        });

        var itemList = _items.Select(it => new
        {
            id = it.Id,
            cx = it.CellX,
            cz = it.CellZ,
            type = it.Type, life = it.LifeRemaining, dropped = it.DroppedBy!=null,
            y = _stage.SurfaceAt(it.X, it.Z,it.Tier),
        });

        var timeLeft = Math.Max(0, _roundSeconds - _roundTimer);

        context.Broadcast("state", new
        {
            tutorial = TutorialSnapshot(),
            round = _roundNum,
            serverTime = context.Now.ToUnixTimeMilliseconds(),
            stageLighting = StageLightings[(_lightingOffset+Math.Max(0,_roundNum-1))%4],
            presentationSeq = _presentationSeq,
            events = SnapshotPresentationEvents(),
            phase = (int)_phase,
            timeLeft = MathF.Round(timeLeft, 1),
            closedLeft = _closedLeft,
            closedRight = _closedRight,
            blizzard = _stage.Id==BlastStages.BlizzardBattle ? new {time=_weatherTime,seed=_weatherSeed,round=_roundNum,x=BlizzardWind.X,z=BlizzardWind.Y} : null,
            hurryMode = _tutorialLesson?.Id=="flood"?"water": !_suddenDeath || _practice ? "none" : _stage.RisingWater ? "water" : _stage.Id==BlastStages.RockGarden?"meteors":_stage.Id==BlastStages.BlizzardBattle?"avalanche":"columns",
            waterLevel = _waterLevel,
            waterStartsIn = _tutorialLesson?.Id=="flood" ? (_tutorialStarted?0:Math.Max(0,_phaseTimer)) : !_practice && _suddenDeath && _stage.RisingWater ? Math.Max(0,_roundSeconds-60+FloodRules.RiseDelay-_roundTimer) : -1,
            waterRiseRate = _tutorialLesson?.Id=="flood" ? (_tutorialStarted?FloodRules.RisePerSecond:0) : !_practice && _suddenDeath && _stage.RisingWater && _roundTimer >= _roundSeconds-60+FloodRules.RiseDelay ? FloodRules.RisePerSecond : 0,
            hurryStartsIn = _tutorialLesson?.Id=="flood" ? (_tutorialStarted?0:Math.Max(0,_phaseTimer)) : _practice || !_suddenDeath ? -1 : Math.Max(0,_roundSeconds - 60 - _roundTimer),
            players = playerList,
            bombs = bombList,
            hazards = hazardList,
            meteors = _meteors.Where(_=>_phase==RoundPhase.Hurry).Select(m=>new {id=m.Id,x=m.X,z=m.Z,remaining=MathF.Round(m.Remaining,3)}),
            items = itemList,
            softBlocks = _softBlocks.ToBase64(),
        });
    }

    // ─── scoreboard ───────────────────────────────────────────────────────────

    private JsonObject BuildScoreboard(string reason)
    {
        var rows = new JsonArray(_players.Values.OrderByDescending(p => p.Wins)
            .Select(p => (JsonNode)new JsonObject
            {
                ["id"] = p.Id,
                ["slot"] = p.Slot,
                ["appearance"] = p.Appearance,
                ["name"] = p.DisplayName,
                ["wins"] = p.Wins,
                ["bot"] = p.IsBot,
            }).ToArray());
        return new JsonObject { ["reason"] = reason, ["scoreboard"] = rows };
    }
}

// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Burbuja.Realtime.Abstractions;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

namespace Bomber.Game.Simulation;

/// <summary>Round lifecycle and stage-owned sudden-death hazards.</summary>
public sealed partial class BlastSession
{
    // ─── round lifecycle ─────────────────────────────────────────────────────

    private void StartRound(ISessionContext context, int roundNum)
    {
        _roundNum = roundNum;
        _phase = RoundPhase.Ready;
        _phaseTimer = 2f;
        _roundTimer = 0;
        _fightTicks = 0;
        _weatherTime=0;
        _waterLevel = -1;
        _waterfallStarted = false;
        _closedLeft = 0;
        _closedRight = 0;
        _hurryColTimer = 0;
        _meteorTimer=0;_meteorSequence=0;_meteors.Clear();

        _bombs.Clear();
        _presentedKickContacts.Clear();
        _hazards.Clear();
        _items.Clear();
        foreach(var item in _stage.InitialItems.Concat(_practice&&_tutorialLesson is null ? PracticeItems(context.Random) : []).Where(item=>_advanced.Allows(item.Type)))
            _items.Add(new BlastItem { Id=NextItemId(),CellX=item.X,CellZ=item.Z,Tier=_stage.At(item.X,item.Z).Tier,Type=item.Type,LifeRemaining=_advanced.ItemSeconds });

        // Restore pending joiners and reset living players.
        foreach (var p in _players.Values)
        {
            p.PendingRoundStart = false;
            p.Ghost = false;
            p.Alive = true;
            ClearRecovery(p);ClearKick(p);
            p.InvincibleTimer = 0;
            p.CarryingBombId = null;
            ClearThrowAim(p);
            p.CarryFromPrimary=false;p.PrimaryBombCandidateId=null;p.PrimaryHoldTimer=0;
            p.CarryingPlayerId = null;
            p.LatchedToId = null;
            p.LatchTimer = 0;
            p.LatchReverseCount = 0;
            p.ActiveBombs = 0;
            PowerUpRules.Reset(p,_advanced);
            p.ClearInput();
            p.PrevA = false;
            p.PrevB = false;
            p.PrevR = false;p.PrevStop=false;
        }

        // Assign spawn positions.
        var spawns = _stage.Spawns;
        var idx = 0;
        foreach (var p in _players.Values.OrderBy(p => p.Slot))
        {
            var sp = spawns[idx % spawns.Length];
            p.X = sp.X + 0.5f;
            p.Z = sp.Z + 0.5f;
            p.SurfaceY = _stage.SurfaceAt(p.X, p.Z);
            p.Tier = (int)MathF.Round(p.SurfaceY);
            p.FacingX = 0;
            p.FacingZ = 1;
            p.FacingReleaseHold = 0;
            idx++;
        }

        // Generate soft blocks (deterministic from match seed + round number).
        PlaceSoftBlocks(context.Random);
        PrepareTutorial();
        BotReset();
        Present("round-ready");

        // Tell all connected players about the round reset via stage update.
        foreach (var player in context.Players)
            SendStage(context, player.PlayerId);
    }

    private void EnterFight(ISessionContext context)
    {
        _phase = RoundPhase.Fight;
        _roundTimer = 0;
        Present("fight");
        BeginTutorialFight();
    }

    private void SimulateRound(ISessionContext context, float dt)
    {
        if (!_practice || _tutorialLesson?.Id=="duel") _roundTimer = ++_fightTicks * dt;
        if(_stage.Id==BlastStages.BlizzardBattle)_weatherTime+=dt;
        var timeLeft = _roundSeconds - _roundTimer;

        // Trigger hurry.
        if (!_practice && _suddenDeath && _phase == RoundPhase.Fight && timeLeft <= 60f)
        {
            _phase = RoundPhase.Hurry;
            Present("hurry");
            _hurryColTimer = 0;
            // Remove all ghosts when hurry starts.
            foreach (var p in _players.Values)
                if (p.Ghost && !p.PendingRoundStart)
                {
                    p.Alive = false;
                    p.Ghost = false;
                    DropCarriedBombs(p);
                    DetachIfLatched(p);
                }
        }

        // Temple flooding replaces the legacy maps' closing-column hazard.
        if (_phase == RoundPhase.Hurry && _stage.RisingWater)
        {
            _waterLevel = WaterLevelAfter(0);
            if (!_waterfallStarted && _roundTimer >= _roundSeconds - 60 + FloodRules.RiseDelay)
            {
                _waterfallStarted = true;
                Present("waterfall", magnitude: FloodRules.RisePerSecond, y: _waterLevel);
            }
        }
        if (_phase == RoundPhase.Hurry && !_stage.RisingWater && _stage.Id!=BlastStages.RockGarden && _tutorialLesson is null)
        {
            _hurryColTimer += dt;
            float interval=BlastStages.HurryColumnSeconds(_stage.Id);
            if (_hurryColTimer >= interval)
            {
                _hurryColTimer -= interval;
                var leftBound = _closedLeft;
                var rightBound = _stage.Width - 1 - _closedRight;
                if (leftBound < rightBound)
                {
                    _closedLeft++;
                    _closedRight++;
                    // Kill anything in the newly closed columns.
                    KillInClosedColumns(context);
                }
            }
        }

        if(_phase==RoundPhase.Hurry&&_stage.Id==BlastStages.RockGarden)TickGardenMeteors(context,dt);

        // Time limit: draw.
        if (!_practice && timeLeft <= 0)
        {
            EndRound(context, null, "time_up");
            return;
        }

        foreach(var player in _players.Values)
        {
            player.StatusRemaining=Math.Max(0,player.StatusRemaining-dt);
            if(player.StatusRemaining==0 || !player.Alive || player.Ghost){player.StatusEffect="none";player.StatusRemaining=0;}
        }
        // Tick bots.
        foreach (var p in _players.Values)
            if (p.IsBot && p.Alive && !p.Ghost)
                BotThink(p, dt);

        // Process inputs and apply physics.
        BeginBodyContacts();
        ProcessInputs(context, dt);
        DriftBlizzardPlayers(dt);
        TickPlayerCarrying(dt);
        ResolveBodyContacts(dt);
        SynchronizeLatchedGhosts();
        DrownSubmergedEntities();
        if(_phase==RoundPhase.Hurry&&_stage.Id==BlastStages.BlizzardBattle)KillInClosedColumns(context);
        TickBombs(context, dt);
        TickHazards(context, dt);
        TickItems(context);

        TickTutorial(dt);

        // Check win condition.
        var living = _players.Values.Count(p => p.Alive && !p.Ghost);
        if (!_practice && living <= 1)
        {
            var winner = _players.Values.FirstOrDefault(p => p.Alive && !p.Ghost);
            EndRound(context, winner?.Id, living == 0 ? "no_survivors" : "last_standing");
        }
    }

    private float WaterLevelAfter(float seconds) => _tutorialLesson?.Id=="flood" ? _waterLevel+Math.Max(0,seconds)*FloodRules.RisePerSecond : !_practice && _suddenDeath && _stage.RisingWater
        ? FloodRules.Level(_roundTimer + Math.Max(0, seconds),_roundSeconds) : -1;
    private bool WaterUnsafe(float x,float z,float seconds=0,float y=float.PositiveInfinity) =>
        _stage.AtHeight((int)MathF.Floor(x),(int)MathF.Floor(z),y).IsElimination ||
        FloodRules.Submerged(_stage.SurfaceAt(x,z,y),WaterLevelAfter(seconds));

    private void DrownSubmergedEntities()
    {
        if (_phase != RoundPhase.Hurry || (!_stage.RisingWater && _tutorialLesson?.Id!="flood")) return;
        foreach (var player in _players.Values)
            if (player.Alive && !player.PendingRoundStart && FloodRules.Submerged(player.Y,_waterLevel))
            { DropCarriedBombs(player); Eliminate(null,player,"water"); }
        // Submerged bombs fizzle instead of detonating; water is lethal independently of hearts.
        foreach (var bomb in _bombs)
            if (!bomb.Exploded && bomb.State != BombState.Carried && FloodRules.Submerged(BombY(bomb),_waterLevel))
            { bomb.Exploded=true; DecrementOwnerBombs(bomb.OwnerId); }
        _items.RemoveAll(item=>FloodRules.Submerged(_stage.SurfaceAt(item.X,item.Z,item.Tier),_waterLevel));
    }

    private void EndRound(ISessionContext context, string? winnerId, string reason)
    {
        _phase = RoundPhase.Over;
        _phaseTimer = 3f;
        Present("round-over", playerId: winnerId, reason: reason);

        if (winnerId is not null && _players.TryGetValue(winnerId, out var winner))
            winner.Wins++;

        // Check match win.
        if (winnerId is not null && _players.TryGetValue(winnerId, out var w) && w.Wins >= _winsTarget)
        {
            var details = BuildScoreboard(reason);
            // Capture the accepted outcome now, but keep the final round's frozen
            // arena visible for the same announcement interval as every other round.
            _pendingMatchResult = w.IsBot
                ? new MatchResult("bot_won", [], details)
                : new MatchResult("won", [winnerId], details);
        }
        BroadcastState(context); // Deliver the finish and winning point immediately, even on an odd tick.
    }

    private void AdvanceRound(ISessionContext context)
    {
        if (_pendingMatchResult is {} result)
        {
            _matchEndScheduled = true;
            BroadcastState(context);
            context.EndMatch(result);
            return;
        }
        StartRound(context, _roundNum + 1);
    }

    // ─── hurry: column closure ────────────────────────────────────────────────

    private void KillInClosedColumns(ISessionContext context)
    {
        foreach (var p in _players.Values)
        {
            if (!p.Alive || p.Ghost) continue;
            if ((int)p.X < _closedLeft || (int)p.X >= _stage.Width - _closedRight)
                {if(_stage.Id==BlastStages.BlizzardBattle)Eliminate(null,p,"avalanche");else HitPlayer(context, p, "columns");}
        }
        // Also explode resting bombs in closed columns.
        foreach (var b in _bombs.ToList())
        {
            if (b.State != BombState.Resting) continue;
            if (b.CellX < _closedLeft || b.CellX >= _stage.Width - _closedRight)
                ExplodeBomb(context, b);
        }
    }
}

// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Burbuja.Realtime.Abstractions;

namespace Bomber.Game.Simulation;

/// <summary>
/// Server-authoritative Blast session. Runs at 30 Hz; broadcasts snapshots at 15 Hz (every other tick).
///
/// Round flow: Ready (2 s frozen) → Fight → optional Hurry (sudden death at 60 s left) → Over (3 s).
/// The first player to reach the configured <c>wins</c> count ends the match. The session ends the match
/// itself via <see cref="ISessionContext.EndMatch"/>; the kernel never hits the MaxMatchDuration for a
/// normal game.
///
/// Threading contract: plain fields, no locks, no blocking. An exception here ends the lobby.
/// </summary>
public sealed partial class BlastSession : GameSession
{
    // ─── settings (validated and clamped from lobby options) ─────────────────

    private readonly int[] _localAppearances;
    private readonly bool _practice;
    private readonly int _winsTarget;
    private readonly int _roundSeconds;
    private readonly bool _suddenDeath;
    private readonly bool _ghostsEnabled;
    private readonly int _botCount;

    /// <summary>CPU players fill the arena, so a lobby with bots starts with one human, as in the original.</summary>
    public override int? MinPlayersToStart => _botCount > 0 ? 1 : null;
    private readonly StageLayout _stage;

    // ─── round state ─────────────────────────────────────────────────────────

    private RoundPhase _phase;
    private int _roundNum;
    private float _phaseTimer;     // seconds until phase transition
    private long _fightTicks;
    private float _waterLevel = -1;
    private float _roundTimer;     // time elapsed in current fight/hurry phase
    private int _closedLeft;       // hurry: columns closed from the left (x < _closedLeft = dead zone)
    private int _closedRight;      // hurry: columns closed from the right (x >= Width-_closedRight = dead zone)
    private float _hurryColTimer;  // seconds until next column closure

    // ─── entities ────────────────────────────────────────────────────────────

    private readonly Dictionary<string, BlastPlayer> _players = new(StringComparer.Ordinal);
    private readonly List<BlastBomb> _bombs = [];
    private readonly List<BlastHazard> _hazards = [];
    private readonly List<BlastItem> _items = [];
    private SoftBlockGrid _softBlocks = null!;
    private int _tickCount;

    // ─── match-end guard ─────────────────────────────────────────────────────

    private bool _matchEndScheduled;
    private bool _waterfallStarted;

    // Per-session ID counters — deterministic given the same sequence of events.
    private int _nextBombId;
    private int _nextHazardId;
    private int _nextItemId;

    private string NextBombId()   => $"b{++_nextBombId}";
    private string NextHazardId() => $"h{++_nextHazardId}";
    private string NextItemId()   => $"i{++_nextItemId}";

    public BlastSession(GameSessionOptions options, StageLayout? arena = null, bool practice = false, IReadOnlyList<int>? appearances = null)
    {
        _localAppearances = appearances?.ToArray() ?? [];
        _practice = practice;
        _stage = arena ?? BlastStages.Get(options.Settings.TryGetValue("stage", out var s) ? s : "sunken-temple");

        _winsTarget = TryInt(options, "wins", 2, 1, 5);
        _roundSeconds = TryInt(options, "round-seconds", 120, 60, 300);
        _suddenDeath = TryBool(options, "sudden-death", true);
        _ghostsEnabled = TryBool(options, "ghosts", true);
        _botCount = Math.Clamp(TryInt(options, "bots", 0, 0, 7), 0, 7);
        BotInitialize(options);
    }

    // ─── IGameSession callbacks ───────────────────────────────────────────────

    public override void OnPlayerJoined(ISessionContext context, RealtimePlayer player)
    {
        // Bots fill slots outside the kernel's human-seat table. A late human owns
        // their kernel slot and replaces its bot, keeping the eight-player limit.
        foreach (var bot in _players.Values.Where(p => p.IsBot && p.Slot == player.Slot).ToArray())
            OnPlayerLeft(context, new RealtimePlayer(bot.Id, bot.DisplayName, bot.Slot, new Dictionary<string, string>()), LeaveReason.Left);
        var p = new BlastPlayer
        {
            Id = player.PlayerId,
            Slot = player.Slot,
            Appearance = DefaultAppearance(player.Slot),
            DisplayName = PlayerNames.Display(player.DisplayName,player.Slot),
        };

        // A player who joins mid-match waits for the next round as a ghost spectator.
        if (context.State == LobbyState.Running)
        {
            p.Ghost = true;
            p.PendingRoundStart = true;
            p.Alive = false;
        }

        _players[player.PlayerId] = p;
        SendStage(context, player.PlayerId);
        SendAppearances(context,player.PlayerId);
    }

    public override void OnPlayerLeft(ISessionContext context, RealtimePlayer player, LeaveReason reason)
    {
        if (_players.TryGetValue(player.PlayerId, out var p))
        {
            DropCarriedBombs(p);
            if (p.LatchedToId is not null)
                DetachGhost(p);
        }
        _players.Remove(player.PlayerId);
        _bombs.RemoveAll(b => b.OwnerId == player.PlayerId && b.State == BombState.Carried);
    }

    public override void OnPlayerConnectionChanged(ISessionContext context, RealtimePlayer player, bool connected)
    {
        if (!connected && _players.TryGetValue(player.PlayerId, out var p))
        {
            DropCarriedBombs(p); // a lost connection safely releases a hold instead of launching its victim
            p.ClearInput();p.PrevB=false;
        }
        if (connected)
        {
            SendStage(context, player.PlayerId);
            SendAppearances(context,player.PlayerId);
        }
    }

    private void SendAppearances(ISessionContext context,string recipient)
    {
        foreach(var player in _players.Values.OrderBy(p=>p.Slot))
            context.Send(recipient,"appearance",new {playerId=player.Id,index=player.Appearance});
    }

    public override void OnMatchStarted(ISessionContext context)
    {
        // Add CPU bots into the player table.
        for (var i = 0; i < _botCount && _players.Count < 8; i++)
        {
            var slot = FindFreeSlot();
            var botId = $"bot-{i}";
            _players[botId] = new BlastPlayer
            {
                Id = botId, Slot = slot, IsBot = true, Appearance = DefaultAppearance(slot),
                DisplayName = $"Bot {i + 1}",
            };
        }

        StartRound(context, 1);
    }

    private int DefaultAppearance(int slot) => slot>=0&&slot<_localAppearances.Length ? _localAppearances[slot] : Math.Clamp(slot,0,7);

    public override void OnMessage(ISessionContext context, RealtimePlayer player, InboundMessage message)
    {
        if (!_players.TryGetValue(player.PlayerId, out var p))
            return;

        if (message.Type == "appearance")
        {
            // Only the authenticated sender is writable. Client-supplied slot/playerId fields have
            // no authority; invalid/missing/non-integer selections never alter the current choice.
            if(message.Format==FrameFormat.Text && message.Data.ValueKind==System.Text.Json.JsonValueKind.Object &&
                message.Data.TryGetProperty("index",out var value) && value.ValueKind==System.Text.Json.JsonValueKind.Number &&
                value.TryGetInt32(out var index) && index is >=0 and <=7)
            {
                p.Appearance=index;
                context.Broadcast("appearance",new {playerId=p.Id,index});
            }
            return;
        }

        if (message.Type == "stage")
        {
            SendStage(context, player.PlayerId);
            return;
        }

        if (message.Type == "input" && message.TryRead(out InputMsg? raw) && raw is not null)
        {
            // Clamp and normalise horizontal intent.
            var x = Finite(raw.X);
            var z = Finite(raw.Z);
            var len = MathF.Sqrt(x * x + z * z);
            if (len > 1f) { x /= len; z /= len; }
            p.ReceiveInput(new InputState(x, z, raw.A, raw.B, raw.R, raw.Aim, raw.Stop));
        }
    }

    public override void OnTick(ISessionContext context)
    {
        if (_matchEndScheduled)
            return;

        foreach(var player in _players.Values)if(!player.IsBot)player.AdvanceReceivedInput();
        _tickCount++;
        var dt = (float)context.TickDelta.TotalSeconds;

        switch (_phase)
        {
            case RoundPhase.Ready:
                _phaseTimer -= dt;
                if (_phaseTimer <= 0)
                    EnterFight(context);
                break;

            case RoundPhase.Fight:
            case RoundPhase.Hurry:
                SimulateRound(context, dt);
                break;

            case RoundPhase.Over:
                _phaseTimer -= dt;
                if (_phaseTimer <= 0)
                    AdvanceRound(context);
                break;
        }

        // Broadcast at 15 Hz (every other tick).
        if (_tickCount % 2 == 0)
            BroadcastState(context);
    }

    public override void OnMatchEnded(ISessionContext context, MatchResult result) { }

    // ─── round lifecycle ─────────────────────────────────────────────────────

    private void StartRound(ISessionContext context, int roundNum)
    {
        _roundNum = roundNum;
        _phase = RoundPhase.Ready;
        _phaseTimer = 2f;
        _roundTimer = 0;
        _fightTicks = 0;
        _waterLevel = -1;
        _waterfallStarted = false;
        _closedLeft = 0;
        _closedRight = 0;
        _hurryColTimer = 0;

        _bombs.Clear();
        _presentedKickContacts.Clear();
        _hazards.Clear();
        _items.Clear();
        foreach(var item in _stage.InitialItems.Concat(_practice ? _stage.TrainingItems : []))
            _items.Add(new BlastItem { Id=NextItemId(),CellX=item.X,CellZ=item.Z,Tier=_stage.At(item.X,item.Z).Tier,Type=item.Type,LifeRemaining=-1 });

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
            PowerUpRules.Reset(p);
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
            idx++;
        }

        // Generate soft blocks (deterministic from match seed + round number).
        PlaceSoftBlocks(context.Random);
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
    }

    private void SimulateRound(ISessionContext context, float dt)
    {
        if (!_practice) _roundTimer = ++_fightTicks * dt;
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
        if (_phase == RoundPhase.Hurry && _stage.Id == BlastStages.SunkenTemple)
        {
            _waterLevel = WaterLevelAfter(0);
            if (!_waterfallStarted && _roundTimer >= _roundSeconds - 60 + FloodRules.RiseDelay)
            {
                _waterfallStarted = true;
                Present("waterfall", magnitude: FloodRules.RisePerSecond, y: _waterLevel);
            }
        }
        if (_phase == RoundPhase.Hurry && _stage.Id != BlastStages.SunkenTemple)
        {
            _hurryColTimer += dt;
            if (_hurryColTimer >= 1f)
            {
                _hurryColTimer -= 1f;
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

        // Time limit: draw.
        if (!_practice && timeLeft <= 0)
        {
            EndRound(context, null, "time_up");
            return;
        }

        // Tick bots.
        foreach (var p in _players.Values)
            if (p.IsBot && p.Alive && !p.Ghost)
                BotThink(p, dt);

        // Process inputs and apply physics.
        BeginBodyContacts();
        ProcessInputs(context, dt);
        TickPlayerCarrying(dt);
        ResolveBodyContacts(dt);
        SynchronizeLatchedGhosts();
        DrownSubmergedEntities();
        TickBombs(context, dt);
        TickHazards(context, dt);
        TickItems(context);

        // Check win condition.
        var living = _players.Values.Count(p => p.Alive && !p.Ghost);
        if (!_practice && living <= 1)
        {
            var winner = _players.Values.FirstOrDefault(p => p.Alive && !p.Ghost);
            EndRound(context, winner?.Id, living == 0 ? "no_survivors" : "last_standing");
        }
    }

    private float WaterLevelAfter(float seconds) => !_practice && _suddenDeath && _stage.Id == BlastStages.SunkenTemple
        ? FloodRules.Level(_roundTimer + Math.Max(0, seconds),_roundSeconds) : -1;
    private bool WaterUnsafe(float x,float z,float seconds=0) =>
        _stage.At((int)MathF.Floor(x),(int)MathF.Floor(z)).IsElimination ||
        FloodRules.Submerged(_stage.SurfaceAt(x,z),WaterLevelAfter(seconds));

    private void DrownSubmergedEntities()
    {
        if (_phase != RoundPhase.Hurry || _stage.Id != BlastStages.SunkenTemple) return;
        foreach (var player in _players.Values)
            if (player.Alive && !player.PendingRoundStart && FloodRules.Submerged(player.Y,_waterLevel))
            { DropCarriedBombs(player); Eliminate(null,player,"water"); }
        // Submerged bombs fizzle instead of detonating; water is lethal independently of hearts.
        foreach (var bomb in _bombs)
            if (!bomb.Exploded && bomb.State != BombState.Carried && FloodRules.Submerged(BombY(bomb),_waterLevel))
            { bomb.Exploded=true; DecrementOwnerBombs(bomb.OwnerId); }
        _items.RemoveAll(item=>FloodRules.Submerged(_stage.SurfaceAt(item.X,item.Z),_waterLevel));
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
            _matchEndScheduled = true;
            var details = BuildScoreboard(reason);
            BroadcastState(context); // Flush the final accepted actions before the match-end envelope.
            if (w.IsBot)
                context.EndMatch(new MatchResult("bot_won", [], details));
            else
                context.EndMatch(new MatchResult("won", [winnerId], details));
        }
    }

    private void AdvanceRound(ISessionContext context)
    {
        if (_matchEndScheduled)
            return;
        StartRound(context, _roundNum + 1);
    }

    // ─── input processing ─────────────────────────────────────────────────────

    private void ProcessInputs(ISessionContext context, float dt)
    {
        // Read each victim's own intent before applying the ghost's controls. Do
        // this before the player loop so dictionary order cannot change shake-off.
        foreach (var ghost in _players.Values)
            if (ghost.Alive && ghost.Ghost && ghost.LatchedToId is { } victimId)
            {
                if (_players.TryGetValue(victimId, out var victim) && victim.Alive && !victim.Ghost)
                    TrackLatch(ghost, victim, dt);
                else
                    DetachGhost(ghost);
            }
        foreach (var p in _players.Values)
        {
            if (!p.Alive || p.PendingRoundStart)
                continue;

            // Determine effective input (may be overridden by a latching ghost).
            var possessor = _players.Values.FirstOrDefault(g => g.Alive && g.Ghost && g.LatchedToId == p.Id);
            var input = possessor is null ? p.Input : p.Input with
            {
                X = p.Input.X + possessor.Input.X*.3f,
                Z = p.Input.Z + possessor.Input.Z*.3f,
            };
            if(input.Length>1)input=input with {X=input.X/input.Length,Z=input.Z/input.Length};
            // Clients preserve stick magnitude for precision aim. With no bomb,
            // the ignored modifier must retain ordinary full-speed walking.
            if(input.Aim&&p.CarryingBombId is null&&input.Length>0)
                input=input with {X=input.X/input.Length,Z=input.Z/input.Length};

            // Count down timers.
            if (p.InvincibleTimer > 0) p.InvincibleTimer = Math.Max(0,p.InvincibleTimer-dt);
            if (p.ImpactProtectionTimer > 0) p.ImpactProtectionTimer = Math.Max(0,p.ImpactProtectionTimer-dt);
            if(p.Falling||p.Airborne||p.StunTimer>0||p.CarriedById is not null)ClearKick(p);
            if(p.KickRecoveryRemaining>0)p.KickRecoveryRemaining=Math.Max(0,p.KickRecoveryRemaining-dt);
            if(p.Falling)
            {
                p.AirVX=input.X*PlayerSpeed;p.AirVZ=input.Z*PlayerSpeed;
                if(input.Length>.1f){p.FacingX=input.X/input.Length;p.FacingZ=input.Z/input.Length;}
                p.PrevA=input.A;p.PrevB=input.B;p.PrevR=input.R;p.PrevStop=input.Stop;
                continue;
            }
            if (ProcessRecovery(p,p.Input,dt)) continue;

            // Edge detection for buttons.
            var aPress = input.A && !p.PrevA;
            var bPress = input.B && !p.PrevB;
            var bRelease = !input.B && p.PrevB;
            var rPress = input.R && !p.PrevR;
            var stopPress = input.Stop && !p.PrevStop;
            p.PrevA = input.A;
            p.PrevB = input.B;
            p.PrevR = input.R;p.PrevStop=input.Stop;

            // Ghosts retain movement/latching, but no bomb input or collision path.
            if(p.Ghost)
            {
                ClearKick(p);ClearThrowAim(p);
                if(p.LatchedToId is null)MovePlayer(p,input,dt);
                if(_ghostsEnabled&&p.LatchedToId is null)TryLatch(p);
                continue;
            }
            if(stopPress)StopKickedBombs(p);
            if(AdvanceKick(p,dt))continue;
            UpdateThrowAim(p,input,dt);

            // A planted aim stance exchanges movement for deliberate direction and range.
            if(!p.Aiming)
            {
                if (p.CarryingBombId is null && p.CarryingPlayerId is null)
                    MovePlayer(p, input, dt);
                else
                    MovePlayerCarrying(p, input, dt);
            }

            // R: remote detonate.
            if (rPress && p.HasRemote)
                RemoteDetonate(context, p);

            if(p.KickBombId is null&&p.KickRecoveryRemaining<=0)
                ProcessBombActions(context,p,input,aPress,bPress,bRelease,dt);
        }
    }

    // ─── movement ─────────────────────────────────────────────────────────────

    private const float PlayerSpeed = 4.5f;  // cells/sec
    private const float PlayerRadius = 0.4f;

    /// <summary>How high above its tier a player stands: a bomb coming down below this lands on their head.</summary>
    private const float HeadHeight = 1.2f;

    private void MovePlayer(BlastPlayer p, InputState input, float dt)
    {
        var startX = p.X;
        var startZ = p.Z;
        var speed=PlayerSpeed*KickMovementScale(p);
        ApplyMoveDelta(p, input.X * speed * dt, input.Z * speed * dt,dt);

        // Update facing for non-zero input.
        if (input.X * input.X + input.Z * input.Z > 0.01f)
        {
            p.FacingX = input.X;
            p.FacingZ = input.Z;
            // Normalise.
            var len = MathF.Sqrt(p.FacingX * p.FacingX + p.FacingZ * p.FacingZ);
            p.FacingX /= len;
            p.FacingZ /= len;
        }

        // Check kick: walk into a resting bomb.
        if(!p.Ghost)CheckKick(p, input, startX, startZ, dt);
    }

    private void MovePlayerCarrying(BlastPlayer p, InputState input, float dt)
    {
        ApplyMoveDelta(p, input.X * PlayerSpeed * dt, input.Z * PlayerSpeed * dt,dt);
        if (!p.AimSet && input.X * input.X + input.Z * input.Z > 0.01f)
        {
            p.FacingX = input.X; p.FacingZ = input.Z;
            var len = MathF.Sqrt(p.FacingX * p.FacingX + p.FacingZ * p.FacingZ);
            p.FacingX /= len; p.FacingZ /= len;
        }
    }

    private void ApplyMoveDelta(BlastPlayer p, float dx, float dz,float dt)
    {
        var originX=(int)MathF.Floor(p.X);var originZ=(int)MathF.Floor(p.Z);
        var previousY=p.Y;
        // Axis-separated to allow sliding along walls.
        var nx = p.X + dx;
        if (!CollidesWithSolid(nx, p.Z, p.Tier, p))
            p.X = nx;
        var nz = p.Z + dz;
        if (!CollidesWithSolid(p.X, nz, p.Tier, p))
            p.Z = nz;

        // Clamp to prevent overshooting entirely past thin walls (rarely matters at 30 Hz).
        p.X = Math.Clamp(p.X, -0.5f, _stage.Width + 0.5f);
        p.Z = Math.Clamp(p.Z, -0.5f, _stage.Height + 0.5f);

        var cell = _stage.At((int)MathF.Floor(p.X), (int)MathF.Floor(p.Z));
        var ground=PlayerLandingHeight(p.X,p.Z);
        if(!p.Ghost&&ground<previousY-.05f&&!CellsConnected(originX,originZ,(int)MathF.Floor(p.X),(int)MathF.Floor(p.Z)))
        {
            p.Airborne=true;p.Falling=true;p.AirVY=0;
            p.AirVX=dx/Math.Max(dt,.0001f);p.AirVZ=dz/Math.Max(dt,.0001f);
            Present("player-fall",p.Id,p.Id,p.X,p.Y,p.Z);
            return;
        }
        p.SurfaceY=ground;p.Tier=(int)MathF.Round(ground);

        // Elimination: out-of-bounds or void/water cell.
        if (cell.IsElimination || p.X < 0 || p.X >= _stage.Width || p.Z < 0 || p.Z >= _stage.Height)
            Eliminate(null, p, cell.Kind == CellKind.Water ? "water" : "fall");
    }

    // Water/void cells have no floor. Their default Tier=0 is serialization
    // metadata, not a surface that can support or drown a falling player.
    // Use the same water plane published to the renderer; void falls below the
    // lowest floor before elimination as well. The player center still defines
    // the ledge, so this does not extend the island by the collision radius.
    private float PlayerLandingHeight(float x,float z) =>
        _stage.At((int)MathF.Floor(x),(int)MathF.Floor(z)).Kind switch
        {
            CellKind.Water => _waterLevel,
            CellKind.Void => -1,
            _ => _stage.SurfaceAt(x,z),
        };

    private bool CollidesWithSolid(float x, float z, int tier, BlastPlayer mover)
    {
        // Ghosts stay on land after their comeback, including when the fall input is still held.
        if(mover.Ghost&&_stage.At((int)MathF.Floor(x),(int)MathF.Floor(z)).IsElimination)return true;
        // Check a small set of cells the circle could touch.
        for (var cx = (int)MathF.Floor(x - PlayerRadius); cx <= (int)MathF.Floor(x + PlayerRadius); cx++)
            for (var cz = (int)MathF.Floor(z - PlayerRadius); cz <= (int)MathF.Floor(z + PlayerRadius); cz++)
            {
                var cell = _stage.At(cx, cz);
                if (cell.IsPassable && CircleOverlapsCell(x, z, cx, cz) &&
                    !CellsConnected((int)MathF.Floor(mover.X), (int)MathF.Floor(mover.Z), cx, cz)&&
                    _stage.SurfaceAt(Math.Clamp(x,cx+.001f,cx+.999f),Math.Clamp(z,cz+.001f,cz+.999f))>mover.Y+.05f&&
                    !EscapingCell(x,z,mover,cx,cz)) return true;
                if (cell.Kind == CellKind.Pillar) // hard wall
                {
                    if (CircleOverlapsCell(x, z, cx, cz))
                        return true;
                }
                if (_softBlocks.IsAlive(cx, cz))
                {
                    if (CircleOverlapsCell(x, z, cx, cz))
                        return true;
                }
            }

        if(mover.Ghost)return false;
        // Resting/stationary bombs on the same tier block movement.
        foreach (var b in _bombs)
        {
            if (b.State != BombState.Resting) continue;
            if (Math.Abs(BombY(b) - _stage.SurfaceAt(x, z)) > .6f) continue;
            if (b.OwnerId == mover.Id && b.OwnerMayLeave) continue; // first departure is geometric, not a time-limited escape
            var distance = Dist2(b.X,b.Z,x,z);
            var previousDistance = Dist2(b.X,b.Z,mover.X,mover.Z);
            var clearance = (PlayerRadius + BlastBomb.Radius) * (PlayerRadius + BlastBomb.Radius);
            if (distance < clearance && !(previousDistance < clearance && distance > previousDistance + .00001f))
                return true;
        }
        return false;
    }

    private bool CellsConnected(int ax, int az, int bx, int bz)
    {
        if (ax == bx && az == bz) return true;
        if (ax == bx || az == bz) return _stage.CanCross(ax, az, bx, bz);
        return (_stage.CanCross(ax, az, bx, az) && _stage.CanCross(bx, az, bx, bz)) ||
            (_stage.CanCross(ax, az, ax, bz) && _stage.CanCross(ax, bz, bx, bz));
    }

    private static bool CircleOverlapsCell(float x, float z, int cx, int cz)
    {
        var nx = Math.Clamp(x, cx, cx + 1f);
        var nz = Math.Clamp(z, cz, cz + 1f);
        var ddx = x - nx;
        var ddz = z - nz;
        return ddx * ddx + ddz * ddz < PlayerRadius * PlayerRadius - .00001f;
    }

    private static bool EscapingCell(float x,float z,BlastPlayer p,int cx,int cz)
    {
        static float Distance(float x,float z,int cx,int cz)
        {var dx=x-Math.Clamp(x,cx,cx+1f);var dz=z-Math.Clamp(z,cz,cz+1f);return dx*dx+dz*dz;}
        var old=Distance(p.X,p.Z,cx,cz);
        return old<=PlayerRadius*PlayerRadius+.000001f&&Distance(x,z,cx,cz)>=old-.000001f;
    }

    // ─── kicking ──────────────────────────────────────────────────────────────

    private void CheckKick(BlastPlayer p, InputState input, float startX, float startZ, float dt)
    {
        ForgetSeparatedKickContacts(p);
        if(p.KickBombId is not null||p.KickRecoveryRemaining>0||p.Airborne)return;
        if (input.X * input.X + input.Z * input.Z < 0.1f)
        {_presentedKickContacts.RemoveWhere(c=>c.PlayerId==p.Id);return;}

        foreach (var b in _bombs)
        {
            if (b.State != BombState.Resting || b.OwnerMayLeave&&b.OwnerId==p.Id || Math.Abs(BombY(b) - p.Y) > .6f) continue;
            var dx = b.X - p.X;
            var dz = b.Z - p.Z;
            var dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dx * input.X + dz * input.Z <= .001f) continue; // walking away never kicks a bomb behind the player
            if (dist > PlayerRadius + BlastBomb.Radius + 0.05f)
            {
                // Movement collision can stop up to one tick short of touching the bomb. Test
                // this tick's intended segment, rather than enlarging the proximity radius.
                var stepX = input.X * PlayerSpeed * dt;
                var stepZ = input.Z * PlayerSpeed * dt;
                var along = Math.Clamp(((b.X - startX) * stepX + (b.Z - startZ) * stepZ) /
                    (stepX * stepX + stepZ * stepZ), 0, 1);
                var clearance = PlayerRadius + BlastBomb.Radius;
                if (Dist2(b.X, b.Z, startX + along * stepX, startZ + along * stepZ) > clearance * clearance)
                    continue;
            }

            // Preserve the full stick angle; the bomb must clear a running kicker.
            var magnitude = MathF.Sqrt(input.X * input.X + input.Z * input.Z);
            var nx = input.X / magnitude;
            var nz = input.Z / magnitude;

            BeginKick(p,b,nx,nz);
            break; // one kick per tick
        }
    }

    // ─── bomb placement ───────────────────────────────────────────────────────

    private void TryPlaceBomb(ISessionContext context, BlastPlayer p, bool hold = false)
    {
        if (p.ActiveBombs >= p.BombCapacity)
        {Present("bomb-blocked",playerId:p.Id,x:p.X,y:p.Y,z:p.Z,reason:"capacity");return;}

        float x=p.X,z=p.Z;
        if(!hold&&!TryBombPlacement(p,p.FacingX,p.FacingZ,0,out x,out z))
        {Present("bomb-blocked",playerId:p.Id,x:p.X,y:p.Y,z:p.Z,reason:"space");return;}
        var cx = (int)MathF.Floor(x);
        var cz = (int)MathF.Floor(z);

        var bomb = new BlastBomb
        {
            Id = NextBombId(),
            OwnerId = p.Id,
            OwnerMayLeave = !hold,
            X = x, Z = z,
            CellX = cx, CellZ = cz, Tier = (int)MathF.Round(_stage.SurfaceAt(x, z)),
            FireLevel = p.FireLevel,
            Mode = p.BombType,
            FuseTimer = p.BombType=="bounce"?3.5f:BlastBomb.FuseSeconds,
        };
        _bombs.Add(bomb);
        if(p.IsBot&&BotEnemyInRange(bomb.X,BombY(bomb),bomb.Z,bomb.BlastRadius,p.Id,bomb.IsRed))_aiLastThreat[p.Id]=_roundTimer;
        p.ActiveBombs++;
        if (hold) { bomb.State = BombState.Carried; p.CarryingBombId = bomb.Id; UpdateCarriedBombPosition(bomb); }
        PresentBomb(hold ? "bomb-carry" : "bomb-drop", bomb, p.Id);
    }

    // ─── pick up / throw ──────────────────────────────────────────────────────

    private void TryPickUp(ISessionContext context, BlastPlayer p)
    {
        if (p.CarryingBombId is not null || p.CarryingPlayerId is not null) return;

        // Try a bomb first.
        var bomb = _bombs.Where(b => b.State == BombState.Resting && Math.Abs(BombY(b) - p.Y) < .6f)
            .OrderBy(b => Dist2(b.X, b.Z, p.X, p.Z))
            .FirstOrDefault(b => Dist2(b.X, b.Z, p.X, p.Z) <= (PlayerRadius + BlastBomb.Radius + 0.3f) * (PlayerRadius + BlastBomb.Radius + 0.3f));
        if (bomb is not null)
        {
            bomb.State = BombState.Carried;
            p.CarryingBombId = bomb.Id;
            PresentBomb("lift", bomb, p.Id);
            return;
        }

        // Try a stunned player (ghosts cannot pick up players).
        if (!p.Ghost)
        {
            var victim = _players.Values
                .Where(v => v.Id != p.Id && v.Alive && !v.Ghost && v.StunTimer > 0 && v.CarriedById is null && !v.Airborne && Math.Abs(v.Y - p.Y) < .6f)
                .OrderBy(v => Dist2(v.X, v.Z, p.X, p.Z))
                .FirstOrDefault(v => Dist2(v.X, v.Z, p.X, p.Z) <= (PlayerRadius * 2 + 0.3f) * (PlayerRadius * 2 + 0.3f));
            if (victim is not null)
            {
                BeginHold(p,victim);
            }
        }
    }

    private void TryPickUpBombOnly(BlastPlayer p)
    {
        if (p.Ghost || p.CarryingBombId is not null) return;
        var bomb = _bombs.Where(b => b.State == BombState.Resting && Math.Abs(BombY(b) - p.Y) < .6f)
            .OrderBy(b => Dist2(b.X, b.Z, p.X, p.Z))
            .FirstOrDefault(b => Dist2(b.X, b.Z, p.X, p.Z) <= (PlayerRadius + BlastBomb.Radius + 0.3f) * (PlayerRadius + BlastBomb.Radius + 0.3f));
        if (bomb is not null)
        {
            bomb.State = BombState.Carried;
            p.CarryingBombId = bomb.Id;
            PresentBomb("lift", bomb, p.Id);
        }
    }

    private void ThrowCarried(ISessionContext context, BlastPlayer p, InputState input)
    {
        if (p.CarryingBombId is not null)
        {
            ThrowBomb(context, p, input);
        }
        else if (p.CarryingPlayerId is not null)
        {
            ThrowPlayer(context, p, input);
        }
    }

    private void ThrowBomb(ISessionContext context, BlastPlayer p, InputState input)
    {
        var bomb = _bombs.FirstOrDefault(b => b.Id == p.CarryingBombId);
        if (bomb is null) { p.CarryingBombId = null; ClearThrowAim(p); return; }

        // A released stick still throws decisively clear of the carrier. Pumps
        // project the larger blast farther away, rewarding the setup with space.
        var velocity = p.AimSet ? BombThrowRules.AimedVelocity(p.AimX,p.AimZ,p.AimRange,bomb.MotionRate,bomb.Pumps) :
            BombThrowRules.Velocity(p.FacingX,p.FacingZ,input.Length,bomb.Pumps);
        var speed = MathF.Sqrt(velocity.X*velocity.X+velocity.Z*velocity.Z);

        bomb.State = BombState.Flying;
        bomb.RequestedPumps=0; bomb.ChargeRemaining=0; bomb.Bounces=0;
        bomb.LandingY=null;
        bomb.LastTouchedById=p.Id;
        bomb.X = p.X;
        bomb.Z = p.Z;
        bomb.FlyVX = velocity.X;
        bomb.FlyVZ = velocity.Z;
        bomb.FlyVY = velocity.Y;
        bomb.FlyY = p.Y + BombThrowRules.LaunchHeight; // launch slightly above tier
        bomb.Tier = p.Tier;

        p.CarryingBombId = null;
        ClearThrowAim(p);
        p.CarryFromPrimary = false;
        PresentBomb("throw", bomb, p.Id, speed);
    }

    private void ThrowPlayer(ISessionContext context, BlastPlayer p, InputState input)
    {
        if (!_players.TryGetValue(p.CarryingPlayerId!, out var victim)) { p.CarryingPlayerId = null; return; }

        var strength=Math.Clamp(input.Length,0.2f,1f);
        victim.CarriedById=null;victim.HoldRemaining=0;victim.Airborne=true;
        victim.X=p.X;victim.Z=p.Z;victim.SurfaceY=p.Y+PlayerCarryHeight;
        victim.AirVX=p.FacingX*(2+strength*3);victim.AirVZ=p.FacingZ*(2+strength*3);victim.AirVY=3.8f;
        victim.StunTimer=.65f;victim.RecoveryTotal=.65f;victim.KnockdownReason="thrown";
        victim.ImpactProtectionTimer=ImpactProtectionSeconds;p.CarryingPlayerId=null;
        Present("player-throw",victim.Id,p.Id,p.X,p.Y+PlayerCarryHeight,p.Z,strength,reason:"player");
    }

    // ─── pumping ──────────────────────────────────────────────────────────────

    private void PumpBomb(BlastPlayer p)
    {
        var bomb = _bombs.FirstOrDefault(b => b.Id == p.CarryingBombId);
        if (bomb is null) return;
        if (bomb.Pumps + bomb.RequestedPumps >= 3) return;
        bomb.RequestedPumps++;
        if(bomb.ChargeRemaining<=0)
            bomb.ChargeRemaining=bomb.ChargeDuration=p.HasFocus?PowerUpRules.FocusPumpSeconds:PowerUpRules.NormalPumpSeconds;
    }

    // ─── remote detonation ────────────────────────────────────────────────────

    private void RemoteDetonate(ISessionContext context, BlastPlayer p)
    {
        var mine = _bombs.Where(b => b.OwnerId == p.Id && b.IsRemote && !b.Exploded && b.State != BombState.Carried).ToList();
        if (mine.Count == 0) return;
        // Detonate the oldest remote bomb.
        ExplodeBomb(context, mine[0]);
    }

    // ─── bomb simulation ──────────────────────────────────────────────────────

    private void TickBombs(ISessionContext context, float dt)
    {
        for (var i = _bombs.Count - 1; i >= 0; i--)
        {
            var b = _bombs[i];
            if (b.Exploded) { _bombs.RemoveAt(i); continue; }
            if(b.State!=BombState.Carried){b.RequestedPumps=0;b.ChargeRemaining=0;}

            if(b.OwnerMayLeave&&(b.State!=BombState.Resting||b.OwnerId is null||
                !_players.TryGetValue(b.OwnerId,out var placingOwner)||!placingOwner.Alive||placingOwner.Ghost||
                Math.Abs(placingOwner.Y-BombY(b))>.6f||Dist2(b.X,b.Z,placingOwner.X,placingOwner.Z)>.8f*.8f))
                b.OwnerMayLeave=false;

            // A remote cannot wait for a command its owner can no longer send. Keep
            // active owners' deliberate remote semantics, but resume a real fuse for
            // abandoned bombs rather than leaving an immortal obstacle in a corner.
            if(b.IsRemote && (b.OwnerId is null || !_players.TryGetValue(b.OwnerId,out var remoteOwner) ||
                !remoteOwner.Alive || remoteOwner.Ghost || !remoteOwner.HasRemote || !context.IsConnected(remoteOwner.Id)))
            {
                b.IsRemote=false;
                PresentBomb("remote-fuse",b,b.OwnerId,b.FuseTimer);
            }

            // Fuse countdown (not while carried or commandable remote).
            if (b.State != BombState.Carried && !b.IsRemote)
            {
                b.FuseTimer -= dt;
                if (b.FuseTimer <= 0)
                {
                    ExplodeBomb(context, b);
                    continue;
                }
            }

            switch (b.State)
            {
                case BombState.Resting:
                    if(b.LandingY is { } support&&support>StaticBombLandingHeight(b.X,b.Z)+.001f)
                    {
                        b.State=BombState.Flying;b.FlyY=support;b.FlyVX=0;b.FlyVZ=0;b.FlyVY=0;b.LandingY=null;
                        TickFlyingBomb(context,b,dt);
                    }
                    break;
                case BombState.Rolling: TickRollingBomb(context, b, dt*b.MotionRate); break;
                case BombState.Flying: TickFlyingBomb(context, b, dt*b.MotionRate); break;
                case BombState.Carried:
                    UpdateCarriedBombPosition(b);
                    if(b.RequestedPumps>0)
                    {
                        b.ChargeRemaining-=dt;
                        if(b.ChargeRemaining<=.00001f)
                        {
                            b.Pumps++;b.RequestedPumps--;
                            var carrier=_players.Values.FirstOrDefault(p=>p.CarryingBombId==b.Id);
                            if(carrier!=null){carrier.PumpSerial++;PresentBomb("pump",b,carrier.Id,b.Pumps);}
                            b.ChargeRemaining=b.RequestedPumps>0 ? b.ChargeDuration=carrier?.HasFocus==true?PowerUpRules.FocusPumpSeconds:PowerUpRules.NormalPumpSeconds : 0;
                        }
                    }
                    break;
            }
        }
    }

    private float BombY(BlastBomb bomb) => bomb.State == BombState.Flying ? bomb.FlyY :
        bomb.State == BombState.Carried ? (_players.Values.FirstOrDefault(p => p.CarryingBombId == bomb.Id)?.Y ?? bomb.Tier) + BombThrowRules.LaunchHeight :
        (bomb.State==BombState.Resting?bomb.LandingY??_stage.SurfaceAt(bomb.X,bomb.Z):_stage.SurfaceAt(bomb.X,bomb.Z)) + (bomb.State==BombState.Rolling && bomb.KickHopRemaining>0
            ? BlastBomb.KickHopHeight*MathF.Sin(MathF.PI*bomb.KickHopRemaining/BlastBomb.KickHopSeconds) : 0);

    private void TickRollingBomb(ISessionContext context, BlastBomb b, float dt)
    {
        b.KickHopRemaining=Math.Max(0,b.KickHopRemaining-dt);
        // Sweep the whole circle in short steps so faster kicks cannot tunnel through
        // terrain, players, or other bombs between authoritative ticks.
        var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(b.VX*b.VX+b.VZ*b.VZ)*dt/.1f));
        for (var step=0;step<steps && !b.Exploded;step++)
        {
            if(b.State==BombState.Rolling)StepRollingBomb(context,b,dt/steps);
            else if(b.State==BombState.Flying&&b.ResumeRollingAfterFall)TickFallingKickedBomb(context,b,dt/steps);
            else break;
        }
    }

    private void StepRollingBomb(ISessionContext context, BlastBomb b, float dt)
    {
        var nx = b.X + b.VX * dt;
        var nz = b.Z + b.VZ * dt;
        var cx = (int)nx;
        var cz = (int)nz;

        var cell = _stage.At(cx, cz);
        if (!BombTerrainClear(nx, nz, (int)MathF.Floor(b.X), (int)MathF.Floor(b.Z),allowDescent:true,movingBomb:b))
        {
            if(b.IsBouncy)
            {
                bool xClear=BombTerrainClear(nx,b.Z,(int)MathF.Floor(b.X),(int)MathF.Floor(b.Z),true,b);
                bool zClear=BombTerrainClear(b.X,nz,(int)MathF.Floor(b.X),(int)MathF.Floor(b.Z),true,b);
                if(!xClear)b.VX=-b.VX;
                if(!zClear)b.VZ=-b.VZ;
                if(xClear&&zClear){b.VX=-b.VX;b.VZ=-b.VZ;}
                b.Bounces++;PresentBomb("bounce",b,b.LastTouchedById);
            }
            else {b.State=BombState.Resting;b.VX=b.VZ=0;}
            return;
        }

        var previousY=_stage.SurfaceAt(b.X,b.Z);
        if(BombLandingHeight(nx,nz)<previousY-.05f &&
            !CellsConnected((int)MathF.Floor(b.X),(int)MathF.Floor(b.Z),cx,cz))
        {
            b.State=BombState.Flying;b.ResumeRollingAfterFall=true;
            b.FlyVX=b.VX;b.FlyVZ=b.VZ;b.FlyVY=0;b.FlyY=previousY;
            b.KickHopRemaining=0;
            TickFallingKickedBomb(context,b,dt);
            return;
        }

        // Check collision with a player (stun) or another resting bomb (billiard).
        foreach (var p in _players.Values)
        {
            if (!CanReceiveBombImpact(p) || p.Airborne || Math.Abs(p.Y - _stage.SurfaceAt(nx, nz)) > .6f) continue;
            // A player following an outgoing kick must not collide with its trailing edge.
            if((p.X-b.X)*b.VX+(p.Z-b.Z)*b.VZ<=0)continue;
            if (Dist2(nx, nz, p.X, p.Z) < (PlayerRadius + BlastBomb.Radius) * (PlayerRadius + BlastBomb.Radius))
            {
                KnockDown(p,1.5f,"rolling-bomb",b.Id,b.LastTouchedById??b.OwnerId);
                var speed=MathF.Sqrt(b.VX*b.VX+b.VZ*b.VZ);
                ApplyPlayerImpulse(p,b.VX/speed*BlastBomb.ImpactHorizontalSpeed,
                    b.VZ/speed*BlastBomb.ImpactHorizontalSpeed,BlastBomb.ImpactUpSpeed);
                b.X=nx;b.Z=nz;b.CellX=cx;b.CellZ=cz;
                b.State = BombState.Resting;
                b.VX = 0; b.VZ = 0;
                return;
            }
        }

        foreach (var other in _bombs)
        {
            if (other == b || other.State != BombState.Resting || Math.Abs(BombY(other) - _stage.SurfaceAt(nx, nz)) > .6f) continue;
            if (Dist2(nx, nz, other.X, other.Z) < (BlastBomb.Radius * 2 + 0.05f) * (BlastBomb.Radius * 2 + 0.05f))
            {
                // Transfer the shot, including its brief launch hop, without resetting either fuse.
                LaunchKickedBomb(other,b.VX,b.VZ,b.LastTouchedById);
                b.State = BombState.Resting;
                b.VX = 0; b.VZ = 0;
                return;
            }
        }

        b.X = nx;
        b.Z = nz;
        b.CellX = cx;
        b.CellZ = cz;
        b.Tier = (int)MathF.Round(_stage.SurfaceAt(b.X, b.Z));

        // Elimination: rolled off the stage.
        if (cell.IsElimination)
        {
            b.Exploded = true;
            DecrementOwnerBombs(b.OwnerId);
        }
    }

    private void TickFlyingBomb(ISessionContext context, BlastBomb b, float dt)
    {
        if(b.ResumeRollingAfterFall){TickFallingKickedBomb(context,b,dt);return;}
        var flight=BombThrowRules.AdvanceStatic(new(b.X,b.FlyY,b.Z),new(b.FlyVX,b.FlyVY,b.FlyVZ),dt,
            BombLandingHeight,(x,z)=>_stage.At(x,z).Kind,(x,z)=>_softBlocks.IsAlive(x,z),b.IsBouncy,b.GravityScale);
        b.X=flight.Position.X;b.Z=flight.Position.Z;b.FlyY=flight.Position.Y;b.FlyVY=flight.Velocity.Y;b.FlyVX=flight.Velocity.X;b.FlyVZ=flight.Velocity.Z;

        var landingY = StaticBombLandingHeight(b.X, b.Z);

        // A head hit is a bomb coming down onto a player's head; on the way up it clears the thrower.
        foreach (var p in _players.Values)
        {
            if (!CanReceiveBombImpact(p)) continue;
            if (b.FlyVY > 0 || b.FlyY > p.Y + HeadHeight || b.FlyY < p.Y) continue;
            if (Dist2(b.X, b.Z, p.X, p.Z) < (PlayerRadius + BlastBomb.Radius) * (PlayerRadius + BlastBomb.Radius))
            {
                // Head hit: stun the player, bomb bounces (continues).
                KnockDown(p,2.0f,"falling-bomb",b.Id,b.LastTouchedById??b.OwnerId);
                // A head impact dissipates energy instead of relaunching a large bomb.
                b.FlyVY=Math.Min(MathF.Abs(b.FlyVY)*.3f,b.IsBouncy?3.2f:2.0f)/b.GravityScale;
                float retention=b.IsBouncy?.7f:.5f/b.GravityScale;
                b.FlyVX*=retention;b.FlyVZ*=retention;
            }
        }

        // Landed.
        if (b.FlyY <= landingY)
        {
            b.FlyY = landingY;
            var cell = _stage.At((int)b.X, (int)b.Z);
            if (cell.IsElimination || b.X < 0 || b.X >= _stage.Width || b.Z < 0 || b.Z >= _stage.Height)
            {
                b.Exploded = true;
                DecrementOwnerBombs(b.OwnerId);
            }
            else if(b.IsBouncy && b.Pumps>0 && BombThrowRules.CanBounce(b.Bounces,b.FlyVY))
            {
                b.Bounces++; b.FlyVY=BombThrowRules.BounceVelocity(b.FlyVY);
                PresentBomb("bounce",b,b.LastTouchedById);
            }
            else
            {
                b.State = BombState.Resting;
                b.LandingY=landingY;
                b.CellX = (int)b.X;
                b.CellZ = (int)b.Z;
                b.Tier = (int)MathF.Round(landingY); // update tier to where it landed
                b.FlyVX = 0; b.FlyVZ = 0; b.FlyVY = 0;
            }
        }
    }

    private void UpdateCarriedBombPosition(BlastBomb b)
    {
        b.LandingY=null;
        // Whoever holds it, which need not be the player who placed it.
        var carrier = _players.Values.FirstOrDefault(p => p.CarryingBombId == b.Id);
        if (carrier is null) return;
        b.X = carrier.X + carrier.FacingX * (PlayerRadius + BlastBomb.Radius + 0.05f);
        b.Z = carrier.Z + carrier.FacingZ * (PlayerRadius + BlastBomb.Radius + 0.05f);
        b.Tier = carrier.Tier;
        b.CellX = (int)b.X;
        b.CellZ = (int)b.Z;
    }

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

    // ─── items ────────────────────────────────────────────────────────────────

    private void DropItem(ISessionContext context, int cx, int cz, int tier, string? guaranteedType = null)
    {
        var type = guaranteedType ?? PowerUpRules.ChooseDrop(context.Random);
        _items.Add(new BlastItem { Id = NextItemId(), CellX = cx, CellZ = cz, Tier = tier, Type = type });
    }

    private void TickItems(ISessionContext context)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if(item.LifeRemaining>=0){item.LifeRemaining-=(float)context.TickDelta.TotalSeconds;if(item.LifeRemaining<=0){_items.RemoveAt(i);continue;}}
            item.OwnerLockRemaining=Math.Max(0,item.OwnerLockRemaining-(float)context.TickDelta.TotalSeconds);
            // Check if any player (or ghost) walks over it.
            foreach (var p in _players.Values)
            {
                if (!p.Alive || p.PendingRoundStart) continue;
                if (p.Ghost)
                {
                    // Ghost touching an item destroys it without effect.
                    if (p.Tier == item.Tier && Dist2(p.X, p.Z, item.X, item.Z) < PlayerRadius * PlayerRadius)
                    {
                        _items.RemoveAt(i);
                        goto nextItem;
                    }
                    continue;
                }
                if (!p.Alive || p.Tier != item.Tier || !PowerUpRules.CanCollect(p,item.Type) || item.DroppedBy==p.Id&&item.OwnerLockRemaining>0) continue;
                if (Dist2(p.X, p.Z, item.X, item.Z) < (PlayerRadius + 0.4f) * (PlayerRadius + 0.4f))
                {
                    var replaced=PowerUpRules.Exchange(p,item.Type);
                    _items.RemoveAt(i);
                    if(replaced!=null)DropEquipment(p,replaced);
                    goto nextItem;
                }
            }
            nextItem:;
        }
    }

    private static void ApplyItem(BlastPlayer p,string type) => PowerUpRules.Apply(p,type);
    private void DropEquipment(BlastPlayer player,string type)
    {
        _items.Add(new BlastItem {Id=NextItemId(),CellX=(int)MathF.Floor(player.X),CellZ=(int)MathF.Floor(player.Z),Tier=player.Tier,Type=type,DroppedBy=player.Id,OwnerLockRemaining=PowerUpRules.SwapPickupDelay});
    }

    // ─── player hits ──────────────────────────────────────────────────────────

    private void HitPlayer(ISessionContext context, BlastPlayer p, string reason = "explosion", string? causedBy = null)
    {
        if (!p.Alive || p.Ghost || p.InvincibleTimer > 0) return;

        if (p.HasHeart)
        {
            p.HasHeart = false;
            p.InvincibleTimer = 2.0f;
            return;
        }

        Eliminate(causedBy, p, reason);
    }

    private void Eliminate(string? causedBy, BlastPlayer p, string reason)
    {
        if (!p.Alive) return;
        Present("death", p.Id, p.Id, p.X, p.Y, p.Z, reason: reason, causedBy: causedBy);
        if (_practice) { RespawnPracticePlayer(p); Present("respawn", p.Id, p.Id, p.X, p.Y, p.Z, reason: reason); return; }
        p.Alive = false;
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
        PowerUpRules.Reset(player);
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

        if (type is null) return;
        _items.Add(new BlastItem { Id = NextItemId(), CellX = (int)p.X, CellZ = (int)p.Z, Tier = p.Tier, Type = type });
    }

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
        var victim=ghost.LatchedToId;
        ghost.LatchedToId = null;
        ghost.LatchTimer = 0;
        ghost.LatchReverseCount = 0;
        ghost.StunTimer = 0.5f; // give the victim time to move away before another latch
        Present("ghost-detach",ghost.Id,victim,ghost.X,ghost.Y,ghost.Z);
    }

    private void DetachIfLatched(BlastPlayer p)
    {
        if (p.LatchedToId is not null) DetachGhost(p);
    }

    // ─── soft blocks ─────────────────────────────────────────────────────────

    private void PlaceSoftBlocks(Random rng)
    {
        _softBlocks = new SoftBlockGrid(_stage.Width, _stage.Height);
        _coverLootZones.Clear();_claimedLootZones.Clear();
        if (_stage.FixedSoftBlocks is { } fixedBlocks)
        {
            foreach(var cell in fixedBlocks) _softBlocks.Set(cell.Item1,cell.Item2,true);
            return;
        }
        if (_practice) return;
        var spawnCells = new HashSet<(int, int)>(_stage.Spawns.SelectMany(s =>
            new[] { (s.X, s.Z), (s.X + 1, s.Z), (s.X - 1, s.Z), (s.X, s.Z + 1), (s.X, s.Z - 1) }));

        var placed = 0;
        var candidates = new List<(int X,int Z)>();
        for (var x = 0; x < _stage.Width; x++)
            for (var z = 0; z < _stage.Height; z++)
            {
                var cell = _stage.At(x, z);
                if (cell.Kind != CellKind.Floor) continue;
                if (spawnCells.Contains((x, z))) continue;
                if (_stage.ClearCells.Contains((x, z))) continue;
                if (new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => _stage.At(x + d.Item1, z + d.Item2).Kind == CellKind.Ramp)) continue;
                if (_stage.CoverAreas.Count>0) { candidates.Add((x,z)); continue; }
                if (placed < _stage.MaxSoftBlocks && rng.NextDouble() < _stage.SoftBlockDensity)
                { _softBlocks.Set(x, z, true); placed++; }
            }
        if (_stage.CoverAreas.Count>0) PlaceRegionalCover(rng,candidates);
    }

    // ─── hurry: column closure ────────────────────────────────────────────────

    private void KillInClosedColumns(ISessionContext context)
    {
        foreach (var p in _players.Values)
        {
            if (!p.Alive || p.Ghost) continue;
            if ((int)p.X < _closedLeft || (int)p.X >= _stage.Width - _closedRight)
                HitPlayer(context, p, "columns");
        }
        // Also explode resting bombs in closed columns.
        foreach (var b in _bombs.ToList())
        {
            if (b.State != BombState.Resting) continue;
            if (b.CellX < _closedLeft || b.CellX >= _stage.Width - _closedRight)
                ExplodeBomb(context, b);
        }
    }

    // ─── bot AI ──────────────────────────────────────────────────────────────

    // ─── stage message ────────────────────────────────────────────────────────

    private void SendStage(ISessionContext context, string playerId)
    {
        var w = _stage.Width;
        var h = _stage.Height;
        var flat = new object[w * h];
        for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
            {
                var c = _stage.At(x, z);
                flat[z * w + x] = new
                {
                    k = (int)c.Kind,
                    t = c.Tier,
                    rx = c.RampDX,
                    rz = c.RampDZ,
                    rf = c.RampFromTier,
                    rt = c.RampToTier,
                };
            }

        context.Send(playerId, "stage", new
        {
            id = _stage.Id,
            name = _stage.Name,
            width = w,
            height = h,
            cells = flat,
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
            mode = b.Mode, bounces = b.Bounces, motionRate = b.MotionRate, gravityScale=b.GravityScale,
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
            y = _stage.SurfaceAt(it.X, it.Z),
        });

        var timeLeft = Math.Max(0, _roundSeconds - _roundTimer);

        context.Broadcast("state", new
        {
            round = _roundNum,
            serverTime = context.Now.ToUnixTimeMilliseconds(),
            presentationSeq = _presentationSeq,
            events = SnapshotPresentationEvents(),
            phase = (int)_phase,
            timeLeft = MathF.Round(timeLeft, 1),
            closedLeft = _closedLeft,
            closedRight = _closedRight,
            hurryMode = !_suddenDeath || _practice ? "none" : _stage.Id == BlastStages.SunkenTemple ? "water" : "columns",
            waterLevel = _waterLevel,
            waterStartsIn = !_practice && _suddenDeath && _stage.Id == BlastStages.SunkenTemple ? Math.Max(0,_roundSeconds-60+FloodRules.RiseDelay-_roundTimer) : -1,
            waterRiseRate = !_practice && _suddenDeath && _stage.Id == BlastStages.SunkenTemple && _roundTimer >= _roundSeconds-60+FloodRules.RiseDelay ? FloodRules.RisePerSecond : 0,
            hurryStartsIn = _practice || !_suddenDeath ? -1 : Math.Max(0,_roundSeconds - 60 - _roundTimer),
            players = playerList,
            bombs = bombList,
            hazards = hazardList,
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

    // ─── helpers ──────────────────────────────────────────────────────────────

    private static float Dist2(float ax, float az, float bx, float bz) =>
        (ax - bx) * (ax - bx) + (az - bz) * (az - bz);

    private static float Finite(float v) => float.IsFinite(v) ? Math.Clamp(v, -2f, 2f) : 0f;

    private void DecrementOwnerBombs(string? ownerId)
    {
        if (ownerId is not null && _players.TryGetValue(ownerId, out var owner))
            owner.ActiveBombs = Math.Max(0, owner.ActiveBombs - 1);
    }

    private void DropCarriedBombs(BlastPlayer p)
    {
        ClearKick(p);
        ClearThrowAim(p);
        p.CarryFromPrimary=false;p.PrimaryBombCandidateId=null;p.PrimaryHoldTimer=0;
        if(p.CarriedById is not null)ReleaseHeld(p,"interrupted");
        if (p.CarryingBombId is not null)
        {
            var b = _bombs.FirstOrDefault(b => b.Id == p.CarryingBombId);
            if (b is not null) { b.State = BombState.Resting; b.CellX = (int)b.X; b.CellZ = (int)b.Z; }
            p.CarryingBombId = null;
        }
        if (p.CarryingPlayerId is not null)
        {
            if (_players.TryGetValue(p.CarryingPlayerId, out var held))
                ReleaseHeld(held,"carrier-interrupted");
            p.CarryingPlayerId = null;
        }
    }

    private int FindFreeSlot()
    {
        var used = _players.Values.Select(p => p.Slot).ToHashSet();
        for (var i = 0; i < 256; i++)
            if (!used.Contains(i)) return i;
        return 0;
    }

    private static int TryInt(GameSessionOptions opts, string key, int def, int min, int max) =>
        opts.Settings.TryGetValue(key, out var s) && int.TryParse(s, out var v)
            ? Math.Clamp(v, min, max) : def;

    private static bool TryBool(GameSessionOptions opts, string key, bool def) =>
        opts.Settings.TryGetValue(key, out var s) ? s is "on" or "true" or "1" : def;

    /// <summary>JSON shape of the client's input message.</summary>
    private sealed class InputMsg
    {
        public float X { get; set; }
        public float Z { get; set; }
        public bool A { get; set; }
        public bool B { get; set; }
        public bool R { get; set; }
        public bool Aim { get; set; }
        public bool Stop { get; set; }
    }

}

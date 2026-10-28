// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Burbuja.Realtime.Abstractions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;
using Bomber.Game.Tutorial;

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
    private readonly TutorialLesson? _tutorialLesson;
    private readonly HashSet<string> _tutorialSignals = [];
    private int _tutorialStep, _tutorialEfforts, _tutorialBlastTick=-1, _tutorialBlastsAtTick;
    private long _tutorialPickupSerial;
    private string[] _tutorialItems=[];
    private string? _tutorialPlantedBomb;
    private float _tutorialTime, _tutorialAimTime;
    private bool _tutorialFailed, _tutorialComplete, _tutorialStarted;
    private string _tutorialFeedback = "";
    private readonly AdvancedRules _advanced;
    private readonly int _winsTarget;
    private readonly int _roundSeconds;
    private readonly bool _suddenDeath;
    private readonly bool _ghostsEnabled;
    private readonly int _botCount;
    private readonly int _lightingOffset;
    private static readonly string[] StageLightings=["morning","midday","afternoon","night"];
    private static readonly (float X,float Z)[] BotPlacementDirections=Enumerable.Range(0,32).Select(direction=>
    {
        float angle=direction*MathF.Tau/32;
        return (MathF.Cos(angle),MathF.Sin(angle));
    }).ToArray();

    /// <summary>CPU players fill the arena, so a lobby with bots starts with one human, as in the original.</summary>
    public override int? MinPlayersToStart => _botCount > 0 ? 1 : null;
    private readonly StageLayout _stage;

    // ─── round state ─────────────────────────────────────────────────────────

    private RoundPhase _phase;
    private int _roundNum;
    private float _weatherTime;
    private readonly int _weatherSeed;
    private float _phaseTimer;     // seconds until phase transition
    private long _fightTicks;
    private float _waterLevel = -1;
    private float _roundTimer;     // time elapsed in current fight/hurry phase
    private int _closedLeft;       // hurry: columns closed from the left (x < _closedLeft = dead zone)
    private int _closedRight;      // hurry: columns closed from the right (x >= Width-_closedRight = dead zone)
    private float _meteorTimer;
    private int _meteorSequence;
    private readonly List<BlastMeteor> _meteors=[];
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
    private MatchResult? _pendingMatchResult;
    private bool _waterfallStarted;

    // Per-session ID counters — deterministic given the same sequence of events.
    private int _nextBombId;
    private int _nextHazardId;
    private int _nextItemId;

    private string NextBombId()   => $"b{++_nextBombId}";
    private string NextHazardId() => $"h{++_nextHazardId}";
    private string NextItemId()   => $"i{++_nextItemId}";

    public BlastSession(GameSessionOptions options, StageLayout? arena = null, bool practice = false, IReadOnlyList<int>? appearances = null, string? tutorialLesson = null)
    {
        _localAppearances = appearances?.ToArray() ?? [];
        _practice = practice;
        _tutorialLesson = practice && tutorialLesson is not null ? TutorialCourse.Get(tutorialLesson) : null;
        _advanced=AdvancedRules.FromSettings(options.Settings);
        // Cosmetic selection must not consume gameplay RNG (cover, loot or bots).
        _lightingOffset=(int)((uint)options.Seed%4);
        _weatherSeed=options.Seed;
        string stageId=options.Settings.GetValueOrDefault("stage",BlastStages.SunkenTemple);
        _stage = arena ?? BlastStages.Get(!practice && stageId==BlastStages.CircuitGarden ? BlastStages.SunkenTemple : stageId);

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
            // Optional pointer landing target; older clients omit it.
            System.Numerics.Vector2? target = raw.Tx is { } tx && raw.Tz is { } tz && float.IsFinite(tx) && float.IsFinite(tz)
                ? new(tx, tz) : null;
            p.ReceiveInput(new InputState(x, z, raw.A, raw.B, raw.R, raw.Aim, raw.Stop, target, target is not null && raw.Tr));
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
                if(!_tutorialComplete && !_tutorialFailed) SimulateRound(context, dt);
                break;

            case RoundPhase.Over:
                _phaseTimer -= dt;
                if (_phaseTimer <= 0)
                    AdvanceRound(context);
                break;
        }

        if (_matchEndScheduled) return;

        // Broadcast at 15 Hz (every other tick).
        if (_tickCount % 2 == 0)
            BroadcastState(context);
    }

    public override void OnMatchEnded(ISessionContext context, MatchResult result) { }

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
        public float? Tx { get; set; }
        public float? Tz { get; set; }
        public bool Tr { get; set; }
    }

}

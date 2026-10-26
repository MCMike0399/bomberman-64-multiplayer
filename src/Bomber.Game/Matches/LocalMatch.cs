using System.Text.Json;
using Burbuja.Realtime.Abstractions;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Stages;

namespace Bomber.Game.Matches;

/// <summary>Runs the same authoritative session as the online server, without a network or Godot dependency.
/// Call Tick exactly 30 times per second. All calls and events belong on the owning game thread.</summary>
public sealed class LocalMatch
{
    public const int TickRate = 30;
    private readonly BlastSession _session;
    private readonly LocalContext _context;
    private long _sequence;
    public event Action<string>? Message;
    public event Action<JsonElement>? StructuredMessage;
    private string? _stageJson,_stateJson;
    public JsonElement? StageMessage { get; private set; }
    public JsonElement? StateMessage { get; private set; }
    public string? StageJson => _stageJson??=StageMessage?.GetRawText();
    public string? StateJson => _stateJson??=StateMessage?.GetRawText();
    public bool IsEnded => _context.State == LobbyState.Ended;
    public MatchResult? Result => _context.Result;
    public int HumanPlayers => _context.Players.Count;
    public long TickNumber => _context.Tick;

    public LocalMatch(MatchOptions options) : this(options, null) { }

    internal LocalMatch(MatchOptions options, StageLayout? arena)
    {
        options.Validate();
        _context = new LocalContext(options, Receive);
        _session = new BlastSession(new GameSessionOptions("local", "blast", _context.Settings, options.Seed), arena, options.Practice, options.PlayerAppearances, options.TutorialLesson);
        foreach (var player in _context.Players) _session.OnPlayerJoined(_context, player);
        _context.State = LobbyState.Running;
        _session.OnMatchStarted(_context);
        // Prime a snapshot so a renderer can draw immediately. Ready lasts two seconds.
        Tick();
        Tick();
    }

    public void SetInput(int localSlot, PlayerInput input)
    {
        if (localSlot < 0 || localSlot >= HumanPlayers) throw new ArgumentOutOfRangeException(nameof(localSlot));
        if (IsEnded) return;
        var player = _context.Players[localSlot];
        var data = JsonSerializer.SerializeToElement(new
        {
            x = float.IsFinite(input.X) ? input.X : 0,
            z = float.IsFinite(input.Z) ? input.Z : 0,
            a = input.A, b = input.B, r = input.R, aim = input.Aim, stop = input.Stop,
        });
        var sequence = ++_sequence;
        _context.Sequences[player.PlayerId] = sequence;
        _session.OnMessage(_context, player, new InboundMessage("input", data, sequence, _context.Now));
    }

    public void SetAppearance(int localSlot,int appearance)
    {
        if(localSlot<0||localSlot>=HumanPlayers)throw new ArgumentOutOfRangeException(nameof(localSlot));
        if(appearance is <0 or >7)throw new ArgumentOutOfRangeException(nameof(appearance));
        if(IsEnded)return;
        _session.OnMessage(_context,_context.Players[localSlot],new InboundMessage("appearance",
            JsonSerializer.SerializeToElement(new {index=appearance}),null,_context.Now));
    }

    public void Tick()
    {
        if (IsEnded) return;
        _context.Tick++;
        _session.OnTick(_context);
        if (IsEnded && Result is { } result) _session.OnMatchEnded(_context, result);
    }

    public void RequestStage() => _session.OnMessage(_context, _context.Players[0], new InboundMessage("stage", default, null, _context.Now));

    private void Receive(string type, object? payload)
    {
        // Serialize once into an owned, immutable document. Local presentation can
        // retain its elements directly instead of encoding a string and parsing it again.
        var message = JsonSerializer.SerializeToElement(new { t = type, k = _context.Tick, d = payload }, RealtimeJson.Default);
        string? json=Message is null?null:message.GetRawText();
        if (type == "stage") {StageMessage=message;_stageJson=json;}
        if (type == "state") {StateMessage=message;_stateJson=json;}
        StructuredMessage?.Invoke(message);
        if(json is not null)Message?.Invoke(json);
    }

    private sealed class LocalContext : ISessionContext
    {
        private readonly Action<string, object?> _emit;
        public LocalContext(MatchOptions options, Action<string, object?> emit)
        {
            _emit = emit;
            Settings = options.ToSettings();
            Random = new Random(options.Seed);
            Players = Enumerable.Range(0, options.HumanPlayers).Select(i => new RealtimePlayer($"local-{i}", $"P{i + 1}", i, new Dictionary<string, string>())).ToArray();
        }
        public string LobbyId => "local";
        public string GameId => "blast";
        public LobbyState State { get; set; } = LobbyState.Open;
        public IReadOnlyDictionary<string, string> Settings { get; }
        public IReadOnlyList<RealtimePlayer> Players { get; }
        public Dictionary<string, long> Sequences { get; } = [];
        public bool IsConnected(string playerId) => Players.Any(p => p.PlayerId == playerId);
        public long? LastSequence(string playerId) => Sequences.TryGetValue(playerId, out var sequence) ? sequence : null;
        public long Tick { get; set; }
        public TimeSpan TickDelta => TimeSpan.FromSeconds(1d / TickRate);
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch + Elapsed;
        public TimeSpan Elapsed => TimeSpan.FromSeconds((double)Tick / TickRate);
        public Random Random { get; }
        public MatchResult? Result { get; private set; }
        public void Send<T>(string playerId, string type, T payload) => _emit(type, payload);
        public void Broadcast<T>(string type, T payload, string? exceptPlayerId = null) => _emit(type, payload);
        public void SendBinary(string playerId, string type, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public void BroadcastBinary(string type, ReadOnlySpan<byte> payload, string? exceptPlayerId = null) => throw new NotSupportedException();
        public void EndMatch(MatchResult result)
        {
            if (State == LobbyState.Ended) return;
            Result = result;
            State = LobbyState.Ended;
            _emit("sys.ended", new { reason = result.Reason, winners = result.Winners, details = result.Details });
        }
        public void Kick(string playerId, string reason) => throw new NotSupportedException("Local players cannot be kicked.");
    }
}

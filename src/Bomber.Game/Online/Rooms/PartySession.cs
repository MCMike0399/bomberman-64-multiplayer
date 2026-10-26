using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation;
using Burbuja.Realtime.Abstractions;

namespace Bomber.Game.Online.Rooms;

/// <summary>A persistent room owns leadership and successive matches; the kernel owns connections.</summary>
public sealed class PartySession(GameSessionOptions options, int minimumHumans = 1) : GameSession
{
    private readonly Dictionary<string, RealtimePlayer> _members = [];
    private readonly Dictionary<string, int> _appearances = [];
    private readonly Dictionary<string, long> _joined = [];
    private long _joinSequence;
    private BlastSession? _match;
    private IReadOnlyDictionary<string,string> _settings=options.Settings;
    private string? _leader;
    private string _phase = "lobby";
    private int _matchNumber;
    private bool _requested;
    private DateTimeOffset _started, _returnAt;
    private MatchResult? _result, _pendingResult;
    private int Bots => _settings.TryGetValue("bots", out var text) && int.TryParse(text, out var count) ? Math.Clamp(count, 0, 7) : 0;
    public override int? MinPlayersToStart => 1;

    public override void OnPlayerJoined(ISessionContext context, RealtimePlayer player)
    {
        _members[player.PlayerId] = player;
        _joined.TryAdd(player.PlayerId, ++_joinSequence);
        Elect(context);
        Publish(context);
        if (_phase == "playing") _match!.OnPlayerJoined(new MatchContext(this, context), player);
        foreach (var member in _members.Values)
            context.Send(player.PlayerId, "appearance", new { playerId = member.PlayerId, index = _appearances.GetValueOrDefault(member.PlayerId, member.Slot) });
        if (_phase == "results" && _result is {} result) SendResult(context, result, player.PlayerId);
    }
    public override void OnPlayerLeft(ISessionContext context, RealtimePlayer player, LeaveReason reason)
    {
        _members.Remove(player.PlayerId); _appearances.Remove(player.PlayerId);
        _joined.Remove(player.PlayerId);
        if (_phase == "playing") _match!.OnPlayerLeft(new MatchContext(this, context), player, reason);
        Elect(context); CancelInvalidStart(context); Publish(context);
    }
    public override void OnPlayerConnectionChanged(ISessionContext context, RealtimePlayer player, bool connected)
    {
        Elect(context); CancelInvalidStart(context); Publish(context);
        if (_phase == "playing") _match!.OnPlayerConnectionChanged(new MatchContext(this, context), player, connected);
        if (connected && _phase == "results" && _result is {} result) SendResult(context, result, player.PlayerId);
    }
    private void Elect(ISessionContext context)
    {
        if (_leader is not null && _members.ContainsKey(_leader) && context.IsConnected(_leader)) return;
        _leader = _members.Values.OrderBy(p => _joined[p.PlayerId]).FirstOrDefault(p => context.IsConnected(p.PlayerId))?.PlayerId;
    }
    private bool EnoughPlayers(ISessionContext context) => _members.Keys.Count(context.IsConnected) >= minimumHumans && _members.Keys.Count(context.IsConnected) + Bots >= 2;
    private void CancelInvalidStart(ISessionContext context)
    {
        if (_requested && (_leader is null || !EnoughPlayers(context))) { _requested = false; _phase = "lobby"; }
    }
    public override void OnMatchStarted(ISessionContext context)
    {
        // Kernel ticks enable the room. Only an authenticated leader's command enables a match.
        if (_requested) Start(context);
        else Publish(context);
    }
    public override void OnMessage(ISessionContext context, RealtimePlayer player, InboundMessage message)
    {
        if (!_members.ContainsKey(player.PlayerId)) return;
        if(message.Type=="party.settings")
        {
            if(message.Format!=FrameFormat.Text||player.PlayerId!=_leader||_phase!="lobby"||!context.IsConnected(player.PlayerId))return;
            if(RoomRules.TryUpdate(_settings,message.Data,out var settings)){_settings=settings;Publish(context);}
            else context.Send(player.PlayerId,"party.error",new {message="Invalid room settings. No rules were changed."});
            return;
        }
        if (message.Type == "party.start")
        {
            if (message.Format != FrameFormat.Text || player.PlayerId != _leader || _phase != "lobby" || !context.IsConnected(player.PlayerId) || !EnoughPlayers(context)) return;
            if (context.State == LobbyState.Running) Start(context);
            else { _requested = true; _phase = "starting"; Publish(context); }
            return;
        }
        if (message.Type == "appearance" && message.Format == FrameFormat.Text && message.Data.ValueKind == JsonValueKind.Object &&
            message.Data.TryGetProperty("index", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int index) && index is >= 0 and <= 7)
        {
            _appearances[player.PlayerId] = index;
            if (_phase != "playing") context.Broadcast("appearance", new { playerId = player.PlayerId, index });
        }
        if (_phase == "playing") _match!.OnMessage(new MatchContext(this, context), player, message);
    }
    private void Start(ISessionContext context)
    {
        _requested = false; _phase = "playing"; _matchNumber++; _started = context.Now; _result = _pendingResult = null;
        _match = new BlastSession(options with { Settings = _settings });
        Publish(context);
        var initializing = new MatchContext(this, context, true);
        foreach (var player in _members.Values.OrderBy(p => p.Slot))
        {
            _match.OnPlayerJoined(initializing, player);
            _match.OnMessage(initializing, player, new("appearance", JsonSerializer.SerializeToElement(new { index = _appearances.GetValueOrDefault(player.PlayerId, player.Slot) }), null, context.Now));
        }
        _match.OnMatchStarted(new MatchContext(this, context));
    }
    public override void OnTick(ISessionContext context)
    {
        if (_phase == "playing")
        {
            _match!.OnTick(new MatchContext(this, context));
            if (_pendingResult is {} result)
            {
                _pendingResult = null; _result = result; _phase = "results"; _returnAt = context.Now + TimeSpan.FromSeconds(5);
                _match.OnMatchEnded(new MatchContext(this, context), result);
                Publish(context); SendResult(context, result);
            }
        }
        else if (_phase == "results" && context.Now >= _returnAt)
        { _phase = "lobby"; _match = null; Publish(context); }
    }
    private void Publish(ISessionContext context) => context.Broadcast("party.status", new
    {
        leaderId = _leader, phase = _phase, match = _matchNumber,
        minimumHumans, hosting = "cluster",
        canStart = _phase == "lobby" && EnoughPlayers(context), bots = Bots, settings = _settings,
        players = _members.Values.OrderBy(p => p.Slot).Select(p => new { playerId = p.PlayerId, displayName = PlayerNames.Display(p.DisplayName,p.Slot), slot = p.Slot, connected = context.IsConnected(p.PlayerId) }).ToArray()
    });
    private static void SendResult(ISessionContext context, MatchResult result, string? recipient = null)
    {
        var payload = new { reason = result.Reason, winners = result.Winners, details = result.Details };
        if (recipient is null) context.Broadcast("party.result", payload); else context.Send(recipient, "party.result", payload);
    }
    private sealed class MatchContext(PartySession owner, ISessionContext room, bool initializing = false) : ISessionContext
    {
        public string LobbyId => room.LobbyId;
        public string GameId => room.GameId;
        public LobbyState State => initializing ? LobbyState.Open : LobbyState.Running;
        public IReadOnlyDictionary<string, string> Settings => owner._settings;
        public IReadOnlyList<RealtimePlayer> Players => room.Players;
        public bool IsConnected(string id) => room.IsConnected(id);
        public long? LastSequence(string id) => room.LastSequence(id);
        public long Tick => room.Tick;
        public TimeSpan TickDelta => room.TickDelta;
        public DateTimeOffset Now => room.Now;
        public TimeSpan Elapsed => room.Now - owner._started;
        public Random Random => room.Random;
        public void Send<T>(string id, string type, T payload) => room.Send(id, type, payload);
        public void Broadcast<T>(string type, T payload, string? exceptPlayerId = null) => room.Broadcast(type, payload, exceptPlayerId);
        public void SendBinary(string id, string type, ReadOnlySpan<byte> payload) => room.SendBinary(id, type, payload);
        public void BroadcastBinary(string type, ReadOnlySpan<byte> payload, string? exceptPlayerId = null) => room.BroadcastBinary(type, payload, exceptPlayerId);
        public void EndMatch(MatchResult result) => owner._pendingResult ??= result;
        public void Kick(string id, string reason) => room.Kick(id, reason);
    }
}

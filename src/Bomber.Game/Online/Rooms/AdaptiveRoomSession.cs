using System.Text.Json;
using System.Text.Json.Nodes;
using Burbuja.Realtime.Abstractions;
using Bomber.Game.Matches;

namespace Bomber.Game.Online.Rooms;

/// <summary>One persistent room/seat owns either a local simulation permit or a qualified peer lease.</summary>
public sealed class AdaptiveRoomSession : GameSession
{
    private readonly PartySession _server;
    private readonly PeerRoomSession _peer;
    private readonly RoomHostQuality _quality = new();
    private readonly Dictionary<string, RealtimePlayer> _members = [];
    private readonly Dictionary<string, long> _joined = [];
    private readonly Dictionary<string, int> _connections = [];
    private long _joinSerial;
    private readonly Dictionary<string, (long Window, int Bytes)> _budgets = [];
    private readonly bool _public;
    private JsonObject? _serverStatus, _peerStatus;
    private string? _active, _leader;
    private string _phase = "lobby", _notice = "";
    private string? _publishedStatus;
    private string _policy;
    private bool _selecting;
    private bool _startRequested;
    private int _matchSerial;
    private DateTimeOffset? _countdown;
    private string[] _countdownRoster = [];
    private DateTimeOffset _retryAt;
    private int PeerEpoch => _peerStatus?["epoch"]?.GetValue<int>() ?? 0;

    public AdaptiveRoomSession(GameSessionOptions options, bool publicRoom = false)
    {
        if (!RoomRules.TryUpdate(new MatchOptions().ToSettings(), JsonSerializer.SerializeToElement(options.Settings), out var validated))
            throw new ArgumentException("Invalid room settings.", nameof(options));
        options = options with { Settings = validated };
        _public = publicRoom;
        _policy = publicRoom ? "auto" : options.Settings.GetValueOrDefault("hosting", "auto");
        if (_policy is not ("auto" or "server" or "peer")) throw new ArgumentException("Unknown hosting preference.");
        _server = new PartySession(options, publicRoom ? 2 : 1);
        _peer = new PeerRoomSession(options, stableRoster: true);
    }
    public override int? MinPlayersToStart => 1;
    public override bool AcceptsNewPlayers => _public ? _active is null && _phase == "lobby" : _active != "peer" || _phase != "playing";

    public override void OnPlayerJoined(ISessionContext c, RealtimePlayer p)
    {
        // A reservation made during the countdown can be claimed after gameplay starts.
        if (_public && !AcceptsNewPlayers && !_members.ContainsKey(p.PlayerId))
        { c.Kick(p.PlayerId, "This Quick Play match has started. Use Play online to find another lobby."); return; }
        CancelPendingStart();
        _members[p.PlayerId] = p; _joined.TryAdd(p.PlayerId, ++_joinSerial); ElectLeader(c);
        _connections[p.PlayerId] = _connections.GetValueOrDefault(p.PlayerId) + 1;
        RefreshPublicCountdown(c);
        _server.OnPlayerJoined(Wrap(c, false), p); _peer.OnPlayerJoined(Wrap(c, true), p);
        Publish(c);
    }
    public override void OnPlayerLeft(ISessionContext c, RealtimePlayer p, LeaveReason reason)
    {
        if (!_members.ContainsKey(p.PlayerId)) return;
        CancelPendingStart();
        _members.Remove(p.PlayerId); _joined.Remove(p.PlayerId); _quality.Remove(p.PlayerId); _budgets.Remove(p.PlayerId); ElectLeader(c);
        _connections.Remove(p.PlayerId);
        RefreshPublicCountdown(c);
        _server.OnPlayerLeft(Wrap(c, false), p, reason); _peer.OnPlayerLeft(Wrap(c, true), p, reason);
        Publish(c);
    }
    public override void OnPlayerConnectionChanged(ISessionContext c, RealtimePlayer p, bool connected)
    {
        if (!_members.ContainsKey(p.PlayerId)) return;
        CancelPendingStart();
        if (connected) _connections[p.PlayerId] = _connections.GetValueOrDefault(p.PlayerId) + 1;
        _quality.Remove(p.PlayerId); ElectLeader(c);
        RefreshPublicCountdown(c, reconnect: connected);
        _server.OnPlayerConnectionChanged(Wrap(c, false), p, connected);
        _peer.OnPlayerConnectionChanged(Wrap(c, true), p, connected);
        Publish(c);
    }
    public override void OnMatchStarted(ISessionContext c)
    { _server.OnMatchStarted(Wrap(c, false)); _peer.OnMatchStarted(Wrap(c, true)); }
    public override void OnMatchEnded(ISessionContext c, MatchResult result)
    { _server.OnMatchEnded(Wrap(c, false), result); _peer.OnMatchEnded(Wrap(c, true), result); Release(c); }
    public override void OnTick(ISessionContext c)
    {
        if (_active != "peer") _server.OnTick(Wrap(c, false));
        if (_active != "cluster") _peer.OnTick(Wrap(c, true));
        if (_active is null && _startRequested && c.Now >= _retryAt)
        { _retryAt = c.Now.AddSeconds(.5); Start(c); }
        if (!_public || _active is not null || _phase != "lobby") return;
        if (RefreshPublicCountdown(c)) Publish(c);
        if (_countdown is { } deadline && c.Now >= deadline && c.Now >= _retryAt)
        { _retryAt = c.Now.AddSeconds(2); Start(c); }
    }

    public override void OnMessage(ISessionContext c, RealtimePlayer p, InboundMessage message)
    {
        if (!_members.ContainsKey(p.PlayerId) || !c.IsConnected(p.PlayerId) || message.Format != FrameFormat.Text) return;
        if (message.Data.ValueKind != JsonValueKind.Object) return;
        long window = c.Now.ToUnixTimeSeconds();
        var previous = _budgets.GetValueOrDefault(p.PlayerId);
        int bytes = System.Text.Encoding.UTF8.GetByteCount(message.Data.GetRawText());
        int used = (previous.Window == window ? previous.Bytes : 0) + bytes;
        int limit = _active == "peer" && PeerHost(c)?.PlayerId == p.PlayerId
            ? PeerRoomSession.RelayByteLimit(_members.Keys.Count(id => id != p.PlayerId && c.IsConnected(id))) : 64 * 1024;
        _budgets[p.PlayerId] = (window, Math.Min(used, limit + 1));
        if (used > limit) return;
        if (message.Type == "peer.quality")
        {
            if (message.Data.ValueKind == JsonValueKind.Object && message.Data.TryGetProperty("epoch", out var epoch) &&
                epoch.ValueKind == JsonValueKind.Number && epoch.TryGetInt32(out int version) && version == PeerEpoch)
                _quality.Record(p.PlayerId, message.Data, _members.Keys.ToHashSet(StringComparer.Ordinal), c.Now);
            return;
        }
        if (message.Type == "party.start")
        { if (!_public && p.PlayerId == _leader && _active is null) { _startRequested = true; Start(c); } return; }
        if (message.Type == "party.settings")
        {
            if (_public)
            { c.Send(p.PlayerId, "party.error", new { message = "Quick Play uses shared rules. Create a private room to choose your own." }); return; }
            if (p.PlayerId != _leader || _active is not null) return;
            _countdown = null; _startRequested = false;
            _server.OnMessage(Wrap(c, false), p, message);
            _policy = _public ? "auto" : _serverStatus?["settings"]?["hosting"]?.GetValue<string>() ?? "auto";
            var peerHost = PeerHost(c);
            if (peerHost is not null) _peer.OnMessage(Wrap(c, true), peerHost, message);
            Publish(c); return;
        }
        if (message.Type == "appearance")
        { _server.OnMessage(Wrap(c, false), p, message); _peer.OnMessage(Wrap(c, true), p, message); return; }
        if (message.Type.StartsWith("peer.", StringComparison.Ordinal))
        { if (_policy != "server" || message.Type is "peer.hello" or "peer.heartbeat") _peer.OnMessage(Wrap(c, true), p, message); return; }
        if (_active == "cluster") _server.OnMessage(Wrap(c, false), p, message);
    }

    private void Start(ISessionContext c)
    {
        if (_active is not null || _leader is null || !Enough(c)) return;
        var players = Connected(c);
        var candidate = _policy == "server" ? null : _quality.Best(players, id => _peer.EligibleHost(c, id), c.Now);
        bool preferPeer = _policy == "peer" || !_public && candidate is { AllDirect: true } &&
            candidate.WorstRtt + 15 < _quality.ServerWorst(players, c.Now) * .8;
        if (!preferPeer && StartServer(c)) return;
        if (candidate is not null && c.TryReserveResource("peer-relay", 1))
        {
            _selecting = true;
            bool selected = _peer.SelectHost(Wrap(c, true), candidate.PlayerId);
            if (selected)
            {
                _active = "peer"; _notice = _public ? "Player hosted." : "Player hosted. The room owner still controls the rules.";
                _peer.OnMessage(Wrap(c, true), _members[candidate.PlayerId], new("party.start", JsonSerializer.SerializeToElement(new { }), null, c.Now));
            }
            _selecting = false;
            if (selected && _peerStatus?["phase"]?.GetValue<string>() == "playing")
            { _phase = "playing"; _countdown = null; _startRequested = false; Publish(c); return; }
            Release(c); _active = null;
        }
        if (preferPeer && _policy != "peer" && StartServer(c)) return;
        _notice = "Waiting for server capacity or a suitable player connection.";
        Publish(c);
    }
    private bool StartServer(ISessionContext c)
    {
        if (!c.TryReserveResource("simulation", 1)) return false;
        _active = "cluster"; _notice = "Server hosted."; _countdown = null;
        _server.OnMessage(Wrap(c, false), _members[_leader!], new("party.start", JsonSerializer.SerializeToElement(new { }), null, c.Now));
        if (_phase is "playing" or "starting") { _startRequested = false; return true; }
        Release(c); _active = null; return false;
    }
    private bool Enough(ISessionContext c)
    {
        int humans = Connected(c).Count;
        int bots = _serverStatus?["bots"]?.GetValue<int>() ?? 0;
        return humans >= (_public || _policy == "peer" ? 2 : 1) && humans + bots >= 2;
    }
    private List<string> Connected(ISessionContext c) => _members.Keys.Where(c.IsConnected).OrderBy(id => _joined[id]).ToList();
    private bool RefreshPublicCountdown(ISessionContext c, bool reconnect = false)
    {
        if (!_public || _active is not null || _phase != "lobby") return false;
        var players = Connected(c).ToArray();
        // Removing a disconnected seat later is cleanup, not another roster change.
        if (!reconnect && players.SequenceEqual(_countdownRoster) && (players.Length < 2 || _countdown is not null)) return false;
        _countdownRoster = players;
        _countdown = players.Length >= 2 ? c.Now.AddMinutes(1) : null;
        _retryAt = default;
        _notice = "";
        return true;
    }
    private void ElectLeader(ISessionContext c)
    {
        if (_leader is null || !_members.ContainsKey(_leader) || !c.IsConnected(_leader))
        { _leader = Connected(c).FirstOrDefault(); _startRequested = false; }
    }
    private RealtimePlayer? PeerHost(ISessionContext c) => _peerStatus?["leaderId"]?.GetValue<string>() is { } id && c.IsConnected(id) ? _members.GetValueOrDefault(id) : null;
    private void Release(ISessionContext c)
    { c.TryReserveResource("simulation", 0); c.TryReserveResource("peer-relay", 0); }
    private void CancelPendingStart()
    {
        if (_startRequested) _notice = "The roster changed. Start when everyone is ready.";
        _startRequested = false;
    }
    private Context Wrap(ISessionContext c, bool peer) => new(this, c, peer);

    private void Publish(ISessionContext c)
    {
        if (_selecting || _serverStatus is null) return;
        var source = _active == "peer" ? _peerStatus : _serverStatus;
        if (source is null) return;
        var data = (JsonObject)source.DeepClone();
        data["leaderId"] = _leader;
        data["hostId"] = _active == "peer" ? _peerStatus?["leaderId"]?.DeepClone() : null;
        data["hosting"] = _active ?? "auto";
        data["hostingPolicy"] = _policy;
        data["adaptive"] = true;
        data["epoch"] = PeerEpoch;
        data["phase"] = _phase;
        data["match"] = _matchSerial;
        data["canStart"] = !_public && _active is null && _phase == "lobby" && Enough(c);
        data["minimumHumans"] = _public || _policy == "peer" ? 2 : 1;
        data["publicRoom"] = _public;
        data["startAt"] = _countdown?.ToUnixTimeMilliseconds();
        data["notice"] = _notice;
        var appearances = (_peerStatus?["players"] as JsonArray)?.OfType<JsonObject>()
            .ToDictionary(p => p["playerId"]!.GetValue<string>(), p => p["appearance"]!.GetValue<int>()) ?? [];
        data["players"] = JsonSerializer.SerializeToNode(_members.Values.OrderBy(p => p.Slot).Select(p => new
        {
            playerId = p.PlayerId, displayName = PlayerNames.Display(p.DisplayName, p.Slot), slot = p.Slot,
            connected = c.IsConnected(p.PlayerId), appearance = appearances.GetValueOrDefault(p.PlayerId, p.Slot),
            connection = _connections.GetValueOrDefault(p.PlayerId),
        }));
        string serialized = data.ToJsonString();
        if (serialized == _publishedStatus) return;
        _publishedStatus = serialized;
        c.Broadcast("party.status", data);
    }
    private void Observe<T>(ISessionContext c, bool peer, string type, T payload)
    {
        if (type != "party.status") return;
        var data = JsonSerializer.SerializeToNode(payload)!.AsObject();
        if (peer) _peerStatus = data; else _serverStatus = data;
        if (_active == (peer ? "peer" : "cluster"))
        {
            string phase = data["phase"]!.GetValue<string>();
            if (phase == "playing" && _phase != "playing") _matchSerial++;
            _phase = phase;
            if (!_selecting && _phase is "results" or "lobby")
            {
                Release(c);
                if (_phase == "lobby") { _active = null; _notice = data["notice"]?.GetValue<string>() ?? "Ready for another match."; }
            }
        }
        Publish(c);
    }
    private sealed class Context(AdaptiveRoomSession owner, ISessionContext inner, bool peer) : ISessionContext
    {
        public string LobbyId => inner.LobbyId; public string GameId => inner.GameId;
        public LobbyState State => inner.State;
        public IReadOnlyDictionary<string, string> Settings => inner.Settings;
        public IReadOnlyList<RealtimePlayer> Players => inner.Players;
        public bool IsConnected(string id) => inner.IsConnected(id);
        public long? LastSequence(string id) => inner.LastSequence(id);
        public long Tick => inner.Tick; public TimeSpan TickDelta => inner.TickDelta;
        public DateTimeOffset Now => inner.Now; public TimeSpan Elapsed => inner.Elapsed;
        public Random Random => inner.Random;
        public bool TryReserveResource(string name, int units) => inner.TryReserveResource(name, units);
        public void Send<T>(string id, string type, T data)
        {
            if (type == "party.status") { owner.Observe(inner, peer, type, data); return; }
            if (type is "peer.signal" or "party.error" || owner._active == (peer ? "peer" : "cluster") || !peer && owner._active is null)
                inner.Send(id, type, data);
        }
        public void Broadcast<T>(string type, T data, string? exceptPlayerId = null)
        {
            if (type == "party.status") { owner.Observe(inner, peer, type, data); return; }
            if (owner._active == (peer ? "peer" : "cluster") || !peer && owner._active is null) inner.Broadcast(type, data, exceptPlayerId);
        }
        public void SendBinary(string id, string type, ReadOnlySpan<byte> data) => inner.SendBinary(id, type, data);
        public void BroadcastBinary(string type, ReadOnlySpan<byte> data, string? exceptPlayerId = null) => inner.BroadcastBinary(type, data, exceptPlayerId);
        public void EndMatch(MatchResult result) => inner.EndMatch(result);
        public void Kick(string id, string reason) => inner.Kick(id, reason);
    }
}

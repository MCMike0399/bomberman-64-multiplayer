using System.Text.Json;
using Burbuja.Realtime.Abstractions;
using Bomber.Game.Matches;

namespace Bomber.Game.Online.Rooms;

/// <summary>Authenticated membership, host leases and bounded signalling/relay. Never simulates the game.</summary>
public sealed class PeerRoomSession(GameSessionOptions options, bool stableRoster = false, PeerHostLease? lease = null, PeerRoomTrace? trace = null) : GameSession
{
    internal static int RelayByteLimit(int recipients) => Math.Clamp(recipients, 1, 7) * 2 * 1024 * 1024;
    private readonly Dictionary<string, RealtimePlayer> _members = [];
    private readonly Dictionary<string, long> _order = [];
    private readonly Dictionary<string, int> _appearances = [];
    private readonly HashSet<string> _expiredHosts = [];
    private readonly Dictionary<string, long> _budgetNotices = [];
    private void Trace(string text) => (trace ?? PeerRoomTrace.None).Write($"peer-room {options.LobbyId} epoch={_epoch} phase={_phase} host={_host ?? "-"} {text}");
    private readonly HashSet<string> _ready = [];
    private readonly HashSet<string> _incompatible = [];
    private readonly Dictionary<string, DateTimeOffset> _handshakes = [];
    private readonly Dictionary<string, (long Window, int Bytes)> _budgets = [];
    private IReadOnlyDictionary<string, string> _settings = options.Settings;
    private long _serial;
    private int _epoch = 1, _match;
    private string? _host;
    private string _phase = "lobby", _notice = "";
    private DateTimeOffset _lease, _returnAt;
    public override int? MinPlayersToStart => 1;
    public override bool AcceptsNewPlayers => !stableRoster || _phase != "playing";
    internal bool EligibleHost(ISessionContext c, string id) => _members.ContainsKey(id) && c.IsConnected(id) && _ready.Contains(id) && !_expiredHosts.Contains(id);
    internal bool SelectHost(ISessionContext c, string id)
    {
        if (_phase != "lobby" || !EligibleHost(c, id)) return false;
        if (_host != id) { _host = id; _epoch++; }
        _lease = c.Now; Publish(c); return true;
    }

    public override void OnPlayerJoined(ISessionContext c, RealtimePlayer p)
    {
        if (stableRoster && _phase == "playing")
        { c.Kick(p.PlayerId, "This match has started. Join again when the room returns to its lobby."); return; }
        _members[p.PlayerId] = p; _order.TryAdd(p.PlayerId, ++_serial);
        _handshakes[p.PlayerId] = c.Now;
        // A changed roster starts a new epoch; existing rounds cannot admit uninitialised peers.
        if (_phase == "playing") Reset("A player joined. Start a fresh match when everyone is ready.");
        Elect(c); Publish(c);
    }
    public override void OnPlayerLeft(ISessionContext c, RealtimePlayer p, LeaveReason reason)
    {
        _members.Remove(p.PlayerId); _order.Remove(p.PlayerId); _appearances.Remove(p.PlayerId);
        _expiredHosts.Remove(p.PlayerId); _budgets.Remove(p.PlayerId);
        _ready.Remove(p.PlayerId); _handshakes.Remove(p.PlayerId); _incompatible.Remove(p.PlayerId);
        Changed(c, p.PlayerId, false);
    }
    public override void OnPlayerConnectionChanged(ISessionContext c, RealtimePlayer p, bool connected)
    {
        if (connected) { _expiredHosts.Remove(p.PlayerId); _handshakes[p.PlayerId] = c.Now; }
        else _ready.Remove(p.PlayerId);
        Changed(c, p.PlayerId, connected);
    }
    private void Changed(ISessionContext c, string id, bool connected)
    {
        Trace($"member player={id} connected={connected}");
        if (_phase == "playing" && (!stableRoster || id == _host)) Reset(connected
            ? "A player reconnected. The interrupted match was discarded; start again with the restored roster."
            : id == _host ? "Host left. A new host is taking over; the interrupted match was discarded."
            : "A player disconnected. The interrupted match was discarded.");
        Elect(c); Publish(c);
    }
    private void Elect(ISessionContext c)
    {
        if (_host is not null && _members.ContainsKey(_host) && c.IsConnected(_host) && !_expiredHosts.Contains(_host)) return;
        var next = _members.Keys.Where(id => c.IsConnected(id) && !_expiredHosts.Contains(id)).OrderBy(id => _order[id]).FirstOrDefault();
        if (next != _host) { Trace($"elect next={next ?? "-"}"); _host = next; _epoch++; }
        _lease = c.Now;
    }
    private void Reset(string notice) { _phase = "lobby"; _epoch++; _notice = notice; }
    private bool Enough(ISessionContext c) => _members.Keys.Count(c.IsConnected) >= 2 && _host is not null && _members.Keys.Where(c.IsConnected).All(_ready.Contains);
    public override void OnMatchStarted(ISessionContext c) => Publish(c);

    public override void OnMessage(ISessionContext c, RealtimePlayer p, InboundMessage message)
    {
        if (!_members.ContainsKey(p.PlayerId) || !c.IsConnected(p.PlayerId) || message.Format != FrameFormat.Text || message.Data.ValueKind != JsonValueKind.Object) return;
        var d = message.Data;
        var window = c.Now.ToUnixTimeSeconds();
        var budget = _budgets.GetValueOrDefault(p.PlayerId);
        int bytes = System.Text.Encoding.UTF8.GetByteCount(d.GetRawText());
        int total = (budget.Window == window ? budget.Bytes : 0) + bytes;
        // The host uploads one copy per relay recipient. A room-wide two-MiB ceiling
        // starved seven receivers for half of every second on large stages.
        int limit = p.PlayerId == _host ? RelayByteLimit(_members.Keys.Count(id => id != p.PlayerId && c.IsConnected(id))) : 64 * 1024;
        _budgets[p.PlayerId] = (window, Math.Min(total, limit + 1));
        if (total > limit)
        {
            if (_budgetNotices.GetValueOrDefault(p.PlayerId) != window) { _budgetNotices[p.PlayerId] = window; Trace($"budget-drop player={p.PlayerId} type={message.Type} bytes={total} limit={limit}"); }
            return;
        }
        if (message.Type == "peer.hello")
        {
            if (d.TryGetProperty("version",out var version) && version.ValueKind==JsonValueKind.Number && version.TryGetInt32(out int v) && v==OnlineProtocol.Revision)
            { _ready.Add(p.PlayerId); _incompatible.Remove(p.PlayerId); _handshakes.Remove(p.PlayerId); if(p.PlayerId==_host)_lease=c.Now; Publish(c); }
            else
            {
                _ready.Remove(p.PlayerId); _incompatible.Add(p.PlayerId); _handshakes.TryAdd(p.PlayerId,c.Now);
                if(_phase=="playing")Reset(OnlineProtocol.UpdateMessage);
                c.Send(p.PlayerId,"party.error",new{message=OnlineProtocol.UpdateMessage});Publish(c);
            }
            return;
        }
        if (message.Type == "appearance" && d.TryGetProperty("index", out var index) && index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out int suit) && suit is >= 0 and <= 7)
        { _appearances[p.PlayerId] = suit; Publish(c); return; }
        if (message.Type == "party.settings" && p.PlayerId == _host && _phase == "lobby")
        {
            if (RoomRules.TryUpdate(_settings, d, out var rules)) { _settings = rules; Publish(c); }
            return;
        }
        if (message.Type == "party.start" && p.PlayerId == _host && _phase == "lobby" && Enough(c))
        { _phase = "playing"; _match++; _epoch++; _lease = c.Now; _notice = ""; Publish(c); return; }
        if (!d.TryGetProperty("epoch", out var epoch) || epoch.ValueKind != JsonValueKind.Number || !epoch.TryGetInt32(out int generation) || generation != _epoch) return;
        if (message.Type == "peer.heartbeat" && _ready.Contains(p.PlayerId))
        {
            _expiredHosts.Remove(p.PlayerId);
            if (_host is null) { Elect(c); Publish(c); }
            if (p.PlayerId == _host) _lease = c.Now;
            return;
        }
        if (message.Type == "peer.finished" && p.PlayerId == _host && _phase == "playing" && d.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object && bytes < 8192)
        { _phase = "results"; _returnAt = c.Now.AddSeconds(5); Publish(c); c.Broadcast("party.result", result); return; }
        if (message.Type is not ("peer.signal" or "peer.frame" or "peer.state") || !d.TryGetProperty("to", out var to) || to.ValueKind != JsonValueKind.String) return;
        var target = to.GetString()!;
        if (target == p.PlayerId || !_members.ContainsKey(target) || !c.IsConnected(target) ||
            (p.PlayerId != _host && target != _host && !(stableRoster && _phase == "lobby" && message.Type == "peer.signal"))) return;
        if (!d.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return;
        if (message.Type == "peer.signal")
        {
            if (bytes > 16384) return;
        }
        else
        {
            if (_phase != "playing" || !payload.TryGetProperty("t", out var type) || type.ValueKind != JsonValueKind.String) return;
            if (p.PlayerId == _host ? type.GetString() is not ("stage" or "state" or "appearance") : type.GetString() != "input" || bytes > 2048) return;
            if (message.Type == "peer.state" && (p.PlayerId != _host || type.GetString() != "state")) return;
        }
        // Sender identity is assigned here, never accepted from the supplied payload.
        string delivery = message.Type != "peer.signal" && payload.TryGetProperty("t", out var frameType) && frameType.GetString() == "state" ? "peer.state" : message.Type;
        c.Send(target, delivery, new { from = p.PlayerId, epoch = _epoch, payload });
    }
    public override void OnTick(ISessionContext c)
    {
        foreach (var pending in _handshakes.Where(p=>c.IsConnected(p.Key)&&!_ready.Contains(p.Key)&&c.Now-p.Value>TimeSpan.FromSeconds(10)).Select(p=>p.Key).ToArray())
        { _handshakes.Remove(pending); Trace($"handshake-kick player={pending}"); c.Kick(pending,OnlineProtocol.UpdateMessage); }
        if (_host is not null && _ready.Contains(_host) && c.Now - _lease > (lease ?? PeerHostLease.Production).Silence)
        {
            Trace($"lease-expired silence={(c.Now - _lease).TotalSeconds:F1}s limit={(lease ?? PeerHostLease.Production).Silence.TotalSeconds:F0}s");
            if (_host is not null) _expiredHosts.Add(_host);
            Reset("Host stopped responding. A new host is taking over; the interrupted match was discarded.");
            Elect(c); Publish(c);
        }
        if (_phase == "results" && c.Now >= _returnAt) { Reset("Ready for another match."); Publish(c); }
    }
    private void Publish(ISessionContext c) => c.Broadcast("party.status", new
    {
        leaderId = _host, phase = _phase, match = _match, canStart = _phase == "lobby" && Enough(c),
        bots = int.TryParse(_settings.GetValueOrDefault("bots"), out int bots) ? bots : 0,
        settings = _settings, hosting = "peer", minimumHumans = 2, epoch = _epoch, seed = options.Seed,
        requiredOnlineProtocol=OnlineProtocol.Revision,
        notice = _incompatible.Any(c.IsConnected) ? OnlineProtocol.UpdateMessage : _phase == "lobby" && _members.Keys.Where(c.IsConnected).Any(id=>!_ready.Contains(id)) ? "Checking peer compatibility..." : _notice,
        players = _members.Values.OrderBy(p => _order[p.PlayerId]).Select(p => new
        { playerId = p.PlayerId, displayName = PlayerNames.Display(p.DisplayName, p.Slot), slot = p.Slot, connected = c.IsConnected(p.PlayerId), appearance = _appearances.GetValueOrDefault(p.PlayerId, p.Slot) }).ToArray()
    });
}

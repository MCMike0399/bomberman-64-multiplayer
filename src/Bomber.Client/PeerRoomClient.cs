using System.Text.Json;
using Bomber.Game;
using Burbuja.Realtime.Abstractions;

namespace Bomber.Client;

/// <summary>Single-thread peer session. The cluster elects authority; direct links and relay carry identical frames.</summary>
internal sealed class PeerRoomClient(string playerId, string lobbyId, Func<IPeerLink>? factory,
    Action<string, object> sendControl, Action<JsonElement> emit, Func<byte[]> input) : IDisposable
{
    private readonly Dictionary<string, IPeerLink> _links = [];
    private readonly Dictionary<string, DateTimeOffset> _lastInput = [];
    private readonly Dictionary<string, long> _directSeen = [];
    private PartyStatus? _party;
    private PeerMatch? _authority;
    private string[] _players = [];
    private int _epoch = -1;
    private long _lastTick = -1;
    private double _accumulator, _heartbeat, _probe;
    private bool _disposed;
    private long _lastAdvance = System.Diagnostics.Stopwatch.GetTimestamp();
    public long DirectSent { get; private set; }
    public long DirectReceived { get; private set; }
    private bool Direct(string id) => _links.TryGetValue(id, out var link) && link.Connected && _directSeen.TryGetValue(id, out var seen) && System.Diagnostics.Stopwatch.GetElapsedTime(seen).TotalSeconds < 2;
    public string Transport => _links.Count > 0 && _links.Keys.All(Direct) ? "Direct P2P" :
        _links.Keys.Any(Direct) ? "P2P + relay" : "Relay";

    public bool Handle(JsonElement message)
    {
        string? type = message.GetProperty("t").GetString();
        if (type == "party.result") return !PeerFrameRules.Valid(message);
        if (type == "party.status")
        {
            var data = message.GetProperty("d"); var party = PartyStatus.Parse(data);
            emit(message); // UI resets the old match before any stage/snapshot for this epoch.
            _party = party;
            if (party.Epoch == _epoch) return true;
            DisposeLinks(); _authority = null; _lastInput.Clear(); _directSeen.Clear(); _lastTick = -1; _epoch = party.Epoch; _accumulator = _heartbeat = _probe = 0;
            _lastAdvance = System.Diagnostics.Stopwatch.GetTimestamp();
            var members = data.GetProperty("players").EnumerateArray().Where(p => p.GetProperty("connected").GetBoolean()).ToArray();
            _players = members.Select(p => p.GetProperty("playerId").GetString()!).ToArray();
            if (party.Phase != "playing" || party.LeaderId is null) return true;
            var targets = playerId == party.LeaderId ? _players.Where(id => id != playerId) : [party.LeaderId];
            foreach (var target in targets)
            {
                if (factory is null) continue;
                var link = factory(); _links[target] = link;
                int epoch = _epoch;
                link.Signal += signal => sendControl("peer.signal", new { epoch, to = target, payload = signal });
                link.Received += packet => Receive(target, epoch, packet, direct: true);
                link.Start(playerId == party.LeaderId);
            }
            if (playerId == party.LeaderId)
            {
                var roster = members.Select(p => new RealtimePlayer(p.GetProperty("playerId").GetString()!, p.GetProperty("displayName").GetString()!, p.GetProperty("slot").GetInt32(), new Dictionary<string, string>())).ToArray();
                var appearances = members.ToDictionary(p => p.GetProperty("playerId").GetString()!, p => p.GetProperty("appearance").GetInt32());
                _authority = new PeerMatch(lobbyId, party.Settings!, data.GetProperty("seed").GetInt32() + party.Match, roster, appearances, Publish);
            }
            return true;
        }
        if (type is not ("peer.signal" or "peer.frame")) return false;
        var d = message.GetProperty("d");
        if (d.GetProperty("epoch").GetInt32() != _epoch) return true;
        string from = d.GetProperty("from").GetString()!;
        if (type == "peer.signal")
        { if (_links.TryGetValue(from, out var link)) link.AcceptSignal(d.GetProperty("payload")); }
        else Receive(from, _epoch, JsonSerializer.SerializeToUtf8Bytes(d.GetProperty("payload")));
        return true;
    }
    public void Advance()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double delta = System.Diagnostics.Stopwatch.GetElapsedTime(_lastAdvance, now).TotalSeconds;
        _lastAdvance = now;
        if (_disposed || _party is null) return;
        _heartbeat += delta;
        if (_heartbeat >= 1) { _heartbeat = 0; sendControl("peer.heartbeat", new { epoch = _epoch }); }
        if (_party.Phase != "playing") return;
        // Network authority follows elapsed real time, independent of rendering, Movie Maker or headless frame speed.
        foreach (var link in _links.Values) link.Poll();
        _probe += delta;
        if (_probe >= .5)
        {
            _probe = 0;
            foreach (var link in _links.Values.Where(l => l.Connected))
                link.Send(JsonSerializer.SerializeToUtf8Bytes(new { epoch = _epoch, ping = true }));
        }
        _accumulator += Math.Min(delta, 5d / LocalMatch.TickRate);
        while (_accumulator >= 1d / LocalMatch.TickRate)
        {
            foreach (var stale in _lastInput.Where(p => DateTimeOffset.UtcNow - p.Value > TimeSpan.FromMilliseconds(500)).Select(p => p.Key).ToArray())
            { _authority?.ReleaseInput(stale); _lastInput.Remove(stale); }
            var packet = input();
            if (_authority is not null) Receive(playerId, _epoch, packet);
            else if (_party.LeaderId is {} host) Send(host, packet);
            _authority?.Advance();
            _accumulator -= 1d / LocalMatch.TickRate;
        }
    }
    private void Publish(string? recipient, string type, object? data)
    {
        if (type == "peer.finished") { sendControl(type, new { epoch = _epoch, result = JsonSerializer.SerializeToElement(data, RealtimeJson.Default) }); return; }
        var frame = JsonSerializer.SerializeToElement(new { t = type, k = _authority?.Tick ?? 0, d = data }, RealtimeJson.Default);
        if (recipient == playerId) { emit(frame); return; }
        if (recipient is not null) Send(recipient, JsonSerializer.SerializeToUtf8Bytes(frame));
    }
    private void Send(string target, byte[] bytes)
    {
        // Generation is inside the encrypted data channel too, not just the signalling envelope.
        using var doc = JsonDocument.Parse(bytes);
        var payload = doc.RootElement;
        var direct = JsonSerializer.SerializeToUtf8Bytes(new { epoch = _epoch, payload });
        if (Direct(target) && _links[target].Send(direct)) { DirectSent++; return; }
        sendControl("peer.frame", new { epoch = _epoch, to = target, payload });
    }
    private void Receive(string from, int epoch, byte[] bytes, bool direct = false)
    {
        if (epoch != _epoch || _party?.Phase != "playing" || bytes.Length > 131072 || !_players.Contains(from)) return;
        if (playerId == _party.LeaderId && bytes.Length > 2048) return;
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            var frame = doc.RootElement;
            if (frame.TryGetProperty("epoch", out var generation))
            {
                if (generation.GetInt32() != _epoch) return;
                if (direct && frame.TryGetProperty("ping", out _))
                { if (_links.TryGetValue(from, out var source)) source.Send(JsonSerializer.SerializeToUtf8Bytes(new { epoch = _epoch, pong = true })); return; }
                if (direct && frame.TryGetProperty("pong", out _)) { _directSeen[from] = System.Diagnostics.Stopwatch.GetTimestamp(); return; }
                frame = frame.GetProperty("payload");
            }
            var type = frame.GetProperty("t").GetString();
            if (direct) DirectReceived++;
            if (playerId == _party.LeaderId)
            { if (type == "input" && bytes.Length < 2048) { _authority?.Receive(from, frame); _lastInput[from] = DateTimeOffset.UtcNow; } }
            else if (from == _party.LeaderId && type is "stage" or "state" or "appearance")
            {
                if (!PeerFrameRules.Valid(frame)) return;
                if (type == "state")
                {
                    long tick = frame.GetProperty("k").GetInt64();
                    if (tick <= _lastTick) return;
                    _lastTick = tick;
                }
                emit(frame);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { /* An invalid peer frame must not unwind the engine loop. */ }
    }
    private void DisposeLinks() { foreach (var link in _links.Values) link.Dispose(); _links.Clear(); }
    public void Dispose() { if (_disposed) return; _disposed = true; DisposeLinks(); _authority = null; }
}

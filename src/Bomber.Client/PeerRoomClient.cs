using System.Diagnostics;
using System.Text.Json;
using Bomber.Game;
using Burbuja.Realtime.Abstractions;

namespace Bomber.Client;

/// <summary>Polling-thread peer session. Membership grants authority; probes only inform its selection.</summary>
internal sealed class PeerRoomClient(string playerId, string lobbyId, Func<IPeerLink>? factory,
    Action<string, object> sendControl, Action<JsonElement> emit, Func<byte[]> input, Func<double>? serverRtt = null) : IDisposable
{
    private readonly Dictionary<string, IPeerLink> _links = [];
    private readonly Dictionary<string, DateTimeOffset> _lastInput = [];
    private readonly Dictionary<string, long> _directSeen = [];
    private readonly Dictionary<string, ProbeWindow> _directQuality = [], _relayQuality = [];
    private HashSet<string> _connected = [], _members = [];
    private PartyStatus? _party;
    private PeerMatch? _authority;
    private string[] _players = [];
    private string _mode = "";
    private int _epoch = -1, _qualitySamples;
    private long _lastTick = -1, _nonce;
    private double _accumulator, _heartbeat, _probe, _report, _frameGap;
    private bool _disposed, _stageReady;
    private JsonElement? _pendingState;
    private long _lastAdvance = Stopwatch.GetTimestamp();
    public long DirectSent { get; private set; }
    public long DirectReceived { get; private set; }
    public int QualitySamples => _qualitySamples;
    private bool Direct(string id) => _links.TryGetValue(id, out var link) && link.Connected &&
        _directSeen.TryGetValue(id, out var seen) && Stopwatch.GetElapsedTime(seen).TotalSeconds < 2 &&
        (_party?.Adaptive != true || !_relayQuality.TryGetValue(id, out var relay) || relay.Count < 3 ||
         !_directQuality.TryGetValue(id, out var direct) || direct.Count < 3 || direct.P95 <= relay.P95 + 20);
    public string Transport => _links.Count > 0 && _links.Keys.All(Direct) ? "Direct P2P" :
        _links.Keys.Any(Direct) ? "P2P + relay" : "Relay";

    public bool Handle(JsonElement message)
    {
        string? type = message.GetProperty("t").GetString();
        if (type == "party.result") return !PeerFrameRules.Valid(message);
        if (type == "party.status")
        {
            var data = message.GetProperty("d"); var party = PartyStatus.Parse(data);
            emit(message);
            var roster = data.GetProperty("players").EnumerateArray().ToArray();
            var connected = roster.Where(p => p.GetProperty("connected").GetBoolean()).Select(p => p.GetProperty("playerId").GetString()!).ToHashSet();
            var members = roster.Select(p => p.GetProperty("playerId").GetString()!).ToHashSet();
            string mode = $"{party.Hosting}:{party.HostingPolicy}:{party.Phase}:{party.Match}:{party.AuthorityId}";
            bool unchanged = party.Epoch == _epoch && mode == _mode;
            var previousConnected = _connected; var previousMembers = _members;
            _party = party; _connected = connected; _members = members;
            if (unchanged)
            {
                if (party.Adaptive)
                {
                    foreach (string id in previousConnected.Except(connected)) { _authority?.ReleaseInput(id); RemoveLink(id); }
                    foreach (string id in previousMembers.Except(members)) _authority?.RemovePlayer(id);
                    if (party.Hosting == "auto" && party.HostingPolicy != "server") PrepareLinks(connected.Where(id => id != playerId), preflight: true);
                    else if (_authority is not null)
                        foreach (string id in connected.Except(previousConnected).Where(id => id != playerId))
                        {
                            var p = roster.First(p => p.GetProperty("playerId").GetString() == id);
                            CreateLink(id, offer: true);
                            _authority.RestorePlayer(new RealtimePlayer(id, p.GetProperty("displayName").GetString()!, p.GetProperty("slot").GetInt32(), new Dictionary<string,string>()), p.GetProperty("appearance").GetInt32());
                            _players = _authority.Players.Select(p => p.PlayerId).ToArray();
                        }
                }
                return true;
            }
            DisposeLinks(); _authority = null; _lastInput.Clear(); _directSeen.Clear(); _directQuality.Clear(); _relayQuality.Clear();
            _lastTick = -1; _epoch = party.Epoch; _mode = mode; _accumulator = _heartbeat = _probe = _report = 0;
            _stageReady = false; _pendingState = null; _qualitySamples = 0; _frameGap = 0;
            _lastAdvance = Stopwatch.GetTimestamp();
            _players = connected.ToArray();
            if (party.Adaptive && party.Hosting == "auto" && party.HostingPolicy != "server")
            { PrepareLinks(_players.Where(id => id != playerId), preflight: true); return true; }
            if (party.Hosting != "peer" || party.Phase != "playing" || party.AuthorityId is null) return true;
            PrepareLinks(playerId == party.AuthorityId ? _players.Where(id => id != playerId) : [party.AuthorityId], preflight: false);
            if (playerId == party.AuthorityId)
            {
                var active = roster.Where(p => p.GetProperty("connected").GetBoolean()).ToArray();
                var players = active.Select(p => new RealtimePlayer(p.GetProperty("playerId").GetString()!, p.GetProperty("displayName").GetString()!, p.GetProperty("slot").GetInt32(), new Dictionary<string, string>())).ToArray();
                var appearances = active.ToDictionary(p => p.GetProperty("playerId").GetString()!, p => p.GetProperty("appearance").GetInt32());
                _authority = new PeerMatch(lobbyId, party.Settings!, data.GetProperty("seed").GetInt32() + party.Match, players, appearances, Publish);
            }
            return true;
        }
        if (type is not ("peer.signal" or "peer.frame" or "peer.state")) return false;
        var d = message.GetProperty("d");
        if (d.GetProperty("epoch").GetInt32() != _epoch) return true;
        string from = d.GetProperty("from").GetString()!;
        if (!_connected.Contains(from)) return true;
        if (type == "peer.signal")
        {
            var signal = d.GetProperty("payload");
            if (!RelayProbe(from, signal) && _links.TryGetValue(from, out var link)) link.AcceptSignal(signal);
        }
        else Receive(from, _epoch, JsonSerializer.SerializeToUtf8Bytes(d.GetProperty("payload")));
        return true;
    }

    private void PrepareLinks(IEnumerable<string> targets, bool preflight)
    {
        foreach (string target in targets)
            if (!_links.ContainsKey(target)) CreateLink(target, preflight ? string.CompareOrdinal(playerId, target) < 0 : playerId == _party?.AuthorityId);
    }
    private void CreateLink(string target, bool offer)
    {
        RemoveLink(target);
        if (factory is null) return;
        var link = factory(); _links[target] = link;
        int epoch = _epoch;
        link.Signal += signal => sendControl("peer.signal", new { epoch, to = target, payload = signal });
        link.Received += packet => Receive(target, epoch, packet, direct: true);
        link.Start(offer);
    }

    public void Advance()
    {
        long now = Stopwatch.GetTimestamp();
        double delta = Stopwatch.GetElapsedTime(_lastAdvance, now).TotalSeconds; _lastAdvance = now;
        if (_disposed || _party is null) return;
        _frameGap = Math.Max(_frameGap, Math.Min(5000, delta * 1000));
        _heartbeat += delta;
        if (_heartbeat >= 1) { _heartbeat = 0; sendControl("peer.heartbeat", new { epoch = _epoch }); }
        foreach (var link in _links.Values.ToArray()) link.Poll();
        _probe += delta; _report += delta;
        if (_probe >= .5)
        {
            _probe = 0;
            foreach (var (id, link) in _links.Where(p => p.Value.Connected).ToArray())
            {
                long nonce = ++_nonce;
                Window(_directQuality, id).Begin(nonce);
                link.Send(JsonSerializer.SerializeToUtf8Bytes(new { epoch = _epoch, ping = nonce }));
            }
            if (_party.Adaptive)
                foreach (string id in ProbeTargets())
                {
                    long nonce = ++_nonce; Window(_relayQuality, id).Begin(nonce);
                    sendControl("peer.signal", new { epoch = _epoch, to = id, payload = new { kind = "probe", nonce } });
                }
        }
        if (_party.Adaptive && _report >= 1)
        {
            _report = 0;
            var observations = ProbeTargets().Select(id =>
            {
                var direct = Window(_directQuality, id); var relay = Window(_relayQuality, id);
                var measured = Direct(id) && direct.Count >= 3 && (relay.Count < 3 || direct.P95 <= relay.P95) ? direct : relay;
                return new { id, rtt = measured.P95, loss = measured.Loss, direct = ReferenceEquals(measured, direct), samples = measured.Count };
            }).ToArray();
            _qualitySamples = observations.Length == 0 ? 0 : observations.Min(p => p.samples);
            sendControl("peer.quality", new { epoch = _epoch, serverRtt = serverRtt?.Invoke() ?? 5000, frameGap = _frameGap, links = observations });
            _frameGap = 0;
        }
        if (_party.Hosting != "peer" || _party.Phase != "playing") return;
        _accumulator += Math.Min(delta, 5d / LocalMatch.TickRate);
        while (_accumulator >= 1d / LocalMatch.TickRate)
        {
            foreach (var stale in _lastInput.Where(p => DateTimeOffset.UtcNow - p.Value > TimeSpan.FromMilliseconds(500)).Select(p => p.Key).ToArray())
            { _authority?.ReleaseInput(stale); _lastInput.Remove(stale); }
            var packet = input();
            if (_authority is not null) Receive(playerId, _epoch, packet);
            else if (_party.AuthorityId is { } host) Send(host, packet);
            _authority?.Advance(); _accumulator -= 1d / LocalMatch.TickRate;
        }
    }
    private IEnumerable<string> ProbeTargets() => _party?.HostingPolicy == "server" ? [] : _party?.Hosting == "auto" ? _connected.Where(id => id != playerId) :
        _party?.Hosting == "peer" ? playerId == _party.AuthorityId ? _connected.Where(id => id != playerId) : [_party.AuthorityId!] : [];
    private bool RelayProbe(string from, JsonElement signal)
    {
        if (!signal.TryGetProperty("kind", out var kind) || kind.GetString() is not ("probe" or "probe-reply")) return false;
        if (!signal.TryGetProperty("nonce", out var value) || !value.TryGetInt64(out long nonce)) return true;
        if (kind.GetString() == "probe") sendControl("peer.signal", new { epoch = _epoch, to = from, payload = new { kind = "probe-reply", nonce } });
        else Window(_relayQuality, from).Complete(nonce);
        return true;
    }
    private void Publish(string? recipient, string type, object? data)
    {
        if (type == "peer.finished") { sendControl(type, new { epoch = _epoch, result = JsonSerializer.SerializeToElement(data, RealtimeJson.Default) }); return; }
        var frame = JsonSerializer.SerializeToElement(new { t = type, k = _authority?.Tick ?? 0, d = data }, RealtimeJson.Default);
        if (recipient == playerId) { emit(frame); return; }
        if (recipient is not null && _connected.Contains(recipient)) Send(recipient, JsonSerializer.SerializeToUtf8Bytes(frame));
    }
    private void Send(string target, byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes); var payload = doc.RootElement;
        var direct = JsonSerializer.SerializeToUtf8Bytes(new { epoch = _epoch, payload });
        if (Direct(target) && PeerPacketCodec.TryEncode(direct, out var packet) &&
            (payload.GetProperty("t").GetString() == "state" ? _links[target].SendState(packet) : _links[target].Send(packet)))
        { DirectSent++; return; }
        sendControl(payload.GetProperty("t").GetString() == "state" ? "peer.state" : "peer.frame", new PeerFrameEnvelope(_epoch, target, payload));
    }
    private void Receive(string from, int epoch, byte[] bytes, bool direct = false)
    {
        if (epoch != _epoch || _party is null || bytes.Length > 131072 || !_connected.Contains(from)) return;
        if (direct) { if (!PeerPacketCodec.TryDecode(bytes, out var decoded)) return; bytes = decoded; }
        if ((_party.Hosting == "auto" || playerId == _party.AuthorityId) && bytes.Length > 2048) return;
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 }); var frame = doc.RootElement;
            if (frame.TryGetProperty("epoch", out var generation))
            {
                if (generation.GetInt32() != _epoch) return;
                if (direct && frame.TryGetProperty("ping", out var ping))
                { if (ping.TryGetInt64(out long nonce) && _links.TryGetValue(from, out var source)) source.Send(JsonSerializer.SerializeToUtf8Bytes(new { epoch = _epoch, pong = nonce })); return; }
                if (direct && frame.TryGetProperty("pong", out var pong))
                {
                    if (pong.TryGetInt64(out long nonce) && Window(_directQuality, from).Complete(nonce)) _directSeen[from] = Stopwatch.GetTimestamp();
                    return;
                }
                frame = frame.GetProperty("payload");
            }
            if (_party.Hosting != "peer" || _party.Phase != "playing" || !_players.Contains(from)) return;
            if (playerId == _party.AuthorityId && bytes.Length > 2048) return;
            string? type = frame.GetProperty("t").GetString();
            if (direct) DirectReceived++;
            if (playerId == _party.AuthorityId)
            { if (type == "input") { _authority?.Receive(from, frame); _lastInput[from] = DateTimeOffset.UtcNow; } }
            else if (from == _party.AuthorityId && type is "stage" or "state" or "appearance")
            {
                if (!PeerFrameRules.Valid(frame)) return;
                if (type == "state")
                {
                    long tick = frame.GetProperty("k").GetInt64();
                    if (tick <= _lastTick) return;
                    if (!_stageReady) { _pendingState = frame.Clone(); return; }
                    _lastTick = tick;
                }
                emit(frame);
                if (type == "stage")
                {
                    _stageReady = true;
                    if (_pendingState is { } pending)
                    { _pendingState = null; _lastTick = pending.GetProperty("k").GetInt64(); emit(pending); }
                }
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException) { }
    }
    private void RemoveLink(string id)
    { if (_links.Remove(id, out var link)) link.Dispose(); _directSeen.Remove(id); _directQuality.Remove(id); _relayQuality.Remove(id); }
    private void DisposeLinks() { foreach (var link in _links.Values) link.Dispose(); _links.Clear(); }
    public void Dispose() { if (_disposed) return; _disposed = true; DisposeLinks(); _authority = null; }
    private static ProbeWindow Window(Dictionary<string, ProbeWindow> windows, string id)
    { if (!windows.TryGetValue(id, out var window)) windows[id] = window = new(); return window; }

    private sealed class ProbeWindow
    {
        private readonly Dictionary<long, long> _pending = [];
        private readonly Queue<double?> _results = [];
        public int Count { get { Expire(); return _results.Count(v => v.HasValue); } }
        public double P95 { get { Expire(); var values = _results.Where(v => v.HasValue).Select(v => v!.Value).Order().ToArray(); return values.Length == 0 ? 5000 : values[(int)Math.Ceiling(values.Length * .95) - 1]; } }
        public double Loss { get { Expire(); return _results.Count == 0 ? 1 : (double)_results.Count(v => !v.HasValue) / _results.Count; } }
        public void Begin(long nonce) { Expire(); if (_pending.Count < 16) _pending[nonce] = Stopwatch.GetTimestamp(); }
        public bool Complete(long nonce)
        { if (!_pending.Remove(nonce, out long sent)) return false; Add(Math.Min(5000, Stopwatch.GetElapsedTime(sent).TotalMilliseconds)); return true; }
        private void Expire()
        { foreach (var pending in _pending.Where(p => Stopwatch.GetElapsedTime(p.Value).TotalSeconds >= 1.5).ToArray()) { _pending.Remove(pending.Key); Add(null); } }
        private void Add(double? value) { _results.Enqueue(value); while (_results.Count > 16) _results.Dequeue(); }
    }
}

internal sealed record PeerFrameEnvelope(
    [property: System.Text.Json.Serialization.JsonPropertyName("epoch")] int Epoch,
    [property: System.Text.Json.Serialization.JsonPropertyName("to")] string To,
    [property: System.Text.Json.Serialization.JsonPropertyName("payload")] JsonElement Payload);

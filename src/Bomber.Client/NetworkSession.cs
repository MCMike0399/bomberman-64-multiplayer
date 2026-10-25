using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Bomber.Game;

namespace Bomber.Client;

/// <summary>Guest-authenticated Burbuja client. Network work never calls the engine; poll DrainMessages.</summary>
public sealed partial class NetworkSession : ISession
{
    public const string DefaultServerUrl = "https://blast.burbuja-lab.dev";
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly MessageBuffer _messages = new();
    private readonly MessageBuffer _wireMessages = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _latestPeerStates = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _controlOutbound = new();
    private PeerRoomClient? _peer;
    private Func<IPeerLink>? _peerFactory;
    public string PeerTransport => _peer?.Transport ?? "Relay";
    public long DirectPacketsSent => _peer?.DirectSent ?? 0;
    public long DirectPacketsReceived => _peer?.DirectReceived ?? 0;
    public int ConnectionQualitySamples => Math.Min(Volatile.Read(ref _pingCount), _peer?.QualitySamples ?? int.MaxValue);
    public JsonElement? ConnectionQuality => _peer?.LastQuality;
    private long _lastPing;
    private int _pingCount;
    private double _serverRtt = 5000;
    private readonly object _inputGate = new();
    private readonly Uri _origin;
    private ClientWebSocket? _socket;
    private Task? _worker;
    private string? _token, _lobbyId, _ticketId;
    private string _status = "Connecting";
    private PlayerInput _input;
    private readonly Queue<(bool A, bool B, bool R, bool Aim, bool Stop)> _buttons = new();
    private long _sequence;
    private int _disposed;
    private int _appearance;
    private int _appearancePending, _startPending;
    private int _peerHelloSent;
    private int _connectionGeneration, _peerResetPending;
    public int ConnectionGeneration => Volatile.Read(ref _connectionGeneration);
    private PartyStatus? _party;
    private byte[]? _pendingRules;
    public void SetRoomSettings(SessionSettings settings)
    {
        if(!IsLeader||Party?.Phase!="lobby")throw new InvalidOperationException("Only the waiting room leader can change the rules.");
        Interlocked.Exchange(ref _pendingRules,JsonSerializer.SerializeToUtf8Bytes(new {t="party.settings",d=settings.ToWireSettings()}));
    }
    private readonly TaskCompletionSource _partyReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public PartyStatus? Party => Volatile.Read(ref _party);
    public bool IsLeader => Party is { LeaderId: not null } party && party.LeaderId == PlayerId;
    public void RequestStart() { if (IsLeader && Party?.CanStart == true) Interlocked.Exchange(ref _startPending, 1); }
    public void ReleaseInputs() { lock (_inputGate) { _input = default; _buttons.Clear(); } }
    public int LocalPlayerCount => 1;
    public string Status => Volatile.Read(ref _status);
    public bool IsEnded => Status.StartsWith("Ended", StringComparison.Ordinal) || Status.StartsWith("Error:", StringComparison.Ordinal);
    public string? JoinCode { get; private set; }
    public string? PlayerId { get; private set; }
    public string? LobbyId => _lobbyId;
    public string? ConnectionRoute { get; private set; }

    private NetworkSession(Uri origin, HttpMessageHandler? handler)
    {
        if (!origin.IsAbsoluteUri || origin.Scheme is not ("http" or "https"))
            throw new ArgumentException("Server must be an absolute http(s) URL.", nameof(origin));
        _origin = origin;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = origin;
        _http.Timeout = TimeSpan.FromSeconds(40);
    }

    public static async Task<NetworkSession> ConnectAsync(Uri server, string displayName, SessionSettings settings,
        NetworkMode mode = NetworkMode.CreatePrivate, string? joinCode = null, CancellationToken cancellationToken = default, int appearance = 0,
        Func<IPeerLink>? peerFactory = null)
    {
        if (appearance is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(appearance));
        var session = new NetworkSession(server, null) { _appearance = appearance, _peerFactory = peerFactory };
        try
        {
            await session.InitializeAsync(displayName, settings, mode, joinCode, cancellationToken).ConfigureAwait(false);
            if(mode is NetworkMode.CreatePrivate or NetworkMode.CreatePeer or NetworkMode.QuickPlay)
            {
                try { await session._partyReady.Task.WaitAsync(TimeSpan.FromSeconds(10),cancellationToken).ConfigureAwait(false); }
                catch(TimeoutException) { throw new InvalidOperationException("This server needs the party update before it can host leader-controlled rooms."); }
            }
            return session;
        }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task InitializeAsync(string name, SessionSettings settings, NetworkMode mode, string? code, CancellationToken ct)
    {
        var wireSettings = settings.ToWireSettings();
        if (mode == NetworkMode.CreatePrivate) wireSettings["party"] = "on";
        var guest = await RequestAsync(HttpMethod.Post, "guests", new { displayName = string.IsNullOrWhiteSpace(name)?PlayerNames.UnnamedGuest:name.Trim() }, ct);
        _token = guest.GetProperty("token").GetString()!;
        PlayerId = guest.GetProperty("playerId").GetString();
        JsonElement reservation;
        if (mode is NetworkMode.CreatePrivate or NetworkMode.CreatePeer)
        {
            var created = await RequestAsync(HttpMethod.Post, "lobbies", new { game = mode == NetworkMode.CreatePeer ? "blast-peer" : "blast-room", visibility = "private", settings = wireSettings }, ct);
            var lobby = created.GetProperty("lobby");
            JoinCode = lobby.GetProperty("joinCode").GetString();
            _messages.Add(JsonSerializer.SerializeToElement(new { t = "sys.lobby", d = lobby }));
            reservation = created.GetProperty("reservation");
            _lobbyId = reservation.GetProperty("lobbyId").GetString();
        }
        else if (mode == NetworkMode.JoinCode)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Enter a join code.", nameof(code));
            JoinCode = code.Trim().ToUpperInvariant();
            reservation = await RequestAsync(HttpMethod.Post, "lobbies/join", new { code = JoinCode }, ct);
        }
        else
        {
            Volatile.Write(ref _status, "Searching for players");
            var ticket = await RequestAsync(HttpMethod.Post, "queue", new { game = "blast-online" }, ct);
            _ticketId = ticket.GetProperty("ticketId").GetString();
            while (ticket.GetProperty("state").GetString() == "queued")
                ticket = await RequestAsync(HttpMethod.Get, $"queue/{Uri.EscapeDataString(_ticketId!)}?waitSeconds=10", null, ct);
            if (ticket.GetProperty("state").GetString() != "matched") throw new InvalidOperationException("Matchmaking expired or was cancelled. Try again.");
            reservation = ticket.GetProperty("reservation");
            _ticketId = null;
        }
        _lobbyId = reservation.GetProperty("lobbyId").GetString();
        await OpenSocketAsync(reservation, ct);
        _worker = RunAsync();
    }

    private async Task OpenSocketAsync(JsonElement reservation, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader(OnlineProtocol.HeaderName,OnlineProtocol.WireRevision);
        socket.Options.CollectHttpResponseDetails=true;
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        // Realtime frames contain game state/intent; HTTP credentials stay outside this channel.
        // No shared compression history between messages, and old servers may decline negotiation.
        socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions
        { ClientContextTakeover = false, ServerContextTakeover = false };
        try
        {
            var connectUri = ResolveConnectUri(_origin, reservation.GetProperty("connectUrl").GetString()!);
            await socket.ConnectAsync(connectUri, timeout.Token);
            if(socket.HttpResponseHeaders is null || !OnlineProtocol.Accepts(socket.HttpResponseHeaders.Where(h=>h.Key.Equals(OnlineProtocol.HeaderName,StringComparison.OrdinalIgnoreCase)).SelectMany(h=>h.Value)))
                throw new InvalidOperationException("This game server uses incompatible stage rules. Update the server before joining.");
            ConnectionRoute = connectUri.AbsolutePath;
            _socket = socket;
            int generation = Interlocked.Increment(ref _connectionGeneration);
            while (_controlOutbound.TryDequeue(out _)) { }
            _latestPeerStates.Clear(); _wireMessages.Clear();
            Interlocked.Exchange(ref _pingCount, 0); Volatile.Write(ref _lastPing, 0); Volatile.Write(ref _serverRtt, 5000);
            if (generation > 1) Interlocked.Exchange(ref _peerResetPending, 1);
            Interlocked.Exchange(ref _peerHelloSent, 0);
            Interlocked.Exchange(ref _sequence, 0);
            Volatile.Write(ref _status, "Connected");
        }
        catch { socket.Dispose(); throw; }
    }

    public static Uri ResolveConnectUri(Uri server, string connectUrl)
    {
        var uri = new Uri(server, connectUrl);
        var builder = new UriBuilder(uri) { Scheme = uri.Scheme switch { "http" => "ws", "https" => "wss", "ws" => "ws", "wss" => "wss", _ => throw new InvalidDataException("Invalid WebSocket URL.") } };
        if (server.Scheme == "https" && builder.Scheme != "wss") throw new InvalidDataException("Secure servers must issue secure WebSocket URLs.");
        return builder.Uri;
    }

    private async Task RunAsync()
    {
        var ct = _lifetime.Token;
        var retries = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var connected = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var receive = ReceiveAsync(_socket!, connected.Token);
                var send = SendLoopAsync(_socket!, connected.Token);
                await Task.WhenAny(receive, send);
                connected.Cancel();
                _socket!.Abort();
                try { await Task.WhenAll(receive, send); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                if (Status.StartsWith("Ended", StringComparison.Ordinal)) return;
                throw new IOException("Connection interrupted.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is WebSocketException or IOException or HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
            {
                if (ct.IsCancellationRequested) return;
                if (Status.StartsWith("Ended", StringComparison.Ordinal)) return;
                if (++retries > 5) { Fail("Connection lost. Return to the menu to reconnect."); return; }
                Volatile.Write(ref _status, $"Reconnecting ({retries}/5)");
                ReleaseInputs(); Interlocked.Exchange(ref _startPending, 0);Interlocked.Exchange(ref _pendingRules,null);
                _socket?.Dispose();
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * retries), ct);
                        var reservation = await RequestAsync(HttpMethod.Post, $"lobbies/{Uri.EscapeDataString(_lobbyId!)}/join", new { }, ct);
                        await OpenSocketAsync(reservation, ct);
                        break;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception reconnectError) when (reconnectError is HttpRequestException or WebSocketException or JsonException or InvalidOperationException or OperationCanceledException)
                    {
                        if (++retries > 5) { Fail(reconnectError.Message); return; }
                        Volatile.Write(ref _status, $"Reconnecting ({retries}/5)");
                    }
                }
            }
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var chunk = new byte[8192];
        while (!ct.IsCancellationRequested)
        {
            using var frame = new MemoryStream();
            ValueWebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(chunk.AsMemory(), ct);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    var close = (int?)socket.CloseStatus;
                    if (close is 1000 or 4002 or 4003 or 4004 or 4005)
                    {
                        Volatile.Write(ref _status, $"Ended: {socket.CloseStatusDescription ?? "Lobby closed"}");
                        _messages.Add(JsonSerializer.SerializeToElement(new { t = "sys.closed", d = new { reason = Status } }));
                        return;
                    }
                    throw new IOException("Server disconnected.");
                }
                if (received.MessageType != WebSocketMessageType.Text || frame.Length + received.Count > 1024 * 1024)
                    throw new IOException("Server sent an unsupported or oversized frame.");
                frame.Write(chunk, 0, received.Count);
            } while (!received.EndOfMessage);
            using var document = JsonDocument.Parse(frame.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            var message = document.RootElement;
            if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("t", out var type) || type.ValueKind != JsonValueKind.String)
                throw new IOException("Server sent an invalid message.");
            if (type.GetString() == "sys.ended") Volatile.Write(ref _status, "Ended: Match complete");
            if (type.GetString() == "sys.welcome") Interlocked.Exchange(ref _appearancePending, 1);
            if (type.GetString() == "sys.pong" && message.TryGetProperty("d", out var pong) &&
                pong.TryGetProperty("echo", out var echo) && echo.ValueKind == JsonValueKind.Object &&
                echo.TryGetProperty("nonce", out var nonce) && nonce.TryGetInt64(out long sent) && sent == Volatile.Read(ref _lastPing))
            {
                double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
                if (elapsed is >= 0 and <= 5000) { Volatile.Write(ref _serverRtt, elapsed); Interlocked.Increment(ref _pingCount); }
                continue;
            }
            if (type.GetString() == "party.status")
            {
                var party = PartyStatus.Parse(message.GetProperty("d"));
                if ((party.Hosting == "peer" || party.Adaptive) && Interlocked.Exchange(ref _peerHelloSent, 1) == 0)
                    QueuePeer("peer.hello", new { version = OnlineProtocol.Revision });
                if (Party?.Phase != party.Phase || Party?.Match != party.Match) ReleaseInputs();
                Volatile.Write(ref _party, party);_partyReady.TrySetResult();
            }
            _wireMessages.Add(message);
            if (_wireMessages.Overflowed)
            { Fail("The connection cannot keep up with room control messages."); _lifetime.Cancel(); return; }
        }
    }

    public void SetAppearance(int index)
    {
        if(index is <0 or >7)throw new ArgumentOutOfRangeException(nameof(index));
        Volatile.Write(ref _appearance,index);Interlocked.Exchange(ref _appearancePending,1);
    }
    public static byte[] EncodeAppearance(int index)
    {
        if(index is <0 or >7)throw new ArgumentOutOfRangeException(nameof(index));
        return JsonSerializer.SerializeToUtf8Bytes(new {t="appearance",d=new {index}});
    }
    private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / 30));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(Volatile.Read(ref _lastPing)).TotalSeconds >= .5)
            {
                long nonce = System.Diagnostics.Stopwatch.GetTimestamp(); Volatile.Write(ref _lastPing, nonce);
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { t = "sys.ping", d = new { nonce } }).AsMemory(), WebSocketMessageType.Text, true, ct);
            }
            // The initial eight-player stage/appearance burst must never evict ICE offers or answers.
            for (int count = 0; count < 16 && _controlOutbound.TryDequeue(out var control); count++)
                await socket.SendAsync(control.AsMemory(), WebSocketMessageType.Text, true, ct);
            foreach (string target in _latestPeerStates.Keys.Take(8))
                if (_latestPeerStates.TryRemove(target, out var packet))
                    await socket.SendAsync(packet.AsMemory(), WebSocketMessageType.Text, true, ct);
            if (Interlocked.Exchange(ref _appearancePending,0)!=0)
                await socket.SendAsync(EncodeAppearance(Volatile.Read(ref _appearance)).AsMemory(),WebSocketMessageType.Text,true,ct);
            if(Interlocked.Exchange(ref _pendingRules,null) is {} rules)
                await socket.SendAsync(rules.AsMemory(),WebSocketMessageType.Text,true,ct);
            if (Interlocked.Exchange(ref _startPending, 0) != 0)
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { t = "party.start", d = new {} }).AsMemory(), WebSocketMessageType.Text, true, ct);
            if (Party?.Hosting is "peer" or "auto") continue;
            PlayerInput input;
            lock (_inputGate)
            {
                input = _input;
                if (_buttons.TryDequeue(out var buttons)) input = input with { A = buttons.A, B = buttons.B, R = buttons.R, Aim = buttons.Aim, Stop = buttons.Stop };
            }
            var bytes = EncodeInput(input, Interlocked.Increment(ref _sequence));
            await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct);
        }
    }

    public static byte[] EncodeInput(PlayerInput input, long sequence) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        t = "input", s = sequence,
        d = new { x = float.IsFinite(input.X) ? Math.Clamp(input.X, -1, 1) : 0,
            z = float.IsFinite(input.Z) ? Math.Clamp(input.Z, -1, 1) : 0, a = input.A, b = input.B, r = input.R, aim = input.Aim, stop = input.Stop }
    });

    private async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        ct = timeout.Token;
        using var request = new HttpRequestMessage(method, new Uri(_origin, "/realtime/v1/" + path));
        request.Headers.Add(OnlineProtocol.HeaderName,OnlineProtocol.WireRevision);
        if (_token is not null) request.Headers.Add("x-player-token", _token);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default;
        // Bound bodies before parsing, even if the server omits Content-Length.
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bounded = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(bytes.AsMemory(), ct)) != 0)
        {
            if (bounded.Length + count > 1024 * 1024) throw new HttpRequestException("Server response is too large.");
            bounded.Write(bytes, 0, count);
        }
        using var json = JsonDocument.Parse(bounded.ToArray());
        if (!response.IsSuccessStatusCode)
        {
            var detail = json.RootElement.TryGetProperty("error", out var error) ? error.GetProperty("message").GetString() : response.ReasonPhrase;
            throw new HttpRequestException(detail ?? "Server refused the request.", null, response.StatusCode);
        }
        if(path=="guests" && (!response.Headers.TryGetValues(OnlineProtocol.HeaderName,out var protocol)||!OnlineProtocol.Accepts(protocol)))
            throw new InvalidOperationException("This game server uses incompatible stage rules. Update the server before joining.");
        return json.RootElement.Clone();
    }

    private void Fail(string reason)
    {
        Volatile.Write(ref _status, "Error: " + reason);
        _messages.Add(JsonSerializer.SerializeToElement(new { t = "sys.error", d = new { code = "connection_lost", message = reason } }));
    }
    public void Advance(double deltaSeconds)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        PumpMessages();
        if (Status == "Connected") _peer?.Advance();
    }
    public void SendInput(int localPlayer, PlayerInput input)
    {
        if (localPlayer != 0) throw new ArgumentOutOfRangeException(nameof(localPlayer));
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (_inputGate)
        {
            if ((_input.A, _input.B, _input.R, _input.Aim, _input.Stop) != (input.A, input.B, input.R, input.Aim, input.Stop))
            {
                if (_buttons.Count == 16) _buttons.Dequeue();
                _buttons.Enqueue((input.A, input.B, input.R, input.Aim, input.Stop));
            }
            _input = input;
        }
    }
    public IReadOnlyList<JsonElement> DrainMessages() { PumpMessages(); return _messages.Drain(); }

    private void PumpMessages()
    {
        if (Interlocked.Exchange(ref _peerResetPending, 0) != 0) { _peer?.Dispose(); _peer = null; }
        int generation = ConnectionGeneration;
        if (_messages.Overflowed)
        { if (!IsEnded) { Fail("The game cannot keep up with room control messages."); _lifetime.Cancel(); } return; }
        foreach (var message in _wireMessages.Drain())
        {
            if (ConnectionGeneration != generation) return;
            if (message.GetProperty("t").GetString() == "party.status" && message.GetProperty("d").TryGetProperty("hosting", out var hosting) && hosting.GetString() is "peer" or "auto")
                _peer ??= new PeerRoomClient(PlayerId!, _lobbyId!, _peerFactory,
                    (type, data) => { if (ConnectionGeneration == generation) QueuePeer(type, data); },
                    frame => { if (ConnectionGeneration == generation) _messages.Add(frame); }, TakePeerInput, () => Volatile.Read(ref _serverRtt));
            if (_peer?.Handle(message) != true) _messages.Add(message);
        }
    }
    private void QueuePeer(string type, object data)
    {
        var packet = JsonSerializer.SerializeToUtf8Bytes(new { t = type, d = data });
        if (type == "peer.state" && data is PeerFrameEnvelope state)
        {
            if (_latestPeerStates.Count < 8 || _latestPeerStates.ContainsKey(state.To)) _latestPeerStates[state.To] = packet;
            else { Fail("Too many pending peer routes."); _lifetime.Cancel(); }
            return;
        }
        if (_controlOutbound.Count >= 512)
        { Fail("The connection cannot keep up with reliable game messages."); _lifetime.Cancel(); return; }
        _controlOutbound.Enqueue(packet);
    }
    private byte[] TakePeerInput()
    {
        lock (_inputGate)
        {
            var input = _input;
            if (_buttons.TryDequeue(out var buttons)) input = input with { A = buttons.A, B = buttons.B, R = buttons.R, Aim = buttons.Aim, Stop = buttons.Stop };
            return EncodeInput(input, Interlocked.Increment(ref _sequence));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifetime.CancelAsync();
        _socket?.Abort();
        if (_worker is not null) try { await _worker; } catch (OperationCanceledException) { }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        if (_ticketId is not null)
        {
            try { await RequestAsync(HttpMethod.Delete, $"queue/{Uri.EscapeDataString(_ticketId)}", null, cleanup.Token); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { }
        }
        try
        {
            // Cancellation can race a successful queue match or lobby creation before its response is read.
            // The guest identity can still find and release that seat through the real kernel contract.
            if (_lobbyId is null && _token is not null)
            {
                var me = await RequestAsync(HttpMethod.Get, "me", null, cleanup.Token);
                if (me.TryGetProperty("lobbyId", out var seat) && seat.ValueKind == JsonValueKind.String) _lobbyId = seat.GetString();
            }
            if (_lobbyId is not null)
            {
                await RequestAsync(HttpMethod.Post, $"lobbies/{Uri.EscapeDataString(_lobbyId)}/leave", new { }, cleanup.Token);
                // Leave is queued by the owner. Preserve native channels until its
                // membership update can stop peers sending to this departed seat.
                while (_peer is not null)
                {
                    var me = await RequestAsync(HttpMethod.Get, "me", null, cleanup.Token);
                    if (!me.TryGetProperty("lobbyId", out var seat) || seat.ValueKind != JsonValueKind.String || seat.GetString() != _lobbyId) break;
                    await Task.Delay(20, cleanup.Token);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { }
        _peer?.Dispose();
        _socket?.Dispose();
        _http.Dispose();
        _lifetime.Dispose();
    }
}

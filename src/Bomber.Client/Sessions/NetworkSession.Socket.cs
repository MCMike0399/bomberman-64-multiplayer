using System.Net.WebSockets;
using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Online;

namespace Bomber.Client.Sessions;

/// <summary>WebSocket connection, reconnection, receive and 30 Hz send loops.</summary>
public sealed partial class NetworkSession
{
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
                throw new InvalidOperationException(OnlineProtocol.ServerUpdateMessage);
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
            NoteConnection("connected");
        }
        catch (WebSocketException) when (socket.HttpStatusCode == System.Net.HttpStatusCode.Conflict)
        { socket.Dispose(); throw new InvalidOperationException(OnlineProtocol.ServerUpdateMessage); }
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
                NoteConnection($"interrupted: {ex.GetType().Name}: {ex.Message}");
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
                        NoteConnection($"reconnect failed: {reconnectError.GetType().Name}: {reconnectError.Message}");
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
                    NoteConnection($"server closed {close}: {socket.CloseStatusDescription}");
                    if (close is 1000 or 4002 or 4003 or 4004 or 4005)
                    {
                        Volatile.Write(ref _status, $"Ended: {socket.CloseStatusDescription ?? "Lobby closed"}");
                        _messages.Add(JsonSerializer.SerializeToElement(new { t = "sys.closed", d = new { reason = Status } }));
                        return;
                    }
                    throw new IOException($"Server disconnected ({close}: {socket.CloseStatusDescription}).");
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

    public static byte[] EncodeInput(PlayerInput input, long sequence)
    {
        var target = input.AimTarget is { } t && float.IsFinite(t.X) && float.IsFinite(t.Y) ? t : (System.Numerics.Vector2?)null;
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            t = "input", s = sequence,
            d = new { x = float.IsFinite(input.X) ? Math.Clamp(input.X, -1, 1) : 0,
                z = float.IsFinite(input.Z) ? Math.Clamp(input.Z, -1, 1) : 0, a = input.A, b = input.B, r = input.R, aim = input.Aim, stop = input.Stop,
                tx = target?.X, tz = target?.Y, tr = target is not null && input.AimRelative }
        });
    }
}

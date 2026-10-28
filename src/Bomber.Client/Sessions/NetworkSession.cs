using System.Net.WebSockets;
using System.Text.Json;
using Bomber.Client.Peer;
using Bomber.Game.Matches;

namespace Bomber.Client.Sessions;

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
        if(!IsLeader||Party is not {Phase:"lobby",PublicRoom:false})throw new InvalidOperationException("Only a waiting private room's owner can change the rules.");
        Interlocked.Exchange(ref _pendingRules,JsonSerializer.SerializeToUtf8Bytes(new {t="party.settings",d=settings.ToWireSettings()}));
    }
    private readonly TaskCompletionSource _partyReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public PartyStatus? Party => Volatile.Read(ref _party);
    public bool IsLeader => Party is { LeaderId: not null } party && party.LeaderId == PlayerId;
    public void RequestStart() { if (IsLeader && Party is {CanStart:true,PublicRoom:false}) Interlocked.Exchange(ref _startPending, 1); }
    public void ReleaseInputs() { lock (_inputGate) { _input = default; _buttons.Clear(); } }
    public int LocalPlayerCount => 1;
    public string Status => Volatile.Read(ref _status);
    private long _partyReceivedAt;
    /// <summary>Local UTC milliseconds when the socket received the latest party status,
    /// independent of engine frame timing.</summary>
    public long PartyReceivedAtUnixMs => Volatile.Read(ref _partyReceivedAt);
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _connectionEvents = new();
    /// <summary>The last 64 connection transitions: generation, close status/reason,
    /// reconnects and queue overflows. Bounded; for support and QA diagnostics.</summary>
    public IReadOnlyList<string> ConnectionEvents => _connectionEvents.ToArray();
    private void NoteConnection(string text)
    {
        _connectionEvents.Enqueue($"{DateTimeOffset.UtcNow:HH:mm:ss.fff} g{ConnectionGeneration} {text}");
        while (_connectionEvents.Count > 64) _connectionEvents.TryDequeue(out _);
    }
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
        // Transport I/O must not capture a UI/engine synchronization context.
        // Peer simulation and native links remain on the caller's polling thread.
        _worker = Task.Run(RunAsync);
    }

    public void SetAppearance(int index)
    {
        if(index is <0 or >7)throw new ArgumentOutOfRangeException(nameof(index));
        Volatile.Write(ref _appearance,index);Interlocked.Exchange(ref _appearancePending,1);
    }

    private void Fail(string reason)
    {
        NoteConnection("failed: " + reason);
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

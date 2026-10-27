using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Bomber.Game.Online;

if (args.Length is not (1 or 3)) throw new ArgumentException("Usage: Bomber.Release.Probe ORIGIN [EXPECTED_SERVER REGION]");
var origin = new Uri(args[0].TrimEnd('/') + "/");
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
http.DefaultRequestHeaders.Add(OnlineProtocol.HeaderName, OnlineProtocol.WireRevision);
string? token = null, lobbyId = null;
using var socket = new ClientWebSocket();
socket.Options.SetRequestHeader(OnlineProtocol.HeaderName, OnlineProtocol.WireRevision);
socket.Options.CollectHttpResponseDetails = true;
int checks = 0;
void Check(bool accepted, string description)
{
    if (!accepted) throw new InvalidOperationException("Release connection probe failed: " + description);
    checks++;
}
Uri Endpoint(string path) => new(origin.ToString().TrimEnd('/') + "/realtime/v1/" + path);
async Task<JsonElement> Request(HttpMethod method, string path, object? data, CancellationToken ct)
{
    using var request = new HttpRequestMessage(method, Endpoint(path));
    if (token is not null) request.Headers.Add("x-player-token", token);
    if (data is not null) request.Content = JsonContent.Create(data);
    using var response = await http.SendAsync(request, ct);
    if (method == HttpMethod.Post && path == "lobbies" && response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
    {
        // The node answered with the current release but placed no room: the host monitor has
        // excluded it, or it is full. The caller decides whether that target may defer.
        Check(response.Headers.TryGetValues(OnlineProtocol.HeaderName, out var deferredValues) && OnlineProtocol.Accepts(deferredValues), "matching worker response");
        using var refusal = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (refusal.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code) && code.GetString() == "no_capacity")
            throw new PlacementDeferredException();
    }
    Check(response.IsSuccessStatusCode, "current client API admission");
    Check(response.Headers.TryGetValues(OnlineProtocol.HeaderName, out var values) && OnlineProtocol.Accepts(values), "matching worker response");
    if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default;
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    return json.RootElement.Clone();
}
try
{
try
{
    var guest = await Request(HttpMethod.Post, "guests", new { displayName = "Release connection check" }, deadline.Token);
    token = guest.GetProperty("token").GetString();
    var created = await Request(HttpMethod.Post, "lobbies", new { game = "blast-room", visibility = "private", region = args.Length == 3 ? args[2] : null, settings = new { hosting = "server", bots = "1" } }, deadline.Token);
    lobbyId = created.GetProperty("lobby").GetProperty("lobbyId").GetString()!;
    if (args.Length == 3) Check(created.GetProperty("lobby").GetProperty("serverId").GetString() == args[1], "room is placed on the expected worker");
    var reservation = created.GetProperty("reservation");
    var route = new Uri(origin, reservation.GetProperty("connectUrl").GetString()!);
    Check(route.Scheme is "http" or "https" or "ws" or "wss", "supported socket route");
    var connect = new UriBuilder(route) { Scheme = route.Scheme is "https" or "wss" ? "wss" : "ws" };
    Check(origin.Scheme != "https" || connect.Scheme == "wss", "secure socket route");
    await socket.ConnectAsync(connect.Uri, deadline.Token);
    Check(socket.HttpResponseHeaders is not null && OnlineProtocol.Accepts(socket.HttpResponseHeaders
        .Where(header => header.Key.Equals(OnlineProtocol.HeaderName, StringComparison.OrdinalIgnoreCase)).SelectMany(header => header.Value)), "selected worker handshake");
    bool welcome = false, started = false, cluster = false;
    float? firstTime = null;
    bool advancing = false;
    byte[] buffer = new byte[8192];
    while (!advancing || !welcome || !cluster)
    {
        using var packet = new MemoryStream();
        WebSocketReceiveResult part;
        do
        {
            part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
            Check(part.MessageType == WebSocketMessageType.Text, "worker keeps the authenticated connection open");
            packet.Write(buffer, 0, part.Count);
            if (packet.Length > 1024 * 1024) throw new InvalidOperationException("Release probe frame too large");
        } while (!part.EndOfMessage);
        using var message = JsonDocument.Parse(packet.ToArray());
        var root = message.RootElement;
        var type = root.GetProperty("t").GetString();
        if (type == "sys.welcome") welcome = true;
        if (type == "party.status")
        {
            var status = root.GetProperty("d");
            cluster |= status.GetProperty("hosting").GetString() == "cluster";
            if (!started && status.GetProperty("canStart").GetBoolean())
            {
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { t = "party.start", d = new { } }).AsMemory(), WebSocketMessageType.Text, true, deadline.Token);
                started = true;
            }
        }
        if (type == "state" && root.GetProperty("d").GetProperty("phase").GetInt32() == 1)
        {
            var state = root.GetProperty("d");
            Check(state.GetProperty("players").GetArrayLength() == 2, "server match contains guest and bot");
            float time = state.GetProperty("timeLeft").GetSingle();
            firstTime ??= time;
            advancing = time < firstTime - .2f;
        }
    }
    Check(welcome, "authenticated sys.welcome from selected worker");
    Check(started && cluster && advancing, "expected worker runs an advancing server-hosted match");
}
finally
{
    // Cleanup has its own bounded budget even when the connection deadline expired.
    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(85));
    if (lobbyId is null && token is not null)
    {
        var me = await Request(HttpMethod.Get, "me", null, cleanup.Token);
        if (me.TryGetProperty("lobbyId", out var seat) && seat.ValueKind == JsonValueKind.String) lobbyId = seat.GetString();
    }
    if (lobbyId is not null)
    {
        await Request(HttpMethod.Post, $"lobbies/{Uri.EscapeDataString(lobbyId)}/leave", new { }, cleanup.Token);
        while (true)
        {
            var me = await Request(HttpMethod.Get, "me", null, cleanup.Token);
            if (!me.TryGetProperty("lobbyId", out var seat) || seat.ValueKind != JsonValueKind.String || seat.GetString() != lobbyId) break;
            await Task.Delay(50, cleanup.Token);
        }
        socket.Abort();
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint($"lobbies/{Uri.EscapeDataString(lobbyId)}"));
            request.Headers.Add("x-player-token", token);
            using var response = await http.SendAsync(request, cleanup.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) break;
            Check(response.IsSuccessStatusCode, "room retirement remains observable");
            using var room = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cleanup.Token));
            if (room.RootElement.GetProperty("state").GetString() is "closed" or "ended") break;
            await Task.Delay(1000, cleanup.Token);
        }
        Check(true, "owned room retired and released worker capacity");
    }
    socket.Abort();
}
}
catch (PlacementDeferredException)
{
    Console.WriteLine($"DEFERRED {checks} release admission checks; room placement answered no_capacity");
    return;
}
Console.WriteLine($"PASS {checks} release connection and cleanup checks");

sealed class PlacementDeferredException() : Exception("Room placement answered no_capacity");

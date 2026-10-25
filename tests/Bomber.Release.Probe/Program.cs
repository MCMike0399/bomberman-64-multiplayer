using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Bomber.Game;

if (args.Length != 1) throw new ArgumentException("Usage: Bomber.Release.Probe ORIGIN");
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
    Check(response.IsSuccessStatusCode, "current client API admission");
    Check(response.Headers.TryGetValues(OnlineProtocol.HeaderName, out var values) && OnlineProtocol.Accepts(values), "matching worker response");
    if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default;
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    return json.RootElement.Clone();
}
try
{
    var guest = await Request(HttpMethod.Post, "guests", new { displayName = "Release connection check" }, deadline.Token);
    token = guest.GetProperty("token").GetString();
    var created = await Request(HttpMethod.Post, "lobbies", new { game = "blast-room", visibility = "private", settings = new { bots = "0" } }, deadline.Token);
    lobbyId = created.GetProperty("lobby").GetProperty("lobbyId").GetString()!;
    var reservation = created.GetProperty("reservation");
    var route = new Uri(origin, reservation.GetProperty("connectUrl").GetString()!);
    Check(route.Scheme is "http" or "https" or "ws" or "wss", "supported socket route");
    var connect = new UriBuilder(route) { Scheme = route.Scheme is "https" or "wss" ? "wss" : "ws" };
    Check(origin.Scheme != "https" || connect.Scheme == "wss", "secure socket route");
    await socket.ConnectAsync(connect.Uri, deadline.Token);
    Check(socket.HttpResponseHeaders is not null && OnlineProtocol.Accepts(socket.HttpResponseHeaders
        .Where(header => header.Key.Equals(OnlineProtocol.HeaderName, StringComparison.OrdinalIgnoreCase)).SelectMany(header => header.Value)), "selected worker handshake");
    bool welcome = false;
    byte[] buffer = new byte[8192];
    for (int frame = 0; frame < 64 && !welcome; frame++)
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
        welcome = message.RootElement.GetProperty("t").GetString() == "sys.welcome";
    }
    Check(welcome, "authenticated sys.welcome from selected worker");
}
finally
{
    // Cleanup has its own bounded budget even when the connection deadline expired.
    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
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
    }
    socket.Abort();
}
Console.WriteLine($"PASS {checks} release connection and cleanup checks");

using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using Bomber.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

int checks = 0;
void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); checks++; }
long now = 100 * Stopwatch.Frequency;
var logger = new CaptureLogger<SocketProgressReporter>();
var reporter = new SocketProgressReporter(logger) { Clock = () => now };
var progress = reporter.Open("socket-test");
long started = reporter.BeginSend(progress, 128);
now += Stopwatch.Frequency; reporter.Observe(now);
Check(logger.Lines.Any(x => x.Contains("receive_silence")), "per-client receive silence is observable");
Check(logger.Lines.Any(x => x.Contains("send_stall") && x.Contains("pending_write_bytes=128")), "a blocked socket write reports its own bytes and age");
reporter.FinishedSend(progress, started, false);
Check(progress.SentFrames == 0 && logger.Lines.Any(x => x.Contains("outcome=failed")), "failed writes are never reported as successful sends");
Volatile.Write(ref progress.LastReceive, now); reporter.Observe(now);
Check(logger.Lines.Any(x => x.Contains("receive_resumed")), "receive recovery is correlated to the same connection");
int beforeBacklog = logger.Lines.Count;
for (int i = 0; i < 20; i++) reporter.Backlog(progress, 1000, 500);
Check(logger.Lines.Count == beforeBacklog + 1, "snapshot backlog reports are rate limited per client");
reporter.Closed(progress);

var socket = new FakeSocket();
var context = new DefaultHttpContext(); context.Connection.Id = "public-socket";
context.Request.Path = "/realtime/v1/connect"; context.Request.QueryString = new QueryString("?token=PRIVATE-TICKET");
context.Request.Headers.Authorization = "Bearer PRIVATE-AUTH";
context.Features.Set<IHttpWebSocketFeature>(new FakeFeature(socket));
var middleware = new ConnectionTelemetryMiddleware(async request =>
{
    using var observed = await request.WebSockets.AcceptWebSocketAsync();
    Check(!ReferenceEquals(observed, socket), "the actual WebSocket feature is passively wrapped");
    await observed.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { t = "sys.welcome", d = new
        { playerId = "guest-test", lobby = new { lobbyId = "lby-test", joinCode = "PRIVATE-CODE" } } }).AsMemory(), WebSocketMessageType.Text, true, default);
    await observed.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { t = "state", d = new
        { serverTime = DateTimeOffset.UtcNow.AddSeconds(-2).ToUnixTimeMilliseconds() } }).AsMemory(), WebSocketMessageType.Text, true, default);
});
await middleware.InvokeAsync(context, reporter);
Check(socket.Writes == 2 && socket.Disposed, "observability preserves writes and owned disposal");
Check(logger.Lines.Any(x => x.Contains("snapshot_backlog") && x.Contains("room=lby-test") && x.Contains("player=guest-test")),
    "queued snapshot age is correlated by non-secret room/player IDs");
Check(!logger.Lines.Any(x => x.Contains("PRIVATE-")), "headers, reservation tickets, join codes and payloads are absent from server diagnostics");
Console.WriteLine($"PASS {checks} server connection telemetry checks");

sealed class CaptureLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, error));
}
sealed class FakeFeature(FakeSocket socket) : IHttpWebSocketFeature
{
    public bool IsWebSocketRequest => true;
    public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => Task.FromResult<WebSocket>(socket);
}
sealed class FakeSocket : WebSocket
{
    public int Writes;
    public bool Disposed;
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;
    public override WebSocketState State => WebSocketState.Open;
    public override void Abort() { }
    public override void Dispose() => Disposed = true;
    public override Task CloseAsync(WebSocketCloseStatus status, string? reason, CancellationToken ct) => Task.CompletedTask;
    public override Task CloseOutputAsync(WebSocketCloseStatus status, string? reason, CancellationToken ct) => Task.CompletedTask;
    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) => Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct) { Writes++; return Task.CompletedTask; }
}

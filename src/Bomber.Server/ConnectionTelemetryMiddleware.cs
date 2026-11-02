using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace Bomber.Server;

/// <summary>Wrap only game WebSockets. No credentials, headers, addresses or payloads are logged.</summary>
public sealed class ConnectionTelemetryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SocketProgressReporter reporter)
    {
        var feature = context.Features.Get<IHttpWebSocketFeature>();
        if (feature is { IsWebSocketRequest: true } && context.Request.Path.StartsWithSegments("/realtime/v1/connect"))
            context.Features.Set<IHttpWebSocketFeature>(new ObservedFeature(feature, context.Connection.Id, reporter));
        await next(context);
    }
    private sealed class ObservedFeature(IHttpWebSocketFeature inner, string id, SocketProgressReporter reporter) : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => inner.IsWebSocketRequest;
        public async Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => new ObservedSocket(await inner.AcceptAsync(context), reporter.Open(id), reporter);
    }
    private sealed class ObservedSocket(WebSocket inner, SocketProgressReporter.Progress progress, SocketProgressReporter reporter) : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => inner.CloseStatus;
        public override string? CloseStatusDescription => inner.CloseStatusDescription;
        public override string? SubProtocol => inner.SubProtocol;
        public override WebSocketState State => inner.State;
        public override void Abort() => inner.Abort();
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => inner.CloseAsync(status, description, ct);
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => inner.CloseOutputAsync(status, description, ct);
        public override void Dispose() { reporter.Closed(progress); inner.Dispose(); }
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        { var result = await inner.ReceiveAsync(buffer, ct); if (result.Count > 0) Volatile.Write(ref progress.LastReceive, reporter.Clock()); return result; }
        public override async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken ct = default)
        { var result = await inner.ReceiveAsync(buffer, ct); if (result.Count > 0) Volatile.Write(ref progress.LastReceive, reporter.Clock()); return result; }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct) => Send(buffer.AsMemory(), type, end, ct).AsTask();
        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct = default) => Send(buffer, type, end, ct);
        private async ValueTask Send(ReadOnlyMemory<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct)
        {
            if (type == WebSocketMessageType.Text && end) Inspect(buffer);
            long started = reporter.BeginSend(progress, buffer.Length); bool succeeded = false;
            try { await inner.SendAsync(buffer, type, end, ct); succeeded = true; }
            finally { reporter.FinishedSend(progress, started, succeeded); }
        }
        private void Inspect(ReadOnlyMemory<byte> bytes)
        {
            try
            {
                var reader = new Utf8JsonReader(bytes.Span);
                string? type = null;
                while (reader.Read())
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;
                    if (reader.ValueTextEquals("t") && reader.Read())
                    {
                        type = reader.GetString();
                        if (type is not ("state" or "sys.welcome")) return;
                        if (type == "sys.welcome")
                        {
                            using var document = JsonDocument.Parse(bytes);
                            var data = document.RootElement.GetProperty("d");
                            if (data.TryGetProperty("playerId", out var player)) progress.Player = SafeId(player.GetString());
                            if (data.TryGetProperty("lobby", out var lobby) && lobby.TryGetProperty("lobbyId", out var room)) progress.Room = SafeId(room.GetString());
                            return;
                        }
                    }
                    if (type == "state" && reader.ValueTextEquals("serverTime") && reader.Read() && reader.TryGetInt64(out long time))
                    { reporter.Backlog(progress, Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - time), bytes.Length); return; }
                }
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException) { }
        }
        private static string SafeId(string? value) => value is { Length: > 0 and <= 80 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? value : "-";
    }
}

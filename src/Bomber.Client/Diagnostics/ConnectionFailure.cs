using System.Net.Sockets;
using System.Net.WebSockets;

namespace Bomber.Client.Diagnostics;

internal static class ConnectionFailure
{
    // Exception messages and socket close descriptions may contain hostnames,
    // addresses, room codes or server-provided text. Persist only fixed categories.
    internal static string Describe(Exception error) => error switch
    {
        SocketException socket => $"SocketException code={socket.SocketErrorCode}",
        HttpRequestException http => $"HttpRequestException code={http.HttpRequestError}" +
            (http.InnerException is SocketException inner ? $" socket={inner.SocketErrorCode}" : ""),
        WebSocketException socket => $"WebSocketException code={socket.WebSocketErrorCode}",
        OperationCanceledException => "OperationCanceledException code=cancelled",
        IOException => "IOException code=transport-interrupted",
        System.Text.Json.JsonException => "JsonException code=invalid-frame",
        InvalidOperationException => "InvalidOperationException code=invalid-session",
        _ => "Exception code=unknown",
    };
}

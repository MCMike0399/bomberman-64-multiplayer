using System.Net.Sockets;
using System.Net.WebSockets;
using Bomber.Client.Diagnostics;

internal static class ConnectionFailureChecks
{
    internal static async Task<int> Run()
    {
        var failures = new Exception[]
        {
            new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (127.0.0.1:1234)", new SocketException((int)SocketError.ConnectionRefused)),
            new HttpRequestException(HttpRequestError.NameResolutionError, "Name not known (private-host:443)"),
            new IOException("No route to host ([fd00:1234::1]:8443)"),
            new WebSocketException(WebSocketError.ConnectionClosedPrematurely, "room-code=AB12CD token=secret-playtester-value"),
            new OperationCanceledException("Timed out connecting to private-host.example:443"),
        };
        string folder = Path.Combine(Path.GetTempPath(), "blast-failure-privacy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new ConnectionJournal(folder);
            foreach (var error in failures) journal.Connection(2, "interrupted: " + ConnectionFailure.Describe(error));
            await journal.DisposeAsync();
            string text = File.ReadAllText(journal.Path);
            foreach (string secret in new[] { "127.0.0.1", "1234", "private-host", "fd00", "8443", "AB12CD", "secret-playtester-value" })
                if (text.Contains(secret, StringComparison.Ordinal))
                    throw new Exception("Persisted structured failure exposed private endpoint, room code or credential: " + secret);
            foreach (var error in failures)
                if (!ConnectionFailure.Describe(error).Contains(error.GetType().Name, StringComparison.Ordinal))
                    throw new Exception("Failure category was lost");
            Console.WriteLine("PASS 12 structured failure privacy checks");
            return 12;
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

/// <summary>Owned loopback transport witness. Drops one client's actual TCP connections, never a server.</summary>
internal sealed class ConnectionDropProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Uri _target;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, (TcpClient Client, TcpClient Server)> _connections = new();
    private readonly List<Task> _pipes = [];
    private readonly Task _accept;
    private int _id;
    public Uri Origin { get; }
    public ConnectionDropProxy(Uri target)
    {
        if (!target.IsLoopback || target.Scheme != "http") throw new ArgumentException("Only owned HTTP loopback test servers are supported.");
        _target = target; _listener.Start();
        Origin = new Uri("http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port);
        _accept = Accept();
    }
    private async Task Accept()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _pipes.Add(Pipe(client));
            }
        }
        catch (Exception e) when (_stop.IsCancellationRequested && e is OperationCanceledException or SocketException) { }
    }
    private async Task Pipe(TcpClient client)
    {
        using (client)
        using (var server = new TcpClient())
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            int id = Interlocked.Increment(ref _id);
            try
            {
                await server.ConnectAsync(_target.Host, _target.Port, lifetime.Token);
                _connections[id] = (client, server);
                var inbound = client.GetStream().CopyToAsync(server.GetStream(), lifetime.Token);
                var outbound = server.GetStream().CopyToAsync(client.GetStream(), lifetime.Token);
                await Task.WhenAny(inbound, outbound);
                await lifetime.CancelAsync(); client.Close(); server.Close();
                try { await Task.WhenAll(inbound, outbound); }
                catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            finally { _connections.TryRemove(id, out _); }
        }
    }
    public void DropConnections()
    { foreach (var pair in _connections.Values) { pair.Client.Dispose(); pair.Server.Dispose(); } }
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync(); _listener.Stop(); DropConnections();
        await _accept; await Task.WhenAll(_pipes); _stop.Dispose();
    }
}

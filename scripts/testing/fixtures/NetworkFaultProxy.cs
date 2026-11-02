using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace BlastCircuit.Testing.Fixtures;

/// <summary>Owned loopback fault proxy. Pauses directions or resets only its own test client connections.</summary>
internal sealed class NetworkFaultProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Uri _target;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, (TcpClient Client, TcpClient Server)> _connections = new();
    private readonly List<Task> _pipes = [];
    private readonly Task _accept;
    private int _id;
    private long _pauseUpUntil, _pauseDownUntil, _jitterUntil;
    private int _jitterStep;
    public int ActiveConnections => _connections.Count;
    public void Pause(TimeSpan duration, bool upstream, bool downstream)
    {
        long until = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
        if (upstream) Volatile.Write(ref _pauseUpUntil, until);
        if (downstream) Volatile.Write(ref _pauseDownUntil, until);
    }
    public void Jitter(TimeSpan duration) => Volatile.Write(ref _jitterUntil,
        System.Diagnostics.Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * System.Diagnostics.Stopwatch.Frequency));
    public void ClearFaults()
    {
        Volatile.Write(ref _pauseUpUntil, 0); Volatile.Write(ref _pauseDownUntil, 0);
        Volatile.Write(ref _jitterUntil, 0);
    }
    private async Task Forward(Stream source, Stream destination, bool upstream, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (await source.ReadAsync(buffer, ct) is int count && count > 0)
        {
            long until = upstream ? Volatile.Read(ref _pauseUpUntil) : Volatile.Read(ref _pauseDownUntil);
            double remaining = (double)(until - System.Diagnostics.Stopwatch.GetTimestamp()) / System.Diagnostics.Stopwatch.Frequency;
            if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds(remaining), ct);
            if (System.Diagnostics.Stopwatch.GetTimestamp() < Volatile.Read(ref _jitterUntil))
                await Task.Delay(new[] { 300, 800, 450, 600 }[Interlocked.Increment(ref _jitterStep) % 4], ct);
            await destination.WriteAsync(buffer.AsMemory(0, count), ct);
        }
    }
    public Uri Origin { get; }
    public NetworkFaultProxy(Uri target)
    {
        if (!target.IsLoopback || target.Scheme != "http") throw new ArgumentException("Only owned HTTP loopback test servers are supported.");
        _target = target; _listener.Start();
        Origin = new Uri("http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port);
        // Constructor may run on Godot's render dispatcher. Transport must
        // continue when shader compilation or a slow frame stalls that thread.
        _accept = Task.Run(Accept);
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
                var inbound = Forward(client.GetStream(), server.GetStream(), true, lifetime.Token);
                var outbound = Forward(server.GetStream(), client.GetStream(), false, lifetime.Token);
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

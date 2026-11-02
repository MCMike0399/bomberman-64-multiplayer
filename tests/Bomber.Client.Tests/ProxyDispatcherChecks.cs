using System.Net;
using System.Net.Sockets;
using BlastCircuit.Testing.Fixtures;

internal static class ProxyDispatcherChecks
{
    internal static async Task<int> Run()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var echo = Task.Run(async () =>
        {
            try
            {
                using var peer = await listener.AcceptTcpClientAsync(stop.Token);
                var data = new byte[1];
                int count = await peer.GetStream().ReadAsync(data, stop.Token);
                await peer.GetStream().WriteAsync(data.AsMemory(0, count), stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        var dispatcher = new PausedDispatcher();
        var previous = SynchronizationContext.Current;
        NetworkFaultProxy proxy;
        try
        {
            SynchronizationContext.SetSynchronizationContext(dispatcher);
            proxy = new NetworkFaultProxy(new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(proxy.Origin.Host, proxy.Origin.Port, stop.Token);
            await client.GetStream().WriteAsync(new byte[] { 42 }, stop.Token);
            var received = new byte[1];
            int count = await client.GetStream().ReadAsync(received, stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            if (count != 1 || received[0] != 42) throw new Exception("Proxy did not preserve traffic");
            Console.WriteLine("PASS proxy traffic continues while the rendering dispatcher is stalled");
            return 1;
        }
        finally
        {
            dispatcher.Resume(); await proxy.DisposeAsync();
            await stop.CancelAsync(); listener.Stop(); await echo;
        }
    }
    private sealed class PausedDispatcher : SynchronizationContext
    {
        private readonly object _gate = new();
        private readonly List<(SendOrPostCallback Callback, object? State)> _pending = [];
        private bool _resumed;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_gate)
            {
                if (!_resumed) { _pending.Add((callback, state)); return; }
            }
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
        internal void Resume()
        {
            lock (_gate)
            {
                _resumed = true;
                foreach (var item in _pending) ThreadPool.QueueUserWorkItem(_ => item.Callback(item.State));
                _pending.Clear();
            }
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using Bomber.Client;

internal static class UiContextNetworkChecks
{
    public static Task Run(Uri server) => Task.Run(() =>
    {
        var previous = SynchronizationContext.Current;
        var ui = new QueuedContext();
        SynchronizationContext.SetSynchronizationContext(ui);
        NetworkSession? session = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var connect = NetworkSession.ConnectAsync(server, "UI context probe", new SessionSettings(Bots: 0),
                NetworkMode.CreatePrivate, cancellationToken: deadline.Token);
            ui.Complete(connect);
            session = connect.GetAwaiter().GetResult();
            int before = session.ConnectionQualitySamples;
            // Deliberately stop pumping the caller's UI context. Transport pings
            // must still be sent and received by the independent socket worker.
            Thread.Sleep(1200);
            if (session.ConnectionQualitySamples <= before)
                throw new InvalidOperationException("Socket I/O stalled with the caller's UI synchronization context.");
            Console.WriteLine("PASS socket transport advances while the caller UI context is blocked");
        }
        finally
        {
            try { if (session is not null) ui.Complete(session.DisposeAsync().AsTask()); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
    });

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
        public void Complete(Task task)
        {
            var clock = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (clock.Elapsed.TotalSeconds > 20) throw new TimeoutException("UI-context network fixture did not complete.");
                if (_queue.TryTake(out var next, 10)) next.Callback(next.State);
            }
            task.GetAwaiter().GetResult();
        }
    }
}

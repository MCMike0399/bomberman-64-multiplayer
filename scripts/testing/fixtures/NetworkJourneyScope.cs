using System.Diagnostics;

namespace BlastCircuit.Testing.Fixtures;

/// <summary>Owns network journey fixtures and reports phase and teardown timings.</summary>
internal sealed class NetworkJourneyScope(Action<string, string, double> report, TimeSpan? cleanupBudget = null) : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _resources = [];
    private readonly List<Exception> _errors = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TimeSpan _cleanupBudget = Budget(cleanupBudget ?? TimeSpan.FromSeconds(30));
    private Stopwatch? _cleanup;

    private static TimeSpan Budget(TimeSpan value) => value > TimeSpan.Zero && value <= TimeSpan.FromSeconds(30)
        ? value : throw new ArgumentOutOfRangeException(nameof(value), "Cleanup must be bounded by at most 30 seconds.");

    public Lease<T> Own<T>(string name, T resource) where T : IAsyncDisposable
    {
        var lease = new Lease<T>(this, name, resource);
        _resources.Add(lease);
        Phase(name + "-ready");
        return lease;
    }

    public void Phase(string name) => report(name, "phase", _clock.Elapsed.TotalSeconds);

    public void BeginCleanup()
    {
        if (_cleanup is not null) return;
        _cleanup = Stopwatch.StartNew();
        report("fixtures", "cleanup-begin", _clock.Elapsed.TotalSeconds);
    }

    public async ValueTask DisposeAsync()
    {
        BeginCleanup();
        foreach (var resource in Enumerable.Reverse(_resources)) await resource.DisposeAsync();
        report("fixtures", "cleanup-end", _clock.Elapsed.TotalSeconds);
        if (_errors.Count > 0) throw new AggregateException("Network journey cleanup failed", _errors);
    }

    private async ValueTask DisposeOne(string name, IAsyncDisposable resource)
    {
        BeginCleanup();
        report(name, "cleanup-start", _clock.Elapsed.TotalSeconds);
        try
        {
            var remaining = _cleanupBudget - _cleanup!.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("Aggregate fixture cleanup budget exhausted: " + name);
            await resource.DisposeAsync().AsTask().WaitAsync(remaining);
            report(name, "cleanup-complete", _clock.Elapsed.TotalSeconds);
        }
        catch (Exception error)
        {
            _errors.Add(error);
            report(name, error is TimeoutException ? "cleanup-timeout" : "cleanup-failed", _clock.Elapsed.TotalSeconds);
        }
    }

    internal sealed class Lease<T>(NetworkJourneyScope owner, string name, T resource) : IAsyncDisposable where T : IAsyncDisposable
    {
        private bool _disposed;
        public T Resource => resource;
        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            return owner.DisposeOne(name, resource);
        }
    }
}

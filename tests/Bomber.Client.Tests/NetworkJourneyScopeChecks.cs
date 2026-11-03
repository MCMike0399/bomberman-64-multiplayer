using BlastCircuit.Testing.Fixtures;

internal static class NetworkJourneyScopeChecks
{
    internal static async Task<int> Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        var order = new List<string>();
        var events = new List<(string Name, string State, double Seconds)>();
        var scope = new NetworkJourneyScope((name, state, seconds) => events.Add((name, state, seconds)));
        scope.Own("proxy", new Fixture(() => { order.Add("proxy"); return Task.CompletedTask; }));
        scope.Own("peer", new Fixture(() => { order.Add("peer"); return Task.CompletedTask; }));
        await scope.DisposeAsync();
        Check(order.SequenceEqual(["peer", "proxy"]), "owned fixtures retain reverse acquisition cleanup order");
        Check(events.Count(e => e.State == "cleanup-complete") == 2, "every completed fixture has teardown timing");

        order.Clear();
        var lexical = new NetworkJourneyScope((_, _, _) => { });
        await using (lexical.Own("outer", new Fixture(() => { order.Add("outer"); return Task.CompletedTask; })))
        {
            try
            {
                await using var inner = lexical.Own("inner", new Fixture(() => { order.Add("inner"); return Task.CompletedTask; }));
            }
            finally { order.Add("ui"); }
        }
        await lexical.DisposeAsync();
        Check(order.SequenceEqual(["inner", "ui", "outer"]), "instrumentation preserves the original nested disposal and UI cleanup order without double disposal");

        var failed = new NetworkJourneyScope((_, _, _) => { });
        bool remainingReleased = false;
        failed.Own("remaining", new Fixture(() => { remainingReleased = true; return Task.CompletedTask; }));
        failed.Own("failure", new Fixture(() => throw new IOException("retained fixture failure")));
        bool retainedFailure = false;
        try { await failed.DisposeAsync(); }
        catch (AggregateException error) { retainedFailure = error.InnerExceptions.Any(e => e is IOException); }
        Check(retainedFailure && remainingReleased, "a disposal error is retained while remaining owned fixtures still clean up");

        var bounded = new NetworkJourneyScope((_, _, _) => { }, TimeSpan.FromMilliseconds(100));
        for (int i = 0; i < 3; i++) bounded.Own("slow-" + i, new Fixture(() => Task.Delay(80)));
        bool budgetFailed = false;
        try { await bounded.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (AggregateException error) { budgetFailed = error.InnerExceptions.Any(e => e is TimeoutException); }
        Check(budgetFailed, "aggregate cleanup budget must fail across three individually short disposals");

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stalled = new NetworkJourneyScope((_, _, _) => { }, TimeSpan.FromMilliseconds(50));
        stalled.Own("stalled", new Fixture(() => release.Task));
        bool stalledFailed = false;
        try
        {
            try { await stalled.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (AggregateException error) { stalledFailed = error.InnerExceptions.Any(e => e is TimeoutException); }
        }
        finally { release.TrySetResult(); }
        Check(stalledFailed, "a non-completing fixture fails the journey instead of blocking its final receipt");
        Console.WriteLine($"PASS {checks} network journey cleanup checks");
        return checks;
    }

    private sealed class Fixture(Func<Task> dispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(dispose());
    }
}

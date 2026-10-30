using BlastCircuit.App;

static class FinalizerDrainChecks
{
    public static void Run(Action<bool,string> check)
    {
        int caller=Environment.CurrentManagedThreadId;bool dedicated=false;
        var complete=ManagedFinalizerDrain.Run(TimeSpan.FromSeconds(2),()=>
        {
            dedicated=Environment.CurrentManagedThreadId!=caller&&!Thread.CurrentThread.IsThreadPoolThread;
            return 17;
        });
        check(complete.Completed&&complete.PendingAtCollection==17&&dedicated,"Finalizer collection completes on an owned dedicated waiter");
        var failed=ManagedFinalizerDrain.Run(TimeSpan.FromSeconds(2),()=>throw new InvalidOperationException("injected"));
        check(!failed.Completed&&failed.Detail==nameof(InvalidOperationException),"Collector failure is never a clean shutdown");
        using var release=new ManualResetEventSlim();using var finished=new ManualResetEventSlim();
        try
        {
            var timeout=ManagedFinalizerDrain.Run(TimeSpan.FromMilliseconds(30),()=>
            {
                release.Wait();finished.Set();return 1;
            });
            check(!timeout.Completed&&timeout.PendingAtCollection is null,"An unfinished collector returns bounded failure");
        }
        finally {release.Set();if(!finished.Wait(TimeSpan.FromSeconds(2)))throw new Exception("Test collector did not exit");}
    }
}

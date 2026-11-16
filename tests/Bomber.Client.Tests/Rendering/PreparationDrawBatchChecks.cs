using BlastCircuit.Rendering;

internal static class PreparationDrawBatchChecks
{
    public static async Task<int> Run()
    {
        int checks=0;
        void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
        Check(PreparationDrawBatch.ExpectedDraws(6)==31&&PreparationDrawBatch.ExpectedDraws(10)==47,
            "the explicit fixed7/per-miniature4 contract supports main's six and the final ten miniatures");
        var mainClock=new DeadlineClock();int mainDraws=0;
        using(var mainBatch=new PreparationDrawBatch())
            for(int fence=0;fence<PreparationDrawBatch.ExpectedDraws(6);fence++)
            {
                var pending=mainBatch.CompleteAsync(new TaskCompletionSource().Task,mainClock.Deadline,()=>{mainDraws++;return Task.CompletedTask;},default);
                mainClock.ExpirePending();await pending;
            }
        Check(mainDraws==31&&mainClock.Count==1&&mainClock.Milliseconds==500,"main's real six-miniature fence count retains one deadline and every render");
        var never=new TaskCompletionSource();
        var clock=new DeadlineClock();int renders=0;
        using(var batch=new PreparationDrawBatch())
        {
            for(int fence=0;fence<47;fence++)
            {
                var pending=batch.CompleteAsync(never.Task,clock.Deadline,()=>{renders++;return Task.CompletedTask;},default);
                clock.ExpirePending();await pending;
            }
            Check(clock.Milliseconds==500&&clock.Count==1,"47 starved preparation fences pay exactly one 500ms callback deadline");
            Check(renders==47&&batch.ForcedDraws==47&&batch.NormalWaits==1,"every starved fence still invokes forced rendering, including the first");
            Check(clock.Milliseconds<20_000,"a single-batch callback stall stays inside the unchanged 20-second preparation bound");
            batch.Dispose();Check(!batch.ForceRendering&&batch.Completed,"completion clears active forced-render policy");
            bool disposed=false;try{await batch.CompleteAsync(never.Task,clock.Deadline,()=>Task.CompletedTask,default);}catch(ObjectDisposedException){disposed=true;}
            Check(disposed,"completed batches cannot silently be reused by another preparation");
        }
        // Reference control for the old lifetime: a fresh policy per fence cannot
        // remember the prior callback miss. Native old-path red is retained too.
        var legacyClock=new DeadlineClock();int legacyRenders=0;
        for(int fence=0;fence<47;fence++)
        {
            using var perFence=new PreparationDrawBatch();
            var pending=perFence.CompleteAsync(never.Task,legacyClock.Deadline,()=>{legacyRenders++;return Task.CompletedTask;},default);
            legacyClock.ExpirePending();await pending;
        }
        Check(legacyClock.Milliseconds==23_500&&legacyClock.Milliseconds>20_000&&legacyRenders==47,
            "the old per-fence lifetime reproduces accumulated deadline failure without skipping a render");

        using(var visible=new PreparationDrawBatch())
        {
            int forced=0,waits=0;
            for(int fence=0;fence<47;fence++)await visible.CompleteAsync(Task.CompletedTask,()=>{waits++;return never.Task;},()=>{forced++;return Task.CompletedTask;},default);
            Check(waits==47&&forced==0&&visible.NormalWaits==47&&!visible.ForceRendering,"a fresh visible batch retains every normal draw fence and never forces rendering");
        }
        using(var visible=new PreparationDrawBatch())
        {
            var signal=new TaskCompletionSource();var timing=new DeadlineClock();int forced=0;
            var pending=visible.CompleteAsync(signal.Task,timing.Deadline,()=>{forced++;return Task.CompletedTask;},default);
            Check(!pending.IsCompleted,"a normal pending draw remains fenced until its actual completion");
            signal.SetResult();await pending;
            Check(forced==0&&!visible.ForceRendering&&timing.Milliseconds==0,"a normal callback arriving before the deadline retains the normal path");
        }
        using(var stop=new CancellationTokenSource())using(var cancelled=new PreparationDrawBatch())
        {
            int forced=0;stop.Cancel();bool observed=false;
            try{await cancelled.CompleteAsync(never.Task,clock.Deadline,()=>{forced++;return Task.CompletedTask;},stop.Token);}catch(OperationCanceledException){observed=true;}
            Check(observed&&forced==0&&cancelled.NormalWaits==0,"pre-cancelled preparation never waits or renders");
        }
        using(var stop=new CancellationTokenSource())using(var cancelled=new PreparationDrawBatch())
        {
            int forced=0;bool observed=false;
            var pending=cancelled.CompleteAsync(never.Task,()=>Task.Delay(Timeout.Infinite,stop.Token),()=>{forced++;return Task.CompletedTask;},stop.Token);
            stop.Cancel();try{await pending;}catch(OperationCanceledException){observed=true;}
            Check(observed&&forced==0&&!cancelled.ForceRendering,"cancellation during a missing callback prevents fallback rendering");
        }
        using(var stop=new CancellationTokenSource())using(var forced=new PreparationDrawBatch())
        {
            var timing=new DeadlineClock();int draws=0;
            var pending=forced.CompleteAsync(never.Task,timing.Deadline,()=>{draws++;return Task.CompletedTask;},stop.Token);
            timing.ExpirePending();await pending;stop.Cancel();bool observed=false;
            try{await forced.CompleteAsync(never.Task,timing.Deadline,()=>{draws++;return Task.CompletedTask;},stop.Token);}catch(OperationCanceledException){observed=true;}
            Check(observed&&draws==1&&timing.Count==1,"cancellation in forced mode prevents the next real draw without another deadline");
        }
        Console.WriteLine($"PASS {checks} preparation batch draw/cancellation checks");return checks;
    }

    private sealed class DeadlineClock
    {
        private readonly Queue<TaskCompletionSource> _pending=[];
        public int Count {get;private set;}
        public int Milliseconds {get;private set;}
        public Task Deadline(){Count++;var source=new TaskCompletionSource();_pending.Enqueue(source);return source.Task;}
        public void ExpirePending(){if(_pending.TryDequeue(out var source)){Milliseconds+=500;source.SetResult();}}
    }
}

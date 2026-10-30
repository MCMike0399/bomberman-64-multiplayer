using System.Diagnostics;
namespace BlastCircuit.App;

/// <summary>Finish queued managed resource destructors while native Godot is still alive.</summary>
public static class ManagedFinalizerDrain
{
    public sealed record Result(bool Completed,long? PendingAtCollection,double Milliseconds,string Detail);

    public static Result Run(TimeSpan budget)=>Run(budget,()=>
    {
        GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,blocking:true,compacting:false);
        long pending=GC.GetGCMemoryInfo().FinalizationPendingCount;
        GC.WaitForPendingFinalizers();
        return pending;
    });

    internal static Result Run(TimeSpan budget,Func<long> collect)
    {
        if(budget<=TimeSpan.Zero||budget>TimeSpan.FromSeconds(30))throw new ArgumentOutOfRangeException(nameof(budget));
        var clock=Stopwatch.StartNew();long pending=0;Exception? failure=null;
        // Do not depend on the .NET pool, and do not call Godot from this waiter.
        // A stuck finalizer must be reported, never turn window close into a hang.
        var worker=new Thread(()=>{try{pending=collect();}catch(Exception error){failure=error;}})
            {IsBackground=true,Name="Blast shutdown managed-finalizers"};
        worker.Start();bool joined=worker.Join(budget);
        return new(joined&&failure is null,joined&&failure is null?pending:null,clock.Elapsed.TotalMilliseconds,
            !joined?"managed finalizer drain exceeded its bound":failure is not null?failure.GetType().Name:"queued managed finalizers completed");
    }
}

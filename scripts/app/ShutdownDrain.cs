using System.Diagnostics;
using Godot;
namespace BlastCircuit.App;

/// <summary>Drain pre-existing low-priority work before Godot enters its shutdown barrier.</summary>
public static class ShutdownDrain
{
    public sealed record Result(bool Completed,int Workers,int LowPrioritySlots,int StartedFences,double Milliseconds,string Detail);

    // Godot4.7.2 uses the desktop processor count when max_threads is negative.
    // Its low-priority slots are clamped to [1, initial_threads-1], then truncated.
    public static Result Run(TimeSpan budget)
    {
        int workers=ProjectSettings.GetSetting("threading/worker_pool/max_threads",-1).AsInt32();
        if(workers<0)workers=OS.GetProcessorCount();
        float ratio=ProjectSettings.GetSetting("threading/worker_pool/low_priority_thread_ratio",.3).AsSingle();
        if(workers==0)return new(true,0,0,0,0,"caller-thread pool has no background work");
        if(workers<0||workers>256||!float.IsFinite(ratio)||budget<=TimeSpan.Zero)
            throw new InvalidOperationException("Unsupported shutdown worker-pool configuration");
        int slots=workers==1?1:(int)Math.Clamp(workers*ratio,1,workers-1);
        var clock=Stopwatch.StartNew();var gate=new object();int started=0;bool release=false;
        var tasks=new List<long>();bool arrived=false;
        double Remaining()=>Math.Max(0,budget.TotalMilliseconds-clock.Elapsed.TotalMilliseconds);
        try
        {
            // Every low-priority slot must reach a FIFO fence. Until then, earlier
            // work can occupy a slot. Callbacks use only managed synchronization.
            for(int index=0;index<slots;index++)
                tasks.Add(WorkerThreadPool.AddTask(Callable.From(()=>
                {
                    lock(gate)
                    {
                        started++;Monitor.PulseAll(gate);
                        while(!release&&Remaining()>0)Monitor.Wait(gate,TimeSpan.FromMilliseconds(Remaining()));
                    }
                }),false,"Blast shutdown low-priority fence"));
            lock(gate)
            {
                while(started<slots&&Remaining()>0)Monitor.Wait(gate,TimeSpan.FromMilliseconds(Remaining()));
                arrived=started==slots;
            }
        }
        finally {lock(gate){release=true;Monitor.PulseAll(gate);}}
        bool claimed=true;
        foreach(long task in tasks)
        {
            while(!WorkerThreadPool.IsTaskCompleted(task)&&Remaining()>0)Thread.Sleep(1);
            if(WorkerThreadPool.IsTaskCompleted(task))WorkerThreadPool.WaitForTaskCompletion(task);
            else claimed=false;
        }
        return new(arrived&&claimed,workers,slots,started,clock.Elapsed.TotalMilliseconds,
            arrived&&claimed?"low-priority FIFO fences completed and reclaimed":"shutdown drain exceeded its bound");
    }
}

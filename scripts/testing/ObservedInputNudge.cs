using Bomber.Client.Sessions;
using Bomber.Game.Matches;

namespace BlastCircuit.Testing;

/// <summary>QA-only movement ownership: release on observed sends, with a corridor-derived cutoff.</summary>
internal sealed class ObservedInputNudge(PlayerInput press,int generation,long baselineSamples,long started,long pendingTicks,long corridorTicks)
{
    internal sealed record Release(string Reason,bool Delivered,long Timestamp,InputSendObservation? Observation);
    private Release? _release;
    private long _firstCapture;
    internal Release? Decision=>Volatile.Read(ref _release);

    internal bool Pressed(InputSendObservation? progress,long now,int currentGeneration)
    {
        if(Decision is not null)return false;
        if(currentGeneration!=generation){Finish("session-changed",false,now,progress);return false;}
        if(progress is { } sample&&sample.Generation==generation&&sample.Input.X==press.X&&sample.Input.Z==press.Z&&sample.CapturedAt>=started)
            Interlocked.CompareExchange(ref _firstCapture,Math.Max(started,sample.RunStartedAt),0);
        long first=Volatile.Read(ref _firstCapture);
        if(now>=(first==0?started+pendingTicks:first+corridorTicks))
        {Finish(first==0?"sender-timeout":"corridor-cutoff",false,now,progress);return false;}
        if(progress is { } sent&&sent.CapturedAt>=started&&sent.ContainsNewSends(press,generation,baselineSamples,2))
        {Finish("two-sent-samples",true,now,progress);return false;}
        return Decision is null;
    }
    internal void Cancel(long now)=>Finish("cancelled",false,now,null);
    private void Finish(string reason,bool delivered,long now,InputSendObservation? sample)=>
        Interlocked.CompareExchange(ref _release,new(reason,delivered,now,sample),null);
}

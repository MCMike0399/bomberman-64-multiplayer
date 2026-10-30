using Bomber.Game.Matches;

namespace Bomber.Client.Sessions;

/// <summary>Read-only socket input sampling diagnostics. Counts advance only after a successful send.</summary>
public sealed record InputSendObservation(int Generation,PlayerInput Input,long CapturedSequence,
    long CapturedAt,long RunStartedAt,long SentSequence,long TotalSentSamples,long MatchingSentSamples)
{
    public bool ContainsNewSends(PlayerInput expected,int generation,long afterSamples,int count)=>
        count>0&&Generation==generation&&Input==expected&&TotalSentSamples-afterSamples>=count&&MatchingSentSamples>=count;
}

/// <summary>Records an existing sender's work without queuing or altering any input.</summary>
internal sealed class InputSendTracker(int generation)
{
    private PlayerInput? _lastCaptured,_lastSent;
    private long _runStarted,_total,_matching,_sentSequence;
    internal InputSendObservation Capture(PlayerInput input,long sequence,long timestamp)
    {
        if(_lastCaptured is not {} previous||previous.X!=input.X||previous.Z!=input.Z)_runStarted=timestamp;
        _lastCaptured=input;
        return new(generation,input,sequence,timestamp,_runStarted,_sentSequence,_total,_lastSent==input?_matching:0);
    }
    internal InputSendObservation Complete(InputSendObservation sample)
    {
        _matching=_lastSent==sample.Input?_matching+1:1;
        _lastSent=sample.Input;_sentSequence=sample.CapturedSequence;_total++;
        return sample with{SentSequence=_sentSequence,TotalSentSamples=_total,MatchingSentSamples=_matching};
    }
}

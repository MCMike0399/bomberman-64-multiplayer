namespace BlastCircuit.Testing.Fixtures;

/// <summary>Authority/ACK progress independent of how often a renderer drains coalesced state.</summary>
internal sealed class NetworkDirectionWitness
{
    private readonly long _initialServerTime, _initialSentSequence;
    private long _latestServerTime, _acknowledgment, _ackChangedAt, _sentSequence, _sentAtLastAckChange;
    internal NetworkDirectionWitness(long initialServerTime, long initialAcknowledgment, long initialSentSequence)
    {
        _initialServerTime = _latestServerTime = _ackChangedAt = initialServerTime;
        _acknowledgment = initialAcknowledgment;
        _initialSentSequence = _sentSequence = _sentAtLastAckChange = initialSentSequence;
    }
    internal long AuthorityAdvanceMilliseconds => _latestServerTime - _initialServerTime;
    internal long UnchangedAcknowledgmentMilliseconds => _latestServerTime - _ackChangedAt;
    internal long SentSequenceAdvance => _sentSequence - _initialSentSequence;
    internal bool IncomingWhileAcknowledgmentsStall => AuthorityAdvanceMilliseconds >= 1000
        && UnchangedAcknowledgmentMilliseconds >= 500 && _sentSequence > _acknowledgment
        && _sentSequence > _sentAtLastAckChange && SentSequenceAdvance > 0;
    internal bool Observe(long serverTime, long acknowledgment, long successfullySentSequence)
    {
        if (serverTime <= _latestServerTime) return false;
        _latestServerTime = serverTime;
        _sentSequence = Math.Max(_sentSequence, successfullySentSequence);
        if (acknowledgment != _acknowledgment)
        { _acknowledgment = acknowledgment; _ackChangedAt = serverTime; _sentAtLastAckChange = _sentSequence; }
        return true;
    }
}

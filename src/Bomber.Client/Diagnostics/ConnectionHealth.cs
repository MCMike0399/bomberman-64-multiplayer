using System.Diagnostics;

namespace Bomber.Client.Diagnostics;

/// <summary>Monotonic liveness and acknowledgment progress, independent of rendering.</summary>
internal sealed class ConnectionHealth
{
    private readonly object _gate = new();
    internal Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;
    private long _inbound, _state, _ackAt, _sent, _ack;
    private bool _playing;
    private bool _stateInGeneration, _ackInGeneration, _ackRequired;
    private long? _outageSince;
    private readonly Dictionary<long, long> _pings = [];
    private readonly Queue<double> _rtts = [];
    private long? _badSince, _goodSince;
    private string? _notice;
    private long _noticeSince;
    private long _inboundVersion;
    internal void Connected()
    {
        lock (_gate)
        {
            long now = Clock();
            if (_playing)
                _outageSince ??= _ackRequired ? Math.Min(_state, _ackAt) : _state;
            else
            { _state = _ackAt = now; _outageSince = null; _ackRequired = false; }
            _inbound = now; _inboundVersion++; _sent = _ack = 0;
            _stateInGeneration = _ackInGeneration = false;
            _pings.Clear(); _rtts.Clear(); _badSince = _goodSince = null;
        }
    }
    internal void Match(bool playing)
    {
        lock (_gate)
        {
            if (playing && !_playing)
            {
                _state = _ackAt = Clock(); _outageSince = null;
                _stateInGeneration = _ackInGeneration = false;
            }
            _playing = playing;
            if (!playing) { _notice = null; _badSince = _goodSince = _outageSince = null; }
        }
    }
    internal void Inbound() { lock (_gate) { _inbound = Clock(); _inboundVersion++; } }
    internal ReceiveDeadline? ReceiveStall()
    {
        lock (_gate)
        {
            long now = Clock();
            return _playing && Seconds(_inbound, now) >= 3 ? new(_inboundVersion, now) : null;
        }
    }
    internal bool TryAbortReceive(ReceiveDeadline observation, Action abort)
    {
        lock (_gate)
        {
            long now = Clock();
            if (!_playing || observation.Version != _inboundVersion || Seconds(observation.ObservedAt, now) < .2
                    || Seconds(_inbound, now) < 3) return false;
            // Commit against the same progress observation. An inbound fragment
            // registered before this boundary invalidates it, even if no full
            // authoritative frame has been assembled yet.
            _inboundVersion++; abort(); return true;
        }
    }
    internal readonly record struct ReceiveDeadline(long Version, long ObservedAt);
    internal void State(long? acknowledgment)
    {
        lock (_gate)
        {
            _state = Clock();
            _stateInGeneration = true;
            // A retained previous-generation ACK cannot prove that a new input
            // reached authority. Wait for an ACK within this sender's sequence.
            if (acknowledgment is { } value && value > _ack && value <= _sent)
            { _ack = value; _ackAt = _state; _ackInGeneration = true; }
        }
    }
    internal void InputSent(long sequence)
    { lock (_gate) { _sent = Math.Max(_sent, sequence); if (sequence > 0) _ackRequired = true; } }
    internal void Ping(long nonce)
    {
        lock (_gate)
        {
            while (_pings.Count >= 64) _pings.Remove(_pings.Keys.First());
            _pings[nonce] = Clock();
        }
    }
    internal double? Pong(long nonce)
    {
        lock (_gate)
        {
            if (!_pings.Remove(nonce, out long sent)) return null;
            double milliseconds = Seconds(sent, Clock()) * 1000;
            if (milliseconds is < 0 or > 60000) return null;
            _rtts.Enqueue(milliseconds); while (_rtts.Count > 8) _rtts.Dequeue();
            return milliseconds;
        }
    }
    private static double Seconds(long then, long now) => Math.Max(0, (double)(now - then) / Stopwatch.Frequency);
    internal Observation Read(string status)
    {
        lock (_gate)
        {
            long now = Clock();
            double inbound = Seconds(_inbound, now), state = Seconds(_state, now);
            double acknowledgment = _ackRequired && (!_ackInGeneration || _sent > _ack) ? Seconds(_ackAt, now) : 0;
            double stale = _playing ? Math.Max(inbound, Math.Max(state, acknowledgment)) : 0;
            if (_playing)
            {
                bool recovered = _stateInGeneration && (!_ackRequired || _ackInGeneration)
                    && inbound <= 1 && state <= 1 && acknowledgment <= 1;
                if (recovered) _outageSince = null;
                else if (stale > 1) _outageSince ??= now - (long)(stale * Stopwatch.Frequency);
            }
            double outage = _playing && _outageSince is { } since ? Seconds(since, now) : 0;
            stale = Math.Max(stale, outage);
            string reason = acknowledgment >= Math.Max(inbound, state) ? "input acknowledgments" : state > inbound ? "authoritative state" : "inbound frames";
            if (status.StartsWith("Reconnecting", StringComparison.Ordinal))
                return new(status, "reconnecting", stale, reason, inbound, state, acknowledgment) { OutageSeconds = outage };
            if (!_playing || status.StartsWith("Error:", StringComparison.Ordinal) || status.StartsWith("Ended", StringComparison.Ordinal))
                return new(null, "inactive", 0, reason, inbound, state, acknowledgment) { OutageSeconds = 0 };
            if (stale > 1)
            {
                if (_notice != "Connection interrupted") _noticeSince = now;
                _notice = "Connection interrupted"; _goodSince = null;
                return new(_notice, "interrupted", stale, reason, inbound, state, acknowledgment) { OutageSeconds = outage };
            }
            int highRtt = _rtts.Count(value => value > 250);
            bool badRtt = _rtts.Count >= 3 && (highRtt >= 3 || highRtt >= 2 && _rtts.Max() - _rtts.Min() > 250);
            if (badRtt)
            {
                _badSince ??= now; _goodSince = null;
                if (Seconds(_badSince.Value, now) >= 2 && (_notice != "Connection interrupted" || Seconds(_noticeSince, now) >= 1.5))
                    _notice = "Connection unstable";
            }
            else
            {
                _badSince = null; _goodSince ??= now;
                if (Seconds(_goodSince.Value, now) >= 1.5) _notice = null;
            }
            return new(_notice, _notice is null ? "healthy" : _notice == "Connection unstable" ? "unstable" : "interrupted",
                stale, reason, inbound, state, acknowledgment) { OutageSeconds = outage };
        }
    }
    internal sealed record Observation(string? Text, string Kind, double StaleSeconds, string Reason,
        double InboundSeconds, double StateSeconds, double AcknowledgmentSeconds)
    {
        internal double OutageSeconds { get; init; } = StaleSeconds;
    }
}

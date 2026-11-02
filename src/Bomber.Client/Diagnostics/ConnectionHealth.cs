using System.Diagnostics;

namespace Bomber.Client.Diagnostics;

/// <summary>Monotonic liveness and acknowledgment progress, independent of rendering.</summary>
internal sealed class ConnectionHealth
{
    private readonly object _gate = new();
    internal Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;
    private long _inbound, _state, _ackAt, _sent, _ack;
    private bool _playing;
    private readonly Dictionary<long, long> _pings = [];
    private readonly Queue<double> _rtts = [];
    private long? _badSince, _goodSince;
    private string? _notice;
    private long _noticeSince;
    internal void Connected()
    {
        lock (_gate)
        {
            _inbound = _state = _ackAt = Clock(); _sent = _ack = 0;
            _pings.Clear(); _rtts.Clear(); _badSince = _goodSince = null;
        }
    }
    internal void Match(bool playing)
    {
        lock (_gate)
        {
            if (playing && !_playing) _state = _ackAt = Clock();
            _playing = playing;
            if (!playing) { _notice = null; _badSince = _goodSince = null; }
        }
    }
    internal void Inbound() { lock (_gate) _inbound = Clock(); }
    internal void State(long? acknowledgment)
    {
        lock (_gate)
        {
            _state = Clock();
            if (acknowledgment is { } value && value > _ack) { _ack = value; _ackAt = _state; }
        }
    }
    internal void InputSent(long sequence) { lock (_gate) _sent = Math.Max(_sent, sequence); }
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
            double acknowledgment = _sent > _ack ? Seconds(_ackAt, now) : 0;
            double stale = _playing ? Math.Max(inbound, Math.Max(state, acknowledgment)) : 0;
            string reason = acknowledgment >= Math.Max(inbound, state) ? "input acknowledgments" : state > inbound ? "authoritative state" : "inbound frames";
            if (status.StartsWith("Reconnecting", StringComparison.Ordinal))
                return new(status, "reconnecting", stale, reason, inbound, state, acknowledgment);
            if (!_playing || status.StartsWith("Error:", StringComparison.Ordinal) || status.StartsWith("Ended", StringComparison.Ordinal))
                return new(null, "inactive", 0, reason, inbound, state, acknowledgment);
            if (stale > 1)
            {
                if (_notice != "Connection interrupted") _noticeSince = now;
                _notice = "Connection interrupted"; _goodSince = null;
                return new(_notice, "interrupted", stale, reason, inbound, state, acknowledgment);
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
                stale, reason, inbound, state, acknowledgment);
        }
    }
    internal sealed record Observation(string? Text, string Kind, double StaleSeconds, string Reason,
        double InboundSeconds, double StateSeconds, double AcknowledgmentSeconds);
}

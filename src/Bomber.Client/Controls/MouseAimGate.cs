namespace Bomber.Client.Controls;

/// <summary>Decides when the mouse/trackpad steers a carried bomb. A keyboard-only player whose
/// pointer merely rests over the window keeps ordinary facing throws: the mouse must have moved
/// during this carry or shortly before it began. Keyboard aiming afterwards takes control back
/// until the mouse moves again.</summary>
public sealed class MouseAimGate
{
    /// <summary>Seconds before a carry begins in which mouse movement still counts as pointing.</summary>
    public const double LeadSeconds = 1.5;
    /// <summary>Pixels of one continuous motion before it counts, so a brushed trackpad does not take over.</summary>
    public const float MotionThreshold = 6f;
    /// <summary>A pause this long ends a motion; stray sensor jitter never adds up across minutes.</summary>
    public const double MotionGapSeconds = .3;
    private double _moved = double.NegativeInfinity, _keyboardAimed = double.NegativeInfinity, _lastMotion = double.NegativeInfinity;
    private double? _carryStart;
    private float _pending;

    public void MouseMoved(float pixels, double now)
    {
        if (!float.IsFinite(pixels) || pixels <= 0) return;
        if (now - _lastMotion > MotionGapSeconds) _pending = 0;
        _lastMotion = now;
        _pending += pixels;
        if (_pending < MotionThreshold) return;
        _pending = 0;
        _moved = now;
    }

    public void KeyboardAimed(double now) { _keyboardAimed = now; _pending = 0; }

    public bool Active(bool carrying, double now)
    {
        if (!carrying) { _carryStart = null; return false; }
        _carryStart ??= now;
        return _moved > _keyboardAimed && _moved >= _carryStart.Value - LeadSeconds;
    }

    public void Reset()
    {
        _moved = _keyboardAimed = _lastMotion = double.NegativeInfinity;
        _carryStart = null; _pending = 0;
    }
}

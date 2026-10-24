using Godot;
namespace BlastCircuit.Arena;

/// <summary>Clocked interpolation absorbs uneven packet arrival without resetting motion every packet.</summary>
public sealed class SnapshotMotion
{
    private const double BufferSeconds=.1;
    private readonly List<(double Time,Vector3 Position)> _samples=[];
    private long _tick;
    private double _playback;
    private bool _starved;
    public SnapshotMotion(Vector3 initial,long tick)
    {_tick=tick;_playback=tick/30d-BufferSeconds;_samples.Add((tick/30d,initial));}
    public void Push(Vector3 position,long tick)
    {
        if(tick<=_tick||!position.IsFinite())return;
        bool discontinuity=_samples[^1].Position.DistanceSquaredTo(position)>9||tick-_tick>15;
        _tick=tick;double time=tick/30d;
        if(discontinuity){_samples.Clear();_playback=time-BufferSeconds;_starved=false;}
        _samples.Add((time,position));
        if(_starved&&time-_playback>=BufferSeconds)_starved=false;
        while(_samples.Count>16)_samples.RemoveAt(0);
    }
    public Vector3 Advance(float dt)
    {
        if(!float.IsFinite(dt)||dt<=0)return Sample();
        if(!_starved)_playback+=Math.Min(dt,.25)*(_samples[^1].Time-_playback>BufferSeconds+.06?1.15:1);
        if(_playback>_samples[^1].Time){_playback=_samples[^1].Time;_starved=true;}
        return Sample();
    }
    private Vector3 Sample()
    {
        while(_samples.Count>2&&_samples[1].Time<=_playback)_samples.RemoveAt(0);
        var from=_samples[0];
        if(_samples.Count==1||_playback<=from.Time)return from.Position;
        var to=_samples[1];
        return from.Position.Lerp(to.Position,(float)Math.Clamp((_playback-from.Time)/(to.Time-from.Time),0,1));
    }
}

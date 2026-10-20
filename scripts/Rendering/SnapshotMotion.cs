using Godot;
namespace BlastCircuit.Rendering;

/// <summary>One snapshot of interpolation delay avoids stop/catch-up pulses without inventing authority motion.</summary>
public sealed class SnapshotMotion(Vector3 initial,long tick)
{
    private Vector3 _from=initial,_to=initial;
    private long _tick=tick;
    private float _age,_duration=2f/30;
    public void Push(Vector3 position,long tick)
    {
        if(tick<=_tick)return;
        _duration=Mathf.Clamp((tick-_tick)/30f,1f/30,.2f);
        _from=_to;_to=position;_age=0;_tick=tick;
        if(_from.DistanceSquaredTo(_to)>9)_from=_to;
    }
    public Vector3 Advance(float dt)
    {
        _age+=dt;
        return _from.Lerp(_to,Mathf.Clamp(_age/_duration,0,1));
    }
}

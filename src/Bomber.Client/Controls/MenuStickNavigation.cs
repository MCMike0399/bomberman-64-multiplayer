namespace Bomber.Client.Controls;

/// <summary>Turns a held analog direction into deliberate menu steps, independently of report frequency.</summary>
public sealed class MenuStickNavigation
{
    public const float PressThreshold=.45f,ReleaseThreshold=.3f;
    public const double InitialRepeatDelay=.4,RepeatInterval=.12;
    private int _direction;
    private double _remaining;
    // 1 left, 2 right, 3 up, 4 down; zero means no step this frame.
    public int Step(float x,float y,double delta)
    {
        if(!float.IsFinite(x)||!float.IsFinite(y)){Reset();return 0;}
        float ax=Math.Abs(x),ay=Math.Abs(y);
        int direction=Math.Max(ax,ay)<ReleaseThreshold?0:
            Math.Max(ax,ay)<PressThreshold?_direction:
            ax>ay?(x<0?1:2):(y<0?3:4);
        // A near-diagonal hold keeps its chosen axis instead of jittering across two menu rows.
        if(_direction is 1 or 2 && direction is 3 or 4 && ay<ax*1.25f)
            direction=x<0?1:2;
        else if(_direction is 3 or 4 && direction is 1 or 2 && ax<ay*1.25f)
            direction=y<0?3:4;
        if(direction==0){Reset();return 0;}
        if(direction!=_direction){_direction=direction;_remaining=InitialRepeatDelay;return direction;}
        _remaining-=Math.Max(0,delta);
        if(_remaining>0)return 0;
        _remaining=RepeatInterval;return direction;
    }
    public void Reset(){_direction=0;_remaining=0;}
}

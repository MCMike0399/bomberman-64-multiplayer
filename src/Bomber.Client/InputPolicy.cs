using System.Numerics;
using Bomber.Game;
namespace Bomber.Client;

/// <summary>Arcade movement: analog direction, equal speed, radial drift rejection, no input summation.</summary>
public static class InputPolicy
{
    public const float StickDeadzone=.2f;
    public static Vector2 Direction(float x,float z,float deadzone=0)
    {
        if(!float.IsFinite(deadzone)||deadzone<0||deadzone>=1)throw new ArgumentOutOfRangeException(nameof(deadzone));
        if(!float.IsFinite(x)||!float.IsFinite(z))return Vector2.Zero;
        // Double precision prevents a finite but malformed large input overflowing the normalization.
        double length=Math.Sqrt((double)x*x+(double)z*z);
        if(length<=deadzone||length==0)return Vector2.Zero;
        return new((float)(x/length),(float)(z/length));
    }
    public static PlayerInput Resolve(PlayerInput keyboard,PlayerInput stick=default,PlayerInput dpad=default)
    {
        bool aim=keyboard.Aim||stick.Aim||dpad.Aim;
        var direction=Direction(dpad.X,dpad.Z);
        if(direction==Vector2.Zero)
        {
            direction=Direction(stick.X,stick.Z,StickDeadzone);
            if(aim&&direction!=Vector2.Zero)
            {
                double length=Math.Sqrt((double)stick.X*stick.X+(double)stick.Z*stick.Z);
                float strength=(float)Math.Clamp((length-StickDeadzone)/(1-StickDeadzone),0,1);
                direction*=strength;
            }
        }
        if(direction==Vector2.Zero)direction=Direction(keyboard.X,keyboard.Z);
        return new(direction.X,direction.Y,keyboard.A||stick.A||dpad.A,keyboard.B||stick.B||dpad.B,keyboard.R||stick.R||dpad.R,aim);
    }
}

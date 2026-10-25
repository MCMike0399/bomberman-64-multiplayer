using System.Numerics;

namespace Bomber.Game.Simulation;

/// <summary>Deterministic four-diagonal gusts and calm. No gameplay RNG is consumed by weather.</summary>
public static class BlizzardRules
{
    public const float GustSeconds=6;
    public const float WindSpeed=1.25f;
    public const float PlayerWindScale=.65f;
    public const float AvalancheStepSeconds=3;
    public static Vector2 Wind(float seconds,int seed,int round)
    {
        int cycle=(int)(Math.Max(0,seconds)/GustSeconds);
        uint hash=unchecked((uint)(seed*374761393+round*668265263+cycle*1274126177));
        hash=(hash^(hash>>13))*1274126177u;hash^=hash>>16;
        int direction=(int)(hash%5);
        if(direction==4)return Vector2.Zero;
        float age=seconds-cycle*GustSeconds;
        float envelope=Math.Clamp(age/.6f,0,1)*Math.Clamp((GustSeconds-age)/.6f,0,1);
        return new Vector2((direction&1)==0?-1:1,(direction&2)==0?-1:1)*(WindSpeed*.70710678f*envelope);
    }

    /// <summary>Wind adds displacement without altering stored kick/throw momentum or fuse clocks.</summary>
    public static ThrowFlight AdvanceFlight(Vector3 position,Vector3 velocity,float dt,Vector2 wind,
        Func<float,float,float> surface,Func<int,int,CellKind> kind,Func<int,int,bool> cover,
        bool bounce=false,float gravityScale=1,StageLayout? stage=null)
    {
        var drift=new Vector3(wind.X,0,wind.Y);
        var flight=BombThrowRules.AdvanceStatic(position,velocity+drift,dt,surface,kind,cover,bounce,gravityScale,stage);
        return new(flight.Position,new(flight.Velocity.X==0?0:flight.Velocity.X-drift.X,
            flight.Velocity.Y,flight.Velocity.Z==0?0:flight.Velocity.Z-drift.Z));
    }
}

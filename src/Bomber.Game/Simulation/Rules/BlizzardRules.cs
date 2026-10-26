// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using System.Numerics;
using Bomber.Game.Simulation.Stages;

namespace Bomber.Game.Simulation.Rules;

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
        // Reflect the stored momentum on exactly the axes reflected by collision.
        // Subtracting unreflected wind after a bounce would add twice the gust.
        static float Momentum(float original,float advected,float result) =>
            result==advected?original:result==-advected?-original:0;
        return new(flight.Position,new(Momentum(velocity.X,velocity.X+drift.X,flight.Velocity.X),
            flight.Velocity.Y,Momentum(velocity.Z,velocity.Z+drift.Z,flight.Velocity.Z)));
    }
}

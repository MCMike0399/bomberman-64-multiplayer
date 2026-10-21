using System.Numerics;

namespace Bomber.Game.Simulation;

/// <summary>Launch and flight math shared by authority and the carrier's advisory aim arc.</summary>
public static class BombThrowRules
{
    // Settled rendered hands are ~1.65 world units high (the actor is scaled 1.12).
    public const float LaunchHeight = 1.22f;
    public const float SimulationStep = 1f / 30;
    public static float Speed(float stickMagnitude,int pumps) =>
        BlastBomb.FlySpeed * (.75f + .25f * Math.Clamp(stickMagnitude,0,1)) + Math.Clamp(pumps,0,3) * .65f;
    public static Vector3 Velocity(float facingX,float facingZ,float stickMagnitude,int pumps)
    {
        var speed=Speed(stickMagnitude,pumps);
        return new(facingX*speed,speed*MathF.Tan(MathF.PI*55/180)*.5f,facingZ*speed);
    }
    public static ThrowFlight Advance(Vector3 position,Vector3 velocity,float dt)
    {
        position.X+=velocity.X*dt;position.Z+=velocity.Z*dt;
        velocity.Y-=BlastBomb.Gravity*dt;position.Y+=velocity.Y*dt;
        return new(position,velocity);
    }
    /// <summary>Static scenery only. Players, moving bombs and future changes may interrupt the arc.</summary>
    public static ThrowProjection Project(Vector3 origin,Vector3 velocity,Func<float,float,float> surface,
        Func<Vector3,bool> obstructed,float fuseSeconds=BlastBomb.FuseSeconds,Func<float,float,CellKind>? landingKind=null)
    {
        var points=new List<Vector3>{origin};
        var position=origin;
        for(int tick=0;tick<150;tick++)
        {
            fuseSeconds-=SimulationStep;
            if(fuseSeconds<=0)return new(points,ThrowProjectionEnd.Fuse);
            var flight=Advance(position,velocity,SimulationStep);
            position=flight.Position;velocity=flight.Velocity;
            if(obstructed(position))return new(points,ThrowProjectionEnd.Obstructed);
            var floor=surface(position.X,position.Z);
            if(position.Y<=floor)
            {
                position.Y=floor;points.Add(position);
                var end=landingKind?.Invoke(position.X,position.Z) switch{
                    CellKind.Water=>ThrowProjectionEnd.Water,CellKind.Void=>ThrowProjectionEnd.Void,_=>ThrowProjectionEnd.Landing};
                return new(points,end);
            }
            points.Add(position);
        }
        return new(points,ThrowProjectionEnd.Fuse);
    }
}
public readonly record struct ThrowFlight(Vector3 Position,Vector3 Velocity);
public enum ThrowProjectionEnd {Landing,Obstructed,Fuse,Water,Void}
public sealed record ThrowProjection(IReadOnlyList<Vector3> Points,ThrowProjectionEnd End)
{
    public Vector3 Last=>Points[^1];
}

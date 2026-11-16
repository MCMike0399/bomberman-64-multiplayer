// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using System.Numerics;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.State;

namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private void MoveGroundPlayer(BlastPlayer p,InputState input,float speed,float dt)
    {
        var velocity=new Vector2(input.X*speed,input.Z*speed);
        bool glass=!p.Ghost&&SlipperyGlassRules.OnGlass(_stage,p.X,p.Z,p.Y);
        if(glass)velocity=SlipperyGlassRules.Advance(new(p.SlideVX,p.SlideVZ),velocity,dt);
        float x=p.X,z=p.Z;
        ApplyMoveDelta(p,velocity.X*dt,velocity.Y*dt,dt);
        // Record rim travel too, so entering glass retains the actual approach.
        // A solid contact removes momentum on that axis instead of accumulating it.
        p.SlideVX=p.Falling||Math.Abs(p.X-x-velocity.X*dt)>.001f?0:velocity.X;
        p.SlideVZ=p.Falling||Math.Abs(p.Z-z-velocity.Y*dt)>.001f?0:velocity.Y;
    }

    private void BrakeGlassBomb(BlastBomb b,float dt)
    {
        if(_stage.Id!=Stages.BlastStages.LostAtSea||b.State!=BombState.Rolling)return;
        float speed=MathF.Sqrt(b.VX*b.VX+b.VZ*b.VZ);
        // A launch hop changes occupied height, not the traction of the floor.
        float support=_stage.SurfaceAt(b.X,b.Z,b.Tier);
        float drag=SlipperyGlassRules.OnGlass(_stage,b.X,b.Z,support)
            ?SlipperyGlassRules.BombGlassDeceleration:SlipperyGlassRules.BombRimDeceleration;
        float next=Math.Max(0,speed-drag*dt),scale=speed>.0001f?next/speed:0;
        b.VX*=scale;b.VZ*=scale;
        if(next<.05f){b.VX=b.VZ=0;b.State=BombState.Resting;}
    }
}

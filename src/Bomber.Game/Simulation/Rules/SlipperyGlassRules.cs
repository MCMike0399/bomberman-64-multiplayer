// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using System.Numerics;
using Bomber.Game.Simulation.Stages;

namespace Bomber.Game.Simulation.Rules;

/// <summary>Lost at Sea's ground inertia. Constants are original eight-player tuning,
/// not recovered Bomberman 64 values. The pale rim always has normal traction.</summary>
public static class SlipperyGlassRules
{
    public const float Acceleration=7.5f,CoastDeceleration=3.6f;
    public const float BombGlassDeceleration=1.8f,BombRimDeceleration=22f;
    public static bool OnGlass(StageLayout stage,float x,float z,float y=0)=>stage.Id==BlastStages.LostAtSea&&y<.15f&&
        x>=3&&z>=3&&x<stage.Width-3&&z<stage.Height-3;

    public static Vector2 Advance(Vector2 velocity,Vector2 desired,float dt)
    {
        var delta=desired-velocity;float distance=delta.Length();
        float change=(desired.LengthSquared()<.0001f?CoastDeceleration:Acceleration)*dt;
        return distance<=change?desired:velocity+delta*(change/distance);
    }
}

// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation.Rules;

/// <summary>Public deterministic shallow-water flow. Speeds/intervals are Blast Circuit tuning.</summary>
public static class GutterRules
{
    public const float CycleSeconds=8;
    public const float CurrentSpeed=1.5f;
    public const float PlayerScale=.75f;
    public static float Current(float seconds,int seed,int round)
    {
        int cycle=(int)(Math.Max(0,seconds)/CycleSeconds);
        uint hash=unchecked((uint)(seed*374761393+round*668265263+cycle*1274126177));
        hash=(hash^(hash>>13))*1274126177u;hash^=hash>>16;
        int state=(int)(hash%3);
        float age=seconds-cycle*CycleSeconds;
        float envelope=Math.Clamp(age/.5f,0,1)*Math.Clamp((CycleSeconds-age)/.5f,0,1);
        return state==2?0:(state==0?-1:1)*CurrentSpeed*envelope;
    }
}

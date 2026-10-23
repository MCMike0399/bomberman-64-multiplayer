namespace Bomber.Game.Simulation;

/// <summary>Pyramid-inspired sudden-death flooding. The one-minute warning follows the manual;
/// water rises immediately at the warning. Heights and rate remain project tuning.</summary>
public static class FloodRules
{
    public const float RiseDelay = 0;
    public const float InitialLevel = -.75f;
    public const float RisePerSecond = 1f/12f;
    public static float Level(float fightSeconds,int roundSeconds)
    {
        var elapsed=fightSeconds-Math.Max(0,roundSeconds-60);
        return elapsed<0 ? -1 : InitialLevel+Math.Max(0,elapsed-RiseDelay)*RisePerSecond;
    }
    public static bool Submerged(float surface,float water) => surface<=water;
}

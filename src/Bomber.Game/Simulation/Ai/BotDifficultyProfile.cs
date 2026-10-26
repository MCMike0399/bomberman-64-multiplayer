namespace Bomber.Game.Simulation.Ai;

/// <summary>Decision policy only. Every level uses the same authority physics and visible observations.</summary>
internal sealed record BotDifficultyProfile(float ReactionSeconds,int TacticalDepth,float PreferredEnemyDistance,
    float BombCooldown,float ItemUtilityScale,int EscapeDepth,float SafetyHorizonSeconds,float TrapPenalty,
    float PressureWeight,float GoalCommitmentBonus,float EngagementSeconds,float LeadSeconds)
{
    // LeadSeconds: how far ahead a rival's visible movement is extrapolated when
    // choosing where to stand; trailing a walking target never reaches blast range.
    public static readonly BotDifficultyProfile Easy = new(.24f,6,4f,1.2f,1.15f,8,3.1f,0, .65f,.4f,6,.15f);
    public static readonly BotDifficultyProfile Medium = new(.12f,12,1f,.75f,1f,10,3.25f,.8f, .8f,1.2f,4,.35f);
    public static readonly BotDifficultyProfile Hard = new(.08f,24,.8f,.55f,.95f,14,3.4f,1.8f, 1.2f,2f,2.5f,.5f);
    public static BotDifficultyProfile For(string? difficulty) => difficulty switch
    {
        "easy" => Easy,
        "hard" => Hard,
        // Preserve existing saved games and lobby clients using the original wire name.
        "normal" or "medium" or null => Medium,
        _ => Medium
    };
}

namespace Bomber.Game.Simulation;

/// <summary>Battle tuning. Capacity/range caps and non-stacking hearts follow the manual;
/// starting inventory, drop chance and relative item weights are project choices.</summary>
public static class PowerUpRules
{
    public const int StartingBombs = 3;
    public const int MaximumBombs = 8;
    public const int MaximumNormalFire = 3;
    public const int MaximumMegaFire = 6;
    public const double DropChance = .30;
    public static string ChooseDrop(Random random) => random.Next(100) switch
    {
        < 35 => "fire", < 65 => "bomb", < 77 => "remote", < 90 => "red", _ => "heart",
    };
    public static void Reset(BlastPlayer player)
    {
        player.FireLevel=1; player.BombCapacity=StartingBombs;
        player.HasRemote=player.HasRed=player.HasHeart=false; player.LastPickup=null;
    }
    public static void Apply(BlastPlayer player,string type)
    {
        switch(type)
        {
            case "fire": player.FireLevel=Math.Min(player.FireLevel+1,MaximumNormalFire);break;
            case "bomb": player.BombCapacity=Math.Min(player.BombCapacity+1,MaximumBombs);break;
            case "remote": player.HasRemote=true;break;
            case "red": player.HasRed=true;break;
            case "heart": player.HasHeart=true;break;
            default: return;
        }
        player.PickupSerial++; player.LastPickup=type;
    }
}

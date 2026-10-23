namespace Bomber.Game.Simulation;

/// <summary>Stacking upgrades and two independent, replaceable equipment slots.</summary>
public static class PowerUpRules
{
    public const int StartingBombs=3,MaximumBombs=8,MaximumNormalFire=3,MaximumMegaFire=6;
    public const float ItemLifetime=20,SwapPickupDelay=1.5f,NormalPumpSeconds=.45f,FocusPumpSeconds=.15f;
    public const double DropChance=.70;
    public static bool IsBombType(string type)=>type is "red" or "remote" or "bounce" or "fast";
    public static bool IsAbility(string type)=>type is "heart" or "focus";
    public static string ChooseDrop(Random random)=>random.Next(100) switch
    {<25=>"fire",<48=>"bomb",<60=>"remote",<72=>"red",<82=>"bounce",<90=>"fast",<96=>"heart",_=>"focus"};
    public static void Reset(BlastPlayer player)
    {player.FireLevel=1;player.BombCapacity=StartingBombs;player.BombType="normal";player.Ability="none";player.LastPickup=null;}
    public static bool CanCollect(BlastPlayer p,string type)=>type switch
    {"fire"=>p.FireLevel<MaximumNormalFire,"bomb"=>p.BombCapacity<MaximumBombs,_=>IsBombType(type)?p.BombType!=type:IsAbility(type)&&p.Ability!=type};
    public static string? Exchange(BlastPlayer p,string type)
    {
        string? previous=IsBombType(type)?p.BombType:IsAbility(type)?p.Ability:null;
        Apply(p,type);
        return previous is not (null or "normal" or "none")&&previous!=type?previous:null;
    }
    public static void Apply(BlastPlayer p,string type)
    {
        switch(type)
        {
            case "fire":p.FireLevel=Math.Min(p.FireLevel+1,MaximumNormalFire);break;
            case "bomb":p.BombCapacity=Math.Min(p.BombCapacity+1,MaximumBombs);break;
            case "red":case "remote":case "bounce":case "fast":p.BombType=type;break;
            case "heart":case "focus":p.Ability=type;break;
            default:return;
        }
        p.PickupSerial++;p.LastPickup=type;
    }
}

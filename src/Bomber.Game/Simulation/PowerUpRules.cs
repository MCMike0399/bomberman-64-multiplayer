namespace Bomber.Game.Simulation;

/// <summary>Stacking upgrades and two independent, replaceable equipment slots.</summary>
public static class PowerUpRules
{
    public const int StartingBombs=3,MaximumBombs=5,MaximumNormalFire=5,MaximumMegaFire=8;
    public const float ItemLifetime=20,SwapPickupDelay=1.5f,NormalPumpSeconds=.45f,FocusPumpSeconds=.15f;
    public const double DropChance=.55;
    public static bool IsBombType(string type)=>type is "red" or "remote" or "bounce";
    public static bool IsAbility(string type)=>type is "heart" or "focus";
    public static bool IsStatus(string type)=>type is "tiny" or "big" or "haste" or "slow";
    public static string ChooseDrop(Random random)=>ChooseDrop(random,new AdvancedRules())!;
    public static string? ChooseDrop(Random random,AdvancedRules? rules)
    {
        rules??=new();
        var pool=new (string Type,int Weight)[]{("fire",30),("bomb",30),("remote",7),("red",7),("bounce",6),("heart",3),("focus",3),("tiny",3),("big",3),("haste",6),("slow",2)}.Where(item=>rules.Allows(item.Type)).ToArray();
        int total=pool.Sum(item=>item.Weight);if(total==0)return null;
        int roll=random.Next(total);
        foreach(var item in pool){roll-=item.Weight;if(roll<0)return item.Type;}
        return null;
    }
    public static void Reset(BlastPlayer player,AdvancedRules? rules=null)
    {
        rules??=new();player.MaxFire=rules.MaxFire;player.MaxBombs=rules.MaxBombs;player.EffectDuration=rules.EffectSeconds;
        player.FireLevel=1;player.BombCapacity=Math.Min(StartingBombs,player.MaxBombs);player.BombType="normal";player.Ability="none";player.LastPickup=null;
        player.StatusEffect="none";player.StatusRemaining=0;
    }
    public static bool CanCollect(BlastPlayer p,string type)=>type switch
    {"fire"=>p.FireLevel<p.MaxFire,"bomb"=>p.BombCapacity<p.MaxBombs,_=>IsStatus(type)?p.StatusEffect!=type:IsBombType(type)?p.BombType!=type:IsAbility(type)&&p.Ability!=type};
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
            case "fire":p.FireLevel=Math.Min(p.FireLevel+1,p.MaxFire);break;
            case "bomb":p.BombCapacity=Math.Min(p.BombCapacity+1,p.MaxBombs);break;
            case "red":case "remote":case "bounce":p.BombType=type;break;
            case "heart":case "focus":p.Ability=type;break;
            case "tiny":case "big":case "haste":case "slow":p.StatusEffect=type;p.StatusRemaining=p.EffectDuration;break;
            default:return;
        }
        p.PickupSerial++;p.LastPickup=type;
    }
}

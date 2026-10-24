using System.Globalization;
namespace Bomber.Game;

/// <summary>Shared, validated match tuning for local games and authoritative rooms.</summary>
public sealed record AdvancedRules
{
    public int DropPercent { get; init; } = 55;
    public int BlockPercent { get; init; } = 100;
    public int ItemSeconds { get; init; } = 20;
    public int MaxFire { get; init; } = 5;
    public int MaxBombs { get; init; } = 5;
    public int EffectSeconds { get; init; } = 10;
    public string DisabledItems { get; init; } = "";
    public static readonly string[] ItemTypes=["fire","bomb","remote","red","bounce","fast","heart","focus","tiny","big","haste","slow"];
    public static readonly string[] SettingKeys=["drop-percent","block-percent","item-seconds","max-fire","max-bombs","effect-seconds","disabled-items"];
    public bool Allows(string type)=>!DisabledItems.Split(',',StringSplitOptions.RemoveEmptyEntries).Contains(type,StringComparer.Ordinal);
    public void Validate()
    {
        if(DropPercent is <0 or >100 || BlockPercent is <0 or >100 || ItemSeconds is <5 or >60 || MaxFire is <1 or >5 || MaxBombs is <1 or >8 || EffectSeconds is <5 or >30)
            throw new ArgumentOutOfRangeException(nameof(AdvancedRules));
        var disabled=DisabledItems.Split(',',StringSplitOptions.RemoveEmptyEntries);
        if(disabled.Any(type=>!ItemTypes.Contains(type)) || disabled.Distinct().Count()!=disabled.Length || DisabledItems.Length>100)
            throw new ArgumentException("Unknown or repeated disabled item.");
    }
    public void WriteTo(IDictionary<string,string> settings)
    {
        Validate();
        foreach(var (key,value) in new[]{("drop-percent",DropPercent),("block-percent",BlockPercent),("item-seconds",ItemSeconds),("max-fire",MaxFire),("max-bombs",MaxBombs),("effect-seconds",EffectSeconds)})
            settings[key]=value.ToString(CultureInfo.InvariantCulture);
        settings["disabled-items"]=DisabledItems;
    }
    public static AdvancedRules FromSettings(IReadOnlyDictionary<string,string> settings)
    {
        int Read(string key,int fallback)=>settings.TryGetValue(key,out var value)?int.Parse(value,CultureInfo.InvariantCulture):fallback;
        var rules=new AdvancedRules {DropPercent=Read("drop-percent",55),BlockPercent=Read("block-percent",100),ItemSeconds=Read("item-seconds",20),MaxFire=Read("max-fire",5),MaxBombs=Read("max-bombs",5),EffectSeconds=Read("effect-seconds",10),DisabledItems=settings.GetValueOrDefault("disabled-items","")};
        rules.Validate();return rules;
    }
}

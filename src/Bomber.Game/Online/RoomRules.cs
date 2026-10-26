using System.Text.Json;
using Bomber.Game.Matches;
namespace Bomber.Game.Online;

/// <summary>Validate a whole lobby-rules edit before replacing the next match's settings.</summary>
public static class RoomRules
{
    public static bool TryUpdate(IReadOnlyDictionary<string,string> current,JsonElement data,out IReadOnlyDictionary<string,string> settings)
    {
        settings=current;if(data.ValueKind!=JsonValueKind.Object)return false;
        var candidate=current.ToDictionary();
        foreach(var property in data.EnumerateObject())
        {
            if(property.Name=="party")continue;
            if(!AdvancedRules.SettingKeys.Contains(property.Name) && property.Name is not ("hosting" or "stage" or "bots" or "wins" or "round-seconds" or "ghosts" or "sudden-death" or "bot-difficulty")||property.Value.ValueKind!=JsonValueKind.String)return false;
            candidate[property.Name]=property.Value.GetString()!;
        }
        if(!int.TryParse(candidate.GetValueOrDefault("bots","3"),out int bots)||!int.TryParse(candidate.GetValueOrDefault("wins","2"),out int wins)||!int.TryParse(candidate.GetValueOrDefault("round-seconds","120"),out int seconds))return false;
        bool ReadBool(string key,out bool value){string text=candidate.GetValueOrDefault(key,"on");value=text is "on" or "true";return text is "on" or "off" or "true" or "false";}
        if(!ReadBool("ghosts",out bool ghosts)||!ReadBool("sudden-death",out bool sudden))return false;
        var options=new MatchOptions{HumanPlayers=bots==0?2:1,BotPlayers=bots,Wins=wins,RoundSeconds=seconds,Stage=candidate.GetValueOrDefault("stage","sunken-temple"),BotDifficulty=candidate.GetValueOrDefault("bot-difficulty","normal"),Ghosts=ghosts,SuddenDeath=sudden};
        try{options=options with {Advanced=AdvancedRules.FromSettings(candidate)};options.Validate();}catch(Exception ex) when(ex is ArgumentException or FormatException or OverflowException){return false;}
        string hosting=candidate.GetValueOrDefault("hosting","auto");
        if(hosting is not ("auto" or "server" or "peer"))return false;
        var accepted=options.ToSettings().ToDictionary();accepted["hosting"]=hosting;settings=accepted;return true;
    }
}

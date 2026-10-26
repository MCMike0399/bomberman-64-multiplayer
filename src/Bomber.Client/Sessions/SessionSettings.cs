using Bomber.Game.Matches;

namespace Bomber.Client.Sessions;

public sealed record SessionSettings(string Stage = "sunken-temple", int Bots = 3, int Wins = 2,
    int RoundSeconds = 120, bool SuddenDeath = true, bool Ghosts = true, string BotDifficulty = "normal")
{
    public AdvancedRules Advanced {get;init;}=new();
    public string Hosting { get; init; } = "auto";
    public static SessionSettings FromWire(IReadOnlyDictionary<string,string> settings)
    {
        int Number(string key,int fallback)=>settings.TryGetValue(key,out var value)&&int.TryParse(value,out int number)?number:fallback;
        bool Flag(string key)=>settings.GetValueOrDefault(key,"on") is "on" or "true";
        return new(settings.GetValueOrDefault("stage","sunken-temple"),Number("bots",3),Number("wins",2),Number("round-seconds",120),Flag("sudden-death"),Flag("ghosts"),settings.GetValueOrDefault("bot-difficulty","normal")){Advanced=AdvancedRules.FromSettings(settings), Hosting=settings.GetValueOrDefault("hosting","auto")};
    }
    public Dictionary<string, string> ToWireSettings()
    {
        if (BotDifficulty is not ("easy" or "medium" or "normal" or "hard")) throw new ArgumentException("Unknown CPU difficulty.");
        if (Hosting is not ("auto" or "server" or "peer")) throw new ArgumentException("Unknown hosting preference.");
        if (Stage is not ("sunken-temple" or "rock-garden" or "up-and-down" or "top-rules" or "blizzard-battle" or "greedy-trap")) throw new ArgumentException("Unknown stage.");
        if (Bots is < 0 or > 7 || Wins is < 1 or > 5 || RoundSeconds is < 60 or > 300)
            throw new ArgumentOutOfRangeException(nameof(Bots), "Bots 0–7, wins 1–5, round seconds 60–300.");
        var settings = new Dictionary<string,string>() { ["stage"] = Stage, ["bots"] = Bots.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["wins"] = Wins.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["round-seconds"] = RoundSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bot-difficulty"] = BotDifficulty, ["sudden-death"] = SuddenDeath ? "on" : "off", ["ghosts"] = Ghosts ? "on" : "off" };
        Advanced.WriteTo(settings);settings["hosting"] = Hosting;return settings;
    }
}

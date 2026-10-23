using System.Globalization;

namespace Bomber.Game;

public sealed record MatchOptions
{
    public int HumanPlayers { get; init; } = 1;
    public int BotPlayers { get; init; } = 3;
    public string Stage { get; init; } = "sunken-temple";
    public int Wins { get; init; } = 2;
    public int RoundSeconds { get; init; } = 120;
    public bool SuddenDeath { get; init; } = true;
    public bool Ghosts { get; init; } = true;
    public string BotDifficulty { get; init; } = "normal";
    public IReadOnlyList<int> PlayerAppearances { get; init; } = [];
    public bool Practice { get; init; }
    public int Seed { get; init; } = 64;

    public IReadOnlyDictionary<string, string> ToSettings() => new Dictionary<string, string>
    {
        ["stage"] = Stage,
        ["bot-difficulty"] = BotDifficulty,
        ["wins"] = Wins.ToString(CultureInfo.InvariantCulture),
        ["round-seconds"] = RoundSeconds.ToString(CultureInfo.InvariantCulture),
        ["sudden-death"] = SuddenDeath ? "on" : "off",
        ["ghosts"] = Ghosts ? "on" : "off",
        ["bots"] = BotPlayers.ToString(CultureInfo.InvariantCulture),
    };

    internal void Validate()
    {
        if (HumanPlayers is < 1 or > 8 || BotPlayers is < 0 or > 7 || HumanPlayers + BotPlayers > 8 || HumanPlayers + BotPlayers < (Practice ? 1 : 2))
            throw new ArgumentOutOfRangeException(nameof(HumanPlayers), "A match needs 2–8 total participants and at least one human.");
        if (PlayerAppearances.Count != 0 && (PlayerAppearances.Count != HumanPlayers + BotPlayers || PlayerAppearances.Any(index=>index is < 0 or > 7)))
            throw new ArgumentException("Appearances must contain one index (0–7) per active participant.",nameof(PlayerAppearances));
        if (Stage is not ("sunken-temple" or "rock-garden" or "up-and-down")) throw new ArgumentException("Unknown stage.", nameof(Stage));
        if (BotDifficulty is not ("easy" or "medium" or "normal" or "hard")) throw new ArgumentException("Unknown bot difficulty.",nameof(BotDifficulty));
        if (Wins is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(Wins));
        if (RoundSeconds is < 60 or > 300) throw new ArgumentOutOfRangeException(nameof(RoundSeconds));
    }
}

public readonly record struct PlayerInput(float X, float Z, bool A = false, bool B = false, bool R = false, bool Aim = false, bool Stop = false);

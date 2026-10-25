using System.Text.Json;
using Bomber.Game;

namespace Bomber.Client;

/// <summary>Call Advance, SendInput and DrainMessages from the engine thread. No callbacks touch engine objects.</summary>
public interface ISession : IAsyncDisposable
{
    int LocalPlayerCount { get; }
    string Status { get; }
    string? JoinCode { get; }
    bool IsEnded { get; }
    void Advance(double deltaSeconds);
    void SendInput(int localPlayer, PlayerInput input);
    void ReleaseInputs();
    IReadOnlyList<JsonElement> DrainMessages();
}

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
        if (Stage is not ("sunken-temple" or "rock-garden" or "up-and-down" or "top-rules" or "blizzard-battle")) throw new ArgumentException("Unknown stage.");
        if (Bots is < 0 or > 7 || Wins is < 1 or > 5 || RoundSeconds is < 60 or > 300)
            throw new ArgumentOutOfRangeException(nameof(Bots), "Bots 0–7, wins 1–5, round seconds 60–300.");
        var settings = new Dictionary<string,string>() { ["stage"] = Stage, ["bots"] = Bots.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["wins"] = Wins.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["round-seconds"] = RoundSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bot-difficulty"] = BotDifficulty, ["sudden-death"] = SuddenDeath ? "on" : "off", ["ghosts"] = Ghosts ? "on" : "off" };
        Advanced.WriteTo(settings);settings["hosting"] = Hosting;return settings;
    }
}

public enum NetworkMode { CreatePrivate, JoinCode, QuickPlay, CreatePeer }

/// <summary>Full state snapshots may supersede older snapshots, but stage/control frames stay ordered.</summary>
internal sealed class MessageBuffer
{
    private readonly object _gate = new();
    private readonly LinkedList<JsonElement> _messages = new();
    private const int Capacity = 128;
    private volatile bool _overflowed;
    public bool Overflowed => _overflowed;
    public void Add(JsonElement message)
    {
        lock (_gate)
        {
            string? stateKey = StateKey(message);
            if (stateKey is not null)
                for (var node = _messages.Last; node != null; node = node.Previous)
                    if (StateKey(node.Value) == stateKey) { _messages.Remove(node); break; }
            if (_messages.Count == Capacity)
            {
                var expendable = _messages.First;
                while (expendable is not null && StateKey(expendable.Value) is null) expendable = expendable.Next;
                if (expendable is not null) _messages.Remove(expendable);
                else { if (stateKey is null) _overflowed = true; return; }
            }
            _messages.AddLast(message.Clone());
        }
    }
    private static string? StateKey(JsonElement message)
    {
        string? type = message.GetProperty("t").GetString();
        if (type == "state") return type;
        if (type == "peer.state" && message.TryGetProperty("d", out var data) && data.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.String)
            return "peer:" + from.GetString();
        return null;
    }
    public IReadOnlyList<JsonElement> Drain()
    {
        lock (_gate) { var result = _messages.ToArray(); _messages.Clear(); return result; }
    }
    public void Clear() { lock (_gate) { _messages.Clear(); _overflowed = false; } }
}

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
    IReadOnlyList<JsonElement> DrainMessages();
}

public sealed record SessionSettings(string Stage = "sunken-temple", int Bots = 3, int Wins = 3,
    int RoundSeconds = 120, bool SuddenDeath = true, bool Ghosts = true, string BotDifficulty = "normal")
{
    public Dictionary<string, string> ToWireSettings()
    {
        if (BotDifficulty is not ("easy" or "medium" or "normal" or "hard")) throw new ArgumentException("Unknown CPU difficulty.");
        if (Stage is not ("sunken-temple" or "rock-garden" or "up-and-down")) throw new ArgumentException("Unknown stage.");
        if (Bots is < 0 or > 7 || Wins is < 1 or > 5 || RoundSeconds is < 60 or > 300)
            throw new ArgumentOutOfRangeException(nameof(Bots), "Bots 0–7, wins 1–5, round seconds 60–300.");
        return new() { ["stage"] = Stage, ["bots"] = Bots.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["wins"] = Wins.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["round-seconds"] = RoundSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bot-difficulty"] = BotDifficulty, ["sudden-death"] = SuddenDeath ? "on" : "off", ["ghosts"] = Ghosts ? "on" : "off" };
    }
}

public enum NetworkMode { CreatePrivate, JoinCode, QuickPlay }

/// <summary>Full state snapshots may supersede older snapshots, but stage/control frames stay ordered.</summary>
internal sealed class MessageBuffer
{
    private readonly object _gate = new();
    private readonly LinkedList<JsonElement> _messages = new();
    private const int Capacity = 128;
    public void Add(JsonElement message)
    {
        lock (_gate)
        {
            if (message.GetProperty("t").GetString() == "state")
                for (var node = _messages.Last; node != null; node = node.Previous)
                    if (node.Value.GetProperty("t").GetString() == "state") { _messages.Remove(node); break; }
            if (_messages.Count == Capacity) _messages.RemoveFirst();
            _messages.AddLast(message.Clone());
        }
    }
    public IReadOnlyList<JsonElement> Drain()
    {
        lock (_gate) { var result = _messages.ToArray(); _messages.Clear(); return result; }
    }
}

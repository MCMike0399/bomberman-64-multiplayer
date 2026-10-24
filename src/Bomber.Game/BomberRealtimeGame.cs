using Burbuja.Realtime.Abstractions;
using Bomber.Game.Simulation;

namespace Bomber.Game;

/// <summary>The product's game registration; the kernel supplies lobbies and transport.</summary>
public sealed class BomberRealtimeGame : IRealtimeGame
{
    public string Id => "blast";
    public string DisplayName => "Burbuja Blast";
    public GameRules Rules { get; } = new()
    {
        MinPlayers = 2, MaxPlayers = 8, TickRate = LocalMatch.TickRate,
        StartCountdown = TimeSpan.FromSeconds(3), MaxMatchDuration = TimeSpan.FromHours(6),
        AllowJoinInProgress = true, ReconnectGrace = TimeSpan.FromSeconds(15),
        ResultsLinger = TimeSpan.FromSeconds(8), MaxInboundMessagesPerSecond = 90, MaxMessageBytes = 1024,
    };
    public IGameSession CreateSession(GameSessionOptions options) => options.Settings.GetValueOrDefault("party") == "on" ? new PartySession(options) : new BlastSession(options);
}

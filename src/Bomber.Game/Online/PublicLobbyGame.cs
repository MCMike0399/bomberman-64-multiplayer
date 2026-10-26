using Burbuja.Realtime.Abstractions;
using Bomber.Game.Matches;
using Bomber.Game.Online.Rooms;

namespace Bomber.Game.Online;

/// <summary>FIFO admission creates a waiting room immediately; only its leader starts gameplay.</summary>
public sealed class PublicLobbyGame : IRealtimeGame
{
    public string Id => "blast-lobby";
    public string DisplayName => "Blast random lobby";
    public bool ReliableControlFrames => true;
    public IReadOnlyList<string> ReplaceableMessageTypes { get; } = ["state"];
    public GameRules Rules { get; } = new BomberRealtimeGame().Rules;
    public IMatchmakingStrategy Matchmaking { get; } = new WaitingRoomMatchmaker();
    public IGameSession CreateSession(GameSessionOptions options) => new PartySession(options with
    { Settings = options.Settings.Count == 0 ? new MatchOptions { HumanPlayers = 2, BotPlayers = 0 }.ToSettings() : options.Settings }, minimumHumans: 2);
}

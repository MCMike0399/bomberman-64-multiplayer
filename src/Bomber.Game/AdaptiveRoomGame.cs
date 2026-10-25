using Burbuja.Realtime.Abstractions;

namespace Bomber.Game;

/// <summary>A persistent private room; authority is selected for each match.</summary>
public sealed class AdaptiveRoomGame : IRealtimeGame
{
    public string Id => "blast-room";
    public string DisplayName => "Blast private room";
    public GameRules Rules { get; } = new PeerRoomGame().Rules;
    public bool ReliableControlFrames => true;
    public bool UsesResourceReservations => true;
    public IReadOnlyList<string> ReplaceableMessageTypes { get; } = ["state", "peer.state"];
    public IGameSession CreateSession(GameSessionOptions options) => new AdaptiveRoomSession(options);
}

public sealed class PublicAdaptiveRoomGame : IRealtimeGame
{
    public string Id => "blast-online";
    public string DisplayName => "Blast online battle";
    public GameRules Rules { get; } = new PeerRoomGame().Rules;
    public bool ReliableControlFrames => true;
    public bool UsesResourceReservations => true;
    public IReadOnlyList<string> ReplaceableMessageTypes { get; } = ["state", "peer.state"];
    public IMatchmakingStrategy Matchmaking { get; } = new WaitingRoomMatchmaker();
    public IGameSession CreateSession(GameSessionOptions options) => new AdaptiveRoomSession(options with
    { Settings = options.Settings.Count == 0 ? new MatchOptions { HumanPlayers = 2, BotPlayers = 0 }.ToSettings() : options.Settings }, publicRoom: true);
}

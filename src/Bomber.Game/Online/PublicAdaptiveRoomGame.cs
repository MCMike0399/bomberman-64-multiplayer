using Burbuja.Realtime.Abstractions;
using Bomber.Game.Matches;
using Bomber.Game.Online.Rooms;

namespace Bomber.Game.Online;

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
    { Settings = new MatchOptions { HumanPlayers = 2, BotPlayers = 0 }.ToSettings() }, publicRoom: true);
}

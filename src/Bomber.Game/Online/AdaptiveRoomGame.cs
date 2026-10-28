using Burbuja.Realtime.Abstractions;
using Bomber.Game.Online.Rooms;

namespace Bomber.Game.Online;

/// <summary>A persistent private room; authority is selected for each match.</summary>
public sealed class AdaptiveRoomGame(PeerHostLease? lease = null, PeerRoomTrace? trace = null) : IRealtimeGame
{
    public string Id => "blast-room";
    public string DisplayName => "Blast private room";
    public GameRules Rules { get; } = new PeerRoomGame().Rules;
    public bool ReliableControlFrames => true;
    public bool UsesResourceReservations => true;
    public IReadOnlyList<string> ReplaceableMessageTypes { get; } = ["state", "peer.state"];
    public IGameSession CreateSession(GameSessionOptions options) => new AdaptiveRoomSession(options, lease: lease, trace: trace);
}

using Burbuja.Realtime.Abstractions;
using Bomber.Game.Online.Rooms;

namespace Bomber.Game.Online;

public sealed class PeerRoomGame(PeerHostLease? lease = null, PeerRoomTrace? trace = null) : IRealtimeGame
{
    public string Id => "blast-peer";
    public string DisplayName => "Blast peer room";
    public bool ReliableControlFrames => true;
    public IReadOnlyList<string> ReplaceableMessageTypes { get; } = ["peer.state"];
    public GameRules Rules { get; } = new BomberRealtimeGame().Rules with
    { MaxMessageBytes = 131072, MaxInboundMessagesPerSecond = 240 };
    public IGameSession CreateSession(GameSessionOptions options) => new PeerRoomSession(options, lease: lease, trace: trace);
}

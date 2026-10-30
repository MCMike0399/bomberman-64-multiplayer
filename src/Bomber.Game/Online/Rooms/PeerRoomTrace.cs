namespace Bomber.Game.Online.Rooms;

/// <summary>Writes peer-room authority transitions: membership connection changes,
/// elections, lease expiry, handshake kicks and byte-budget drops.</summary>
/// <remarks>Transition-only and rate-limited, so it stays small in production logs.</remarks>
public sealed record PeerRoomTrace(Action<string> Write)
{
    public static readonly PeerRoomTrace None = new(_ => { });
}

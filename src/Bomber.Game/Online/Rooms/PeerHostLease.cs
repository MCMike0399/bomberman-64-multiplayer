namespace Bomber.Game.Online.Rooms;

/// <summary>How long a ready peer host may stay silent before the room elects another host.</summary>
/// <remarks>Production uses six seconds. Configuration may only lengthen it, for owned QA
/// servers whose clients render in software and draw about one frame per second.</remarks>
public sealed record PeerHostLease(TimeSpan Silence)
{
    public static readonly PeerHostLease Production = new(TimeSpan.FromSeconds(6));

    public static PeerHostLease From(double? seconds) => seconds switch
    {
        null => Production,
        >= 6 and <= 600 => new(TimeSpan.FromSeconds(seconds.Value)),
        _ => throw new ArgumentOutOfRangeException(nameof(seconds), "Peer host lease must be 6 to 600 seconds."),
    };
}

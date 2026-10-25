namespace Bomber.Game;

/// <summary>Shared peer simulation contract: revision 2 adds layered stages and authored stage hazards.</summary>
public static class PeerProtocol
{
    public const int Revision = 2;
    public const string UpdateMessage = "Peer versions differ. Update Blast Circuit to v0.28 or later and rejoin.";
}

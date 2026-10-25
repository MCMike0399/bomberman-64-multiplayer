namespace Bomber.Game;

/// <summary>Shared online simulation contract: revision 2 adds layered stages and authored stage hazards.</summary>
public static class OnlineProtocol
{
    public const int Revision = 2;
    public const string HeaderName = "X-Blast-Protocol";
    public const string WireRevision = "2";
    public static bool Accepts(IEnumerable<string>? values)=>values is not null && values.SequenceEqual([WireRevision]);
    public const string UpdateMessage = "Update Blast Circuit to v0.28 or later before playing online.";
}

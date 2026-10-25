namespace Bomber.Game;

/// <summary>Shared online contract: revision 4 removes Fast bomb settings and motion semantics.</summary>
public static class OnlineProtocol
{
    public const int Revision = 4;
    public const string HeaderName = "X-Blast-Protocol";
    public const string WireRevision = "4";
    public static bool Accepts(IEnumerable<string>? values)=>values is not null && values.SequenceEqual([WireRevision]);
    public const string UpdateMessage = "Update Blast Circuit to v0.30 or later before playing online.";
}

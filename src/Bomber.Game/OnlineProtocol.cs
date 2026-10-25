namespace Bomber.Game;

/// <summary>Shared online contract: revision 3 separates room leadership, authority and snapshot channels.</summary>
public static class OnlineProtocol
{
    public const int Revision = 3;
    public const string HeaderName = "X-Blast-Protocol";
    public const string WireRevision = "3";
    public static bool Accepts(IEnumerable<string>? values)=>values is not null && values.SequenceEqual([WireRevision]);
    public const string UpdateMessage = "Update Blast Circuit to v0.29 or later before playing online.";
}

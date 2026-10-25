namespace Bomber.Game;

/// <summary>Shared online contract: revision 5 adds Blizzard Battle and authoritative weather.</summary>
public static class OnlineProtocol
{
    public const int Revision = 5;
    public const string HeaderName = "X-Blast-Protocol";
    public const string WireRevision = "5";
    public static bool Accepts(IEnumerable<string>? values)=>values is not null && values.SequenceEqual([WireRevision]);
    public const string UpdateMessage = "Update Blast Circuit to a version with Blizzard Battle before playing online.";
}

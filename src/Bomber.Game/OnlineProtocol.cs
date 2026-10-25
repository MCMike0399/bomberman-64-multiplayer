namespace Bomber.Game;

/// <summary>Revision 6 adds Greedy Trap and rejects the incompatible Blizzard-only revision 5.</summary>
public static class OnlineProtocol
{
    public const int Revision = 6;
    public const string HeaderName = "X-Blast-Protocol";
    public const string WireRevision = "6";
    public static bool Accepts(IEnumerable<string>? values)=>values is not null && values.SequenceEqual([WireRevision]);
    public const string UpdateMessage = "Update Blast Circuit to a version supporting Greedy Trap before playing online.";
}

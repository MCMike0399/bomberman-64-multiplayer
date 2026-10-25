namespace Bomber.Game;

/// <summary>Revision 5 adds Greedy Trap after revision 4 removed Fast bomb semantics.</summary>
public static class OnlineProtocol
{
    public const int Revision = 5;
    public const string HeaderName = "X-Blast-Protocol";
    public const string WireRevision = "5";
    public static bool Accepts(IEnumerable<string>? values)=>values is not null && values.SequenceEqual([WireRevision]);
    public const string UpdateMessage = "Update Blast Circuit to v0.30 or later before playing online.";
}

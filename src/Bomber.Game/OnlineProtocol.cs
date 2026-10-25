namespace Bomber.Game;

/// <summary>Every release has an exact admission token; schema 6 includes both new arenas and authoritative Blizzard weather.</summary>
public static class OnlineProtocol
{
    private static string Metadata(string key) => typeof(OnlineProtocol).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(attribute => attribute.Key == key).Value!;
    public static readonly string ReleaseVersion = Metadata("BlastVersion");
    public static readonly string ReleaseBuild = Metadata("BlastBuild");
    public static readonly int Revision = int.Parse(Metadata("BlastProtocolRevision"), System.Globalization.CultureInfo.InvariantCulture);
    public static readonly string SourceRevision = ((System.Reflection.AssemblyInformationalVersionAttribute)
        typeof(OnlineProtocol).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).Single())
        .InformationalVersion.Split('+').ElementAtOrDefault(1) ?? "unknown";
    public const string HeaderName = "X-Blast-Protocol";
    public static readonly string WireRevision = $"{Revision}/{ReleaseVersion}/{ReleaseBuild}";
    public static bool Accepts(IEnumerable<string>? values)=>values is not null && values.SequenceEqual([WireRevision]);
    public static readonly string UpdateMessage = $"This server requires Blast Circuit v{ReleaseVersion} (build {ReleaseBuild}). Open Settings to check for updates.";
    public static readonly string ServerUpdateMessage = $"This client requires a server for Blast Circuit v{ReleaseVersion} (build {ReleaseBuild}). The server may be updating; try again shortly or contact its operator.";
}

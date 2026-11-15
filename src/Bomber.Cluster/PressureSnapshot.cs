using System.Text.Json;
using Burbuja.Cluster.Abstractions;

namespace Bomber.Cluster;

/// <summary>
/// An optional advisory filter, independent of operator drain state. On missing/stale telemetry
/// the measured, resource-capped baseline policy remains available; it never needs the monitor to boot.
///
/// <para>Two sources, never mixed. When the kernel's cluster module is enabled on this host, its
/// own verdict is used: this node's pressure after hysteresis, and the nodes its controller reports
/// as pressed. Both are cached in process, so no file is written, copied or parsed. Otherwise the
/// advisory file from the external monitor is read as before. Turning the module off is the rollback.</para>
///
/// <para>The cluster's node id and the realtime server id are the same value in every deployment
/// of this game (both come from <c>BLAST_NODE</c>), which is why a pressed node id excludes a server.</para>
/// </summary>
public sealed class PressureSnapshot(string? path, Func<DateTimeOffset>? clock = null, INodePressure? cluster = null, string? nodeId = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    /// <summary>Where the advice comes from, for the startup log and tests.</summary>
    public string Source => cluster is not null ? "kernel-cluster" : string.IsNullOrWhiteSpace(path) ? "none" : "file";

    public IReadOnlySet<string> ExcludedServers()
    {
        if (cluster is not null)
        {
            // Unknown and stale both read as "nothing excluded": the cluster reports an unknown verdict as
            // unknown and empties stale advice, and a game is never made unavailable by missing telemetry.
            var local = cluster.Local;
            var pressed = cluster.PressuredNodes;
            if (!(local.Known && local.Pressured) || string.IsNullOrEmpty(nodeId) || pressed.Contains(nodeId)) return pressed;
            return new HashSet<string>(pressed, StringComparer.Ordinal) { nodeId };
        }

        if (string.IsNullOrWhiteSpace(path)) return new HashSet<string>();
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 16384) return new HashSet<string>();
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            var observed = root.GetProperty("at").GetDouble();
            var now = _clock().ToUnixTimeMilliseconds() / 1000.0;
            if (!double.IsFinite(observed) || now-observed is < -5 or > 90) return new HashSet<string>();
            return root.GetProperty("excludedServers").EnumerateArray()
                .Select(e => e.GetString() ?? throw new JsonException()).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
                                     KeyNotFoundException or InvalidOperationException or FormatException)
        { return new HashSet<string>(); }
    }
}

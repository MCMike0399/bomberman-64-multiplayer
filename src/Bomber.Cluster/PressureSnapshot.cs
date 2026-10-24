using System.Text.Json;

namespace Bomber.Cluster;

/// <summary>
/// An optional advisory filter, independent of operator drain state. On missing/stale telemetry
/// the measured, resource-capped baseline policy remains available; it never needs the monitor to boot.
/// </summary>
public sealed class PressureSnapshot(string? path, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    public IReadOnlySet<string> ExcludedServers()
    {
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

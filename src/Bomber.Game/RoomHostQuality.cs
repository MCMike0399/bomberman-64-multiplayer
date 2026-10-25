using System.Text.Json;

namespace Bomber.Game;

/// <summary>Short-lived, bounded observations used to compare eligible hosts, never to grant authority.</summary>
internal sealed class RoomHostQuality
{
    private sealed record Link(double Rtt, double Loss, bool Direct, int Samples);
    private sealed record Report(DateTimeOffset At, double ServerRtt, double FrameGap, Dictionary<string, Link> Links);
    internal sealed record Candidate(string PlayerId, double WorstRtt, double MeanRtt, bool AllDirect);
    private readonly Dictionary<string, Report> _reports = new(StringComparer.Ordinal);

    public bool Record(string player, JsonElement data, IReadOnlySet<string> members, DateTimeOffset now)
    {
        try
        {
            if (!members.Contains(player) || !Number(data, "serverRtt", 5000, out var server) ||
                !Number(data, "frameGap", 5000, out var gap) || !data.TryGetProperty("links", out var values) ||
                values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 7) return false;
            var links = new Dictionary<string, Link>(StringComparer.Ordinal);
            foreach (var value in values.EnumerateArray())
            {
                string? id = value.GetProperty("id").GetString();
                if (id is null || id == player || !members.Contains(id) || !Number(value, "rtt", 5000, out var rtt) ||
                    !Number(value, "loss", 1, out var loss) || !value.GetProperty("samples").TryGetInt32(out int samples) ||
                    samples is < 0 or > 1000 || !links.TryAdd(id, new(rtt, loss, value.GetProperty("direct").GetBoolean(), samples))) return false;
            }
            _reports[player] = new(now, server, gap, links);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return false; }
    }

    public Candidate? Best(IReadOnlyList<string> members, Func<string, bool> eligible, DateTimeOffset now)
    {
        if (members.Count < 2) return null;
        var candidates = new List<Candidate>();
        foreach (string host in members.Where(eligible))
        {
            if (!Fresh(host, now, out var report) || report!.FrameGap > 100) continue;
            var rtts = new List<double>(); bool direct = true;
            foreach (string guest in members.Where(id => id != host))
            {
                if (!Fresh(guest, now, out var peer) || !report.Links.TryGetValue(guest, out var a) ||
                    !peer!.Links.TryGetValue(host, out var b) || Math.Min(a.Samples, b.Samples) < 3 || Math.Max(a.Loss, b.Loss) > .1) break;
                rtts.Add(Math.Max(a.Rtt, b.Rtt)); direct &= a.Direct && b.Direct;
            }
            if (rtts.Count == members.Count - 1 && rtts.Max() <= 200)
                candidates.Add(new(host, rtts.Max(), rtts.Average(), direct));
        }
        return candidates.OrderBy(c => c.WorstRtt).ThenBy(c => c.MeanRtt).FirstOrDefault();
    }

    public double ServerWorst(IReadOnlyList<string> members, DateTimeOffset now) =>
        members.All(id => Fresh(id, now, out _)) ? members.Max(id => _reports[id].ServerRtt) : double.PositiveInfinity;
    public void Remove(string id) => _reports.Remove(id);
    private bool Fresh(string id, DateTimeOffset now, out Report? report) =>
        _reports.TryGetValue(id, out report) && now >= report.At && now - report.At <= TimeSpan.FromSeconds(6);
    private static bool Number(JsonElement value, string key, double maximum, out double number)
    { number = 0; return value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out number) && double.IsFinite(number) && number >= 0 && number <= maximum; }
}

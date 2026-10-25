using System.Text.Json;
namespace Bomber.Client;

public sealed record PartyStatus(string? LeaderId, string Phase, int Match, bool CanStart, int Bots, IReadOnlyDictionary<string,string>? Settings = null,
    string Hosting = "cluster", int MinimumHumans = 1, int Epoch = 0, string Notice = "")
{
    public string? HostId { get; init; }
    public string? AuthorityId => HostId ?? LeaderId;
    public bool Adaptive { get; init; }
    public bool PublicRoom { get; init; }
    public string HostingPolicy { get; init; } = "auto";
    public long? StartAt { get; init; }
    public static PartyStatus Parse(JsonElement data) => new(
        data.GetProperty("leaderId").GetString(), data.GetProperty("phase").GetString()!,
        data.GetProperty("match").GetInt32(), data.GetProperty("canStart").GetBoolean(), data.GetProperty("bots").GetInt32(), data.TryGetProperty("settings",out var settings)?settings.Deserialize<Dictionary<string,string>>():null,
        data.TryGetProperty("hosting", out var hosting) ? hosting.GetString() ?? "cluster" : "cluster",
        data.TryGetProperty("minimumHumans", out var minimum) ? minimum.GetInt32() : 1,
        data.TryGetProperty("epoch", out var epoch) ? epoch.GetInt32() : 0,
        data.TryGetProperty("notice", out var notice) ? notice.GetString() ?? "" : "")
    {
        HostId = data.TryGetProperty("hostId", out var host) && host.ValueKind == JsonValueKind.String ? host.GetString() : null,
        Adaptive = data.TryGetProperty("adaptive", out var adaptive) && adaptive.ValueKind == JsonValueKind.True,
        PublicRoom = data.TryGetProperty("publicRoom", out var pub) && pub.ValueKind == JsonValueKind.True,
        HostingPolicy = data.TryGetProperty("hostingPolicy", out var policy) && policy.ValueKind == JsonValueKind.String ? policy.GetString()! : "auto",
        StartAt = data.TryGetProperty("startAt", out var start) && start.ValueKind == JsonValueKind.Number && start.TryGetInt64(out long at) ? at : null,
    };
}

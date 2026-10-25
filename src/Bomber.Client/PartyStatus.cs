using System.Text.Json;
namespace Bomber.Client;

public sealed record PartyStatus(string? LeaderId, string Phase, int Match, bool CanStart, int Bots, IReadOnlyDictionary<string,string>? Settings = null,
    string Hosting = "cluster", int MinimumHumans = 1, int Epoch = 0, string Notice = "")
{
    public static PartyStatus Parse(JsonElement data) => new(
        data.GetProperty("leaderId").GetString(), data.GetProperty("phase").GetString()!,
        data.GetProperty("match").GetInt32(), data.GetProperty("canStart").GetBoolean(), data.GetProperty("bots").GetInt32(), data.TryGetProperty("settings",out var settings)?settings.Deserialize<Dictionary<string,string>>():null,
        data.TryGetProperty("hosting", out var hosting) ? hosting.GetString() ?? "cluster" : "cluster",
        data.TryGetProperty("minimumHumans", out var minimum) ? minimum.GetInt32() : 1,
        data.TryGetProperty("epoch", out var epoch) ? epoch.GetInt32() : 0,
        data.TryGetProperty("notice", out var notice) ? notice.GetString() ?? "" : "");
}

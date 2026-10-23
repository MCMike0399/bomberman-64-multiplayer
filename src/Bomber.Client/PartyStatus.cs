using System.Text.Json;
namespace Bomber.Client;

public sealed record PartyStatus(string? LeaderId, string Phase, int Match, bool CanStart, int Bots, IReadOnlyDictionary<string,string>? Settings = null)
{
    public static PartyStatus Parse(JsonElement data) => new(
        data.GetProperty("leaderId").GetString(), data.GetProperty("phase").GetString()!,
        data.GetProperty("match").GetInt32(), data.GetProperty("canStart").GetBoolean(), data.GetProperty("bots").GetInt32(), data.TryGetProperty("settings",out var settings)?settings.Deserialize<Dictionary<string,string>>():null);
}

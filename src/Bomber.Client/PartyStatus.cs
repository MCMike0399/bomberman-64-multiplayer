using System.Text.Json;
namespace Bomber.Client;

public sealed record PartyStatus(string? LeaderId, string Phase, int Match, bool CanStart, int Bots)
{
    public static PartyStatus Parse(JsonElement data) => new(
        data.GetProperty("leaderId").GetString(), data.GetProperty("phase").GetString()!,
        data.GetProperty("match").GetInt32(), data.GetProperty("canStart").GetBoolean(), data.GetProperty("bots").GetInt32());
}

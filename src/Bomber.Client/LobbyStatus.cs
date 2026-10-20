using System.Globalization;
using System.Text.Json;
namespace Bomber.Client;

public sealed record LobbyParticipant(string Id, string Name, int Slot, bool Connected);

/// <summary>The HTTP lobby uses members and ISO times; kernel WebSocket lobby frames use players and Unix milliseconds.</summary>
public sealed record LobbyStatus(string State, string? JoinCode, DateTimeOffset? CountdownEndsAt, IReadOnlyList<LobbyParticipant> Players)
{
    public static LobbyStatus Parse(JsonElement data)
    {
        var players = new List<LobbyParticipant>();
        if (data.TryGetProperty("players", out var roster) || data.TryGetProperty("members", out roster))
            foreach (var p in roster.EnumerateArray())
                players.Add(new LobbyParticipant(p.GetProperty("playerId").GetString()!, p.GetProperty("displayName").GetString()!, p.GetProperty("slot").GetInt32(), p.GetProperty("connected").GetBoolean()));
        DateTimeOffset? countdown = null;
        if (data.TryGetProperty("countdownEndsAt", out var time))
        {
            if (time.ValueKind == JsonValueKind.Number) countdown = DateTimeOffset.FromUnixTimeMilliseconds(time.GetInt64());
            else if (time.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) countdown = parsed;
        }
        return new(data.TryGetProperty("state", out var state) ? state.GetString() ?? "open" : "open",
            data.TryGetProperty("joinCode", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null,
            countdown, players.OrderBy(p => p.Slot).ToArray());
    }
}

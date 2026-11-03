using System.Collections.Concurrent;

namespace Bomber.Game.Diagnostics;

/// <summary>Bounded observational events; consumers cannot change authority state.</summary>
public static class InputSilenceTelemetry
{
    private static readonly ConcurrentQueue<Sample> Pending = new();
    internal static void Record(string lobbyId, string playerId, string kind, double ageMilliseconds)
    {
        Pending.Enqueue(new(lobbyId, playerId, kind, Math.Round(ageMilliseconds, 1)));
        while (Pending.Count > 256) Pending.TryDequeue(out _);
    }
    public static IReadOnlyList<Sample> Drain()
    {
        var result = new List<Sample>();
        while (result.Count < 256 && Pending.TryDequeue(out var sample)) result.Add(sample);
        return result;
    }
    public sealed record Sample(string LobbyId, string PlayerId, string Kind, double AgeMilliseconds);
}

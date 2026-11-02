using System.Text.Json;
using Bomber.Client.Diagnostics;

namespace Bomber.Client.Sessions;

public sealed partial class NetworkSession
{
    private readonly ConnectionHealth _health = new();
    private ConnectionJournal? _journal;
    private Task? _terminalMonitor;
    private bool _fastReceiveRecovery = true;
    private string? _lastHealthKind;
    public string? ConnectionLogPath => _journal?.Path;
    public string? ConnectionNotice => ReadHealth().Text;
    private ConnectionHealth.Observation ReadHealth()
    {
        var observation = _health.Read(Status);
        string? previous = Interlocked.Exchange(ref _lastHealthKind, observation.Kind);
        if (previous != observation.Kind && (previous is not null || observation.Kind is not ("healthy" or "inactive")))
            NoteConnection($"network {observation.Kind}: {observation.Reason}; stale={observation.StaleSeconds:F1}s");
        return observation;
    }
    private void ObserveAuthority(JsonElement frame)
    {
        if (!frame.TryGetProperty("t", out var type) || type.GetString() != "state" ||
                !frame.TryGetProperty("d", out var state) || !state.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array) return;
        long? acknowledgment = null;
        foreach (var player in players.EnumerateArray())
            if (player.TryGetProperty("id", out var id) && id.GetString() == PlayerId &&
                    player.TryGetProperty("ack", out var ack) && ack.ValueKind == JsonValueKind.Number && ack.TryGetInt64(out long sequence))
            { acknowledgment = sequence; break; }
        _health.State(acknowledgment);
    }
    private async Task WatchReceiveAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var health = ReadHealth();
            if (health.StaleSeconds > 0 && health.InboundSeconds >= 3)
                throw new IOException("Receive watchdog: no inbound WebSocket progress for 3 seconds in an active match.");
        }
    }
    private async Task MonitorSilenceAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (IsEnded) return;
                foreach (var sample in Bomber.Game.Diagnostics.InputSilenceTelemetry.Drain())
                    if (sample.LobbyId == _lobbyId)
                        NoteConnection($"authority input {sample.Kind}: player={sample.PlayerId}; age_ms={sample.AgeMilliseconds:F0}");
                var health = ReadHealth();
                // A terminal outage bound is independent of fast reconnect
                // tuning. Do not wait forever on a half-open TCP connection.
                if (!IsEnded && health.StaleSeconds >= 30)
                {
                    NoteConnection($"terminal outage: {health.Reason}; stale={health.StaleSeconds:F1}s");
                    Fail("Connection lost. Return to the menu to reconnect.");
                    await _lifetime.CancelAsync(); _socket?.Abort(); return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}

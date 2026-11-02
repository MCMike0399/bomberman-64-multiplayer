using Bomber.Game.Diagnostics;

namespace Bomber.Server;

/// <summary>Reports input starvation without inspecting credentials or pausing simulation.</summary>
public sealed class InputSilenceReporter(ILogger<InputSilenceReporter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                foreach (var sample in InputSilenceTelemetry.Drain())
                    logger.LogInformation("client_input_state kind={Kind} room={Room} player={Player} age_ms={Age}",
                        sample.Kind, sample.LobbyId, sample.PlayerId, sample.AgeMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

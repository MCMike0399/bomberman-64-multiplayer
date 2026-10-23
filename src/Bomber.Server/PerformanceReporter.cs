using System.Diagnostics;
using Burbuja.Realtime.Abstractions;
using Burbuja.Realtime.Lobbies;

namespace Bomber.Server;

/// <summary>Small periodic receipts distinguish game work, scheduler gaps and process GC pauses.</summary>
public sealed class PerformanceReporter(LobbyManager lobbies, ILogger<PerformanceReporter> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var process = Process.GetCurrentProcess();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        long allocations = GC.GetTotalAllocatedBytes(false);
        var cpu = process.TotalProcessorTime;var pause = GC.GetTotalPauseDuration();long sampledAt=Stopwatch.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                process.Refresh();
                long nextAllocations = GC.GetTotalAllocatedBytes(false);
                var nextCpu = process.TotalProcessorTime;var nextPause = GC.GetTotalPauseDuration();long nextSample=Stopwatch.GetTimestamp();double seconds=Stopwatch.GetElapsedTime(sampledAt,nextSample).TotalSeconds;
                foreach (var lobby in lobbies.Lobbies)
                {
                    var room = lobby.Snapshot();
                    if (room.State is LobbyState.Ended or LobbyState.Closed) continue;
                    var p = room.Performance;
                    int bots = int.TryParse(lobby.Settings.GetValueOrDefault("bots"), out var configured) ? Math.Clamp(configured, 0, 7) : 0;
                    log.LogInformation("game_perf room={Room} humans={Humans} cpus={Bots} window_ticks={Ticks} avg_tick_ms={Average:F3} max_tick_ms={Max:F3} overruns={Overruns} dropped_ticks={Dropped} busy={Busy:F4} sample_seconds={Seconds:F3} process_cpu_ms={Cpu:F1} process_allocated_kib={Allocated:F1} process_gc_pause_ms={Pause:F3} threadpool_queue={Queue}",
                        room.LobbyId, room.Members.Count(m => m.Connected), bots, p.TicksInWindow, p.AverageTickMilliseconds, p.MaxTickMilliseconds, p.Overruns, p.DroppedTicks, p.Busy,
                        seconds, (nextCpu - cpu).TotalMilliseconds, (nextAllocations - allocations) / 1024d, (nextPause - pause).TotalMilliseconds, ThreadPool.PendingWorkItemCount);
                }
                allocations = nextAllocations;cpu = nextCpu;pause = nextPause;sampledAt=nextSample;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

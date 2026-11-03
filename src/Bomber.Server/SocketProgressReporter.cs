using System.Collections.Concurrent;
using System.Diagnostics;

namespace Bomber.Server;

/// <summary>Passive socket progress and snapshot queue-age observations; never changes delivery.</summary>
public sealed class SocketProgressReporter(ILogger<SocketProgressReporter> logger) : BackgroundService
{
    internal Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;
    private readonly ConcurrentDictionary<string, Progress> _connections = new();
    internal Progress Open(string id)
    {
        var progress = new Progress(id, Clock()); _connections[id] = progress;
        return progress;
    }
    internal void Closed(Progress progress)
    {
        if (_connections.TryRemove(progress.Id, out _))
            logger.LogInformation("ws_client_closed connection={Connection} room={Room} player={Player} sent_frames={Frames} max_send_wait_ms={Wait}",
                progress.Id, progress.Room, progress.Player, progress.SentFrames, Math.Round(progress.MaximumSendMilliseconds, 1));
    }
    internal long BeginSend(Progress progress, int bytes)
    {
        lock (progress.Gate) { progress.SendBytes = bytes; return progress.SendStarted = Clock(); }
    }
    internal void Observe(long now)
    {
        foreach (var progress in _connections.Values)
        {
            double receiveAge, sendAge = 0; bool receiveReport = false, receiveResumed = false, sendReport = false;
            int bytes;
            lock (progress.Gate)
            {
                receiveAge = Stopwatch.GetElapsedTime(Volatile.Read(ref progress.LastReceive), now).TotalMilliseconds;
                if (receiveAge >= 1000 && (progress.LastReceiveReport == 0 || Stopwatch.GetElapsedTime(progress.LastReceiveReport, now).TotalSeconds >= 5))
                { progress.LastReceiveReport = now; receiveReport = true; }
                else if (receiveAge < 500 && progress.LastReceiveReport != 0)
                { progress.LastReceiveReport = 0; receiveResumed = true; }
                long started = progress.SendStarted; bytes = progress.SendBytes;
                if (started != 0)
                {
                    sendAge = Stopwatch.GetElapsedTime(started, now).TotalMilliseconds;
                    if (sendAge >= 500 && (progress.LastSendReport == 0 || Stopwatch.GetElapsedTime(progress.LastSendReport, now).TotalSeconds >= 5))
                    { progress.LastSendReport = now; sendReport = true; }
                }
            }
            if (receiveReport) logger.LogInformation("ws_client_receive_silence connection={Connection} room={Room} player={Player} age_ms={Age}", progress.Id, progress.Room, progress.Player, Math.Round(receiveAge, 1));
            if (receiveResumed) logger.LogInformation("ws_client_receive_resumed connection={Connection} room={Room} player={Player}", progress.Id, progress.Room, progress.Player);
            if (sendReport) logger.LogInformation("ws_client_send_stall connection={Connection} room={Room} player={Player} pending_write_bytes={Bytes} wait_ms={Wait}", progress.Id, progress.Room, progress.Player, bytes, Math.Round(sendAge, 1));
        }
    }
    internal void Backlog(Progress progress, double ageMilliseconds, int bytes)
    {
        long now = Clock();
        lock (progress.Gate)
        {
            if (ageMilliseconds < 250 || progress.LastBacklogReport != 0 && Stopwatch.GetElapsedTime(progress.LastBacklogReport, now).TotalSeconds < 5) return;
            progress.LastBacklogReport = now;
        }
        logger.LogInformation("ws_snapshot_backlog connection={Connection} room={Room} player={Player} snapshot_age_at_write_ms={Age} bytes={Bytes}", progress.Id, progress.Room, progress.Player, Math.Round(ageMilliseconds, 1), bytes);
    }
    internal void FinishedSend(Progress progress, long started, bool succeeded)
    {
        double elapsed = Stopwatch.GetElapsedTime(started, Clock()).TotalMilliseconds; bool report;
        lock (progress.Gate)
        {
            progress.MaximumSendMilliseconds = Math.Max(progress.MaximumSendMilliseconds, elapsed);
            if (succeeded) progress.SentFrames++;
            progress.SendStarted = 0; report = progress.LastSendReport != 0; progress.LastSendReport = 0;
        }
        if (report) logger.LogInformation("ws_client_send_finished connection={Connection} room={Room} player={Player} outcome={Outcome} send_wait_ms={Wait}", progress.Id, progress.Room, progress.Player, succeeded ? "sent" : "failed", Math.Round(elapsed, 1));
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try { while (await timer.WaitForNextTickAsync(stoppingToken)) Observe(Clock()); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    internal sealed class Progress(string id, long now)
    {
        internal readonly string Id = id;
        internal readonly object Gate = new();
        internal string Room = "-", Player = "-";
        internal long LastReceive = now, SendStarted, LastReceiveReport, LastSendReport, LastBacklogReport, SentFrames;
        internal int SendBytes;
        internal double MaximumSendMilliseconds;
    }
}

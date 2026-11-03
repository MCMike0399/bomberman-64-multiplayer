using System.Diagnostics;
using Bomber.Client.Diagnostics;

internal static class ReceiveDeadlineChecks
{
    internal static int Run()
    {
        int checks = 0; long now = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        var health = new ConnectionHealth { Clock = () => now };
        health.Connected(); health.Match(true); health.InputSent(1); health.State(1);
        now = 3 * Stopwatch.Frequency;
        var observed = health.ReceiveStall() ?? throw new Exception("No stall observation at the deadline");
        // This is the ordering seen in the Windows three-second boundary:
        // timeout sampled, inbound resumes, then the old timeout commits abort.
        now += Stopwatch.Frequency / 100; health.Inbound(); health.State(1);
        now += Stopwatch.Frequency / 4;
        bool aborted = false;
        Check(!health.TryAbortReceive(observed, () => aborted = true) && !aborted,
            "inbound restoration between the deadline observation and abort must preserve the live socket");
        now += 3 * Stopwatch.Frequency;
        observed = health.ReceiveStall() ?? throw new Exception("A subsequent genuine outage was lost");
        Check(!health.TryAbortReceive(observed, () => aborted = true),
            "a threshold observation gives boundary data a short settle interval before aborting");
        now += Stopwatch.Frequency / 4;
        Check(health.TryAbortReceive(observed, () => aborted = true) && aborted,
            "a continued genuine stall still aborts after the bounded settle interval");
        Check(!health.TryAbortReceive(observed, () => throw new Exception("duplicate abort")),
            "an old deadline observation cannot abort twice");
        health.Connected(); now += 4 * Stopwatch.Frequency;
        observed = health.ReceiveStall()!.Value;
        health.Match(false);
        Check(!health.TryAbortReceive(observed, () => throw new Exception("idle lobby abort")),
            "leaving the match invalidates a pending receive deadline");
        Console.WriteLine($"PASS {checks} receive deadline restoration checks");
        return checks;
    }
}

using System.Diagnostics;
using Bomber.Client.Diagnostics;

internal static class ConnectionHealthChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        long now = 0;
        var health = new ConnectionHealth { Clock = () => now };
        health.Connected(); health.Match(true); health.InputSent(1); health.State(1);
        now += (long)(.9 * Stopwatch.Frequency);
        Check(health.Read("Connected").Text is null, "sub-second gaps do not flicker an interruption notice");
        now += (long)(.2 * Stopwatch.Frequency);
        Check(health.Read("Connected").Text == "Connection interrupted", "missing incoming state is visible after one second");
        Check(health.Read("Connected").Kind != "reconnecting", "stale data never claims a reconnect attempt");
        Check(health.Read("Reconnecting (2/5)").Text == "Reconnecting (2/5)", "a real retry exposes its actual bounded attempt count");
        health.Inbound(); health.State(2); health.Read("Connected");
        now += Stopwatch.Frequency; health.Inbound(); health.State(3);
        Check(health.Read("Connected").Text is not null, "one healthy frame does not flicker the warning away");
        now += Stopwatch.Frequency; health.Inbound(); health.State(4);
        Check(health.Read("Connected").Text is null, "sustained fresh data clears the warning automatically");
        health.InputSent(100);
        now += 2 * Stopwatch.Frequency; health.Inbound(); health.State(4);
        Check(health.Read("Connected") is { Kind: "interrupted", Reason: "input acknowledgments" },
            "outgoing-only loss is detected while incoming snapshots remain fresh");
        health.Match(false);
        now += 100 * Stopwatch.Frequency;
        Check(health.Read("Connected").Text is null, "an idle lobby is not a stalled match");
        health.Connected(); health.Match(true);
        for (int sample = 0; sample < 1200; sample++)
        {
            now += Stopwatch.Frequency / 2; health.Inbound(); health.State(sample + 1); health.InputSent(sample + 1);
            if (health.Read("Connected").Text is not null) throw new Exception("Normal ten-minute stream triggered a false notice");
        }
        Check(true, "a deterministic ten-minute normal stream has no false liveness warning");
        var jitter = new ConnectionHealth { Clock = () => now };
        jitter.Connected(); jitter.Match(true);
        for (int sample = 0; sample < 10; sample++)
        {
            long nonce = now; jitter.Ping(nonce); now += (long)(.55 * Stopwatch.Frequency);
            jitter.Pong(nonce); jitter.Inbound(); jitter.State(sample + 1); jitter.InputSent(sample + 1); jitter.Read("Connected");
        }
        Check(jitter.Read("Connected").Text == "Connection unstable", "sustained high RTT is shown without claiming a reconnect");
        for (int sample = 0; sample < 25; sample++)
        {
            now += Stopwatch.Frequency / 2; long nonce = now; jitter.Ping(nonce); now += Stopwatch.Frequency / 50;
            jitter.Pong(nonce); jitter.Inbound(); jitter.State(100 + sample); jitter.InputSent(100 + sample); jitter.Read("Connected");
        }
        Check(jitter.Read("Connected").Text is null, "stable low RTT clears instability with hysteresis");
        Check(jitter.Pong(-1) is null, "unsolicited pong nonces cannot forge RTT diagnostics");
        var singleSpike = new ConnectionHealth { Clock = () => now };
        singleSpike.Connected(); singleSpike.Match(true);
        for (int sample = 0; sample < 12; sample++)
        {
            long nonce = now; singleSpike.Ping(nonce); now += (long)((sample == 3 ? .4 : .02) * Stopwatch.Frequency);
            singleSpike.Pong(nonce); singleSpike.Inbound(); singleSpike.State(sample + 1); singleSpike.InputSent(sample + 1);
            Check(singleSpike.Read("Connected").Text is null, "one isolated RTT spike does not become sustained instability");
            now += Stopwatch.Frequency / 2;
        }
        Console.WriteLine($"PASS {checks} connection health checks");
        return checks;
    }
}

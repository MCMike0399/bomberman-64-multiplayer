using System.Diagnostics;
using Bomber.Client.Diagnostics;

internal static class ConnectionOutageChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        long now = 0;
        var health = new ConnectionHealth { Clock = () => now };
        void At(double seconds) => now = (long)(seconds * Stopwatch.Frequency);
        void Start()
        { At(0); health.Match(false); health.Connected(); health.Match(true); health.InputSent(1); health.State(1); }
        Start(); At(3); health.Read("Connected");
        foreach (double handshakeCompleted in new[] { 12d, 24d })
        { At(handshakeCompleted); health.Connected(); health.InputSent(1); health.Read("Connected"); }
        At(31);
        Check(health.Read("Connected").OutageSeconds >= 30,
            "successful slow socket handshakes without gameplay must not renew the terminal outage budget");

        Start(); At(3); health.Read("Connected");
        foreach (double handshakeCompleted in new[] { 12d, 24d })
        {
            At(handshakeCompleted); health.Connected(); health.InputSent(1);
            health.Inbound(); health.State(0); health.Read("Connected");
        }
        At(31); health.Inbound(); health.State(0);
        var incomingOnly = health.Read("Connected");
        Check(incomingOnly.OutageSeconds >= 30 && incomingOnly.Reason == "input acknowledgments",
            "fresh incoming state without outgoing acknowledgment recovery cannot renew the outage budget");

        Start(); At(3); health.Read("Connected"); At(10); health.Connected();
        health.InputSent(1); health.Inbound(); health.State(0);
        Check(health.Read("Connected").OutageSeconds >= 9,
            "a new generation's state alone does not discard an outstanding acknowledgment outage");
        health.State(500);
        Check(health.Read("Connected").OutageSeconds >= 9,
            "a previous-generation ACK beyond the new sender's sequence is not useful input recovery");
        health.State(1);
        Check(health.Read("Connected").OutageSeconds == 0,
            "useful state and fresh acknowledged input clear a recovered ten-second outage");
        At(11); health.InputSent(2); health.Inbound(); health.State(2);
        Check(health.Read("Connected").OutageSeconds == 0,
            "healthy recovery does not inherit the previous outage's terminal deadline");
        Console.WriteLine($"PASS {checks} cross-generation outage deadline checks");
        return checks;
    }
}

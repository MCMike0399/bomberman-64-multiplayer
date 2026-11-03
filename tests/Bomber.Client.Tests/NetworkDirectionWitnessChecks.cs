using BlastCircuit.Testing.Fixtures;

internal static class NetworkDirectionWitnessChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        // Exact authority/ACK samples from 7561b8c's failed native trace,
        // frames60..64. Five rendered observations cover 1.319 seconds of
        // incoming authority and 528 ms with an unchanged affected-player ACK.
        // Successful-send progression is controlled test input: the original
        // trace did not record this counter. The corrected native receipt does.
        var real = new NetworkDirectionWitness(1793641162531, 54, 60);
        foreach (var sample in new (long Time, long Ack, long Sent)[]
            { (1793641163057, 70, 80), (1793641163322, 72, 90), (1793641163587, 72, 100), (1793641163850, 72, 110) })
            real.Observe(sample.Time, sample.Ack, sample.Sent);
        Check(real.IncomingWhileAcknowledgmentsStall && real.AuthorityAdvanceMilliseconds == 1319
                && real.UnchangedAcknowledgmentMilliseconds == 528,
            "real low-frame-rate trace proves incoming progress and ACK stall without a render-poll count assumption");
        var healthy = new NetworkDirectionWitness(0, 0, 0);
        for (int sample = 1; sample <= 8; sample++) healthy.Observe(sample * 250, sample, sample + 1);
        Check(!healthy.IncomingWhileAcknowledgmentsStall, "healthy advancing ACKs cannot masquerade as outgoing-only loss");
        var frozen = new NetworkDirectionWitness(1000, 10, 20);
        for (int poll = 0; poll < 100; poll++) frozen.Observe(1000, 10, 30 + poll);
        Check(!frozen.IncomingWhileAcknowledgmentsStall, "repeated rendered reads of a frozen snapshot are not incoming progress");
        real.Observe(1793641162000, 1, 120);
        Check(real.AuthorityAdvanceMilliseconds == 1319, "an older snapshot cannot fabricate progress or reset the ACK witness");
        Check(!real.Observe(1793641163850, 72, 120), "a previously valid witness still requires a newly advancing authority sample");
        var idle = new NetworkDirectionWitness(0, 10, 20); idle.Observe(1500, 10, 20);
        Check(!idle.IncomingWhileAcknowledgmentsStall, "an idle sender cannot establish outgoing loss");
        var acknowledged = new NetworkDirectionWitness(0, 0, 0);
        acknowledged.Observe(1000, 20, 20); acknowledged.Observe(2000, 20, 20);
        Check(!acknowledged.IncomingWhileAcknowledgmentsStall, "fully acknowledged sends are not an outstanding input stall");
        real.Observe(1793641164113, 120, 130);
        Check(!real.IncomingWhileAcknowledgmentsStall, "fresh acknowledgment progress clears the outgoing-stall witness");
        Console.WriteLine($"PASS {checks} native network-direction witness checks");
        return checks;
    }
}

using System.Diagnostics;
using System.Text.Json;
using Bomber.Client.Peer;

/// <summary>Failed-link and stalled-handshake recovery: one offerer per pair, bounded backoff,
/// attempt generations that reject stale signaling, healthy links untouched.</summary>
internal static class PeerLinkRecoveryChecks
{
    private sealed class FakeLink : IPeerLink
    {
        public bool Connected { get; set; }
        public bool Failed { get; set; }
        public bool CaptureDiagnostics { get; init; }
        public PeerLinkDiagnosticContext? Context { get; private set; }
        public void SetDiagnosticContext(PeerLinkDiagnosticContext context) => Context = context;
        public bool? Offer { get; private set; }
        public bool Disposed { get; private set; }
        public List<JsonElement> Accepted { get; } = [];
        public event Action<JsonElement>? Signal;
        public event Action<byte[]>? Received { add { } remove { } }
        public void Start(bool offer) => Offer = offer;
        public void AcceptSignal(JsonElement signal) => Accepted.Add(signal);
        public void Poll() { }
        public bool Send(byte[] packet) => Connected;
        public void Raise(object signal) => Signal?.Invoke(JsonSerializer.SerializeToElement(signal));
        public void Dispose() => Disposed = true;
    }

    private static JsonElement Status(params string[] players) => StatusVersion(1, players);
    private static JsonElement StatusVersion(int connection, params string[] players) => JsonSerializer.SerializeToElement(new
    {
        t = "party.status",
        d = new
        {
            leaderId = players[0], phase = "lobby", match = 1, canStart = true, bots = 0, hosting = "auto", hostingPolicy = "auto",
            adaptive = true, epoch = 4, seed = 7,
            players = players.Select((id, slot) => new { playerId = id, displayName = id, slot, connected = true, appearance = slot, connection }).ToArray(),
        },
    });

    private static JsonElement Signal(string from, object payload) => JsonSerializer.SerializeToElement(new
    { t = "peer.signal", d = new { epoch = 4, from, payload } });

    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
        // A native peer can stay Connecting forever without reporting Failed.
        // Drive the real polling/recovery path with its monotonic clock seam.
        long connectingClock = 0;
        var connectingLinks = new List<FakeLink>();
        using var connecting = new PeerRoomClient("a", "lobby", () => { var link = new FakeLink(); connectingLinks.Add(link); return link; },
            (_, _) => { }, _ => { }, () => []) { Clock = () => connectingClock };
        connecting.Handle(Status("a", "b"));
        connectingClock = 14 * Stopwatch.Frequency; connecting.Advance();
        Check(connectingLinks.Count == 1, "a Connecting peer keeps its full handshake window");
        connectingClock = 15 * Stopwatch.Frequency; connecting.Advance();
        Check(connectingLinks.Count == 2 && connectingLinks[0].Disposed && connecting.Recoveries == 1,
            "a peer stuck Connecting without Failed is replaced after its bounded handshake window");

        connecting.Advance();
        Check(connectingLinks.Count == 2, "a replacement Connecting peer gets a fresh full handshake window");
        for (int attempt = 0; attempt < 10; attempt++)
        { connectingClock += 15 * Stopwatch.Frequency; connecting.Advance(); }
        Check(connecting.Recoveries == PeerRoomClient.RecoveryAttempts && connectingLinks.Count == 1 + PeerRoomClient.RecoveryAttempts,
            "stalled Connecting recovery keeps the existing per-epoch five-attempt cap");
        connecting.Handle(StatusVersion(2, "a", "b"));
        int rejoinedCount = connectingLinks.Count;
        connectingClock += 14 * Stopwatch.Frequency; connecting.Advance();
        Check(connectingLinks.Count == rejoinedCount, "a member reconnect starts a fresh handshake window");
        connectingClock += Stopwatch.Frequency; connecting.Advance();
        Check(connectingLinks.Count == rejoinedCount + 1, "a member reconnect resets the bounded recovery allowance");

        long healthyClock = 0;
        var healthyLinks = new List<FakeLink>();
        using var healthy = new PeerRoomClient("a", "lobby", () => { var link = new FakeLink(); healthyLinks.Add(link); return link; },
            (_, _) => { }, _ => { }, () => []) { Clock = () => healthyClock };
        healthy.Handle(Status("a", "b"));
        healthyLinks[0].Connected = true;
        healthyClock = 60 * Stopwatch.Frequency; healthy.Advance();
        Check(healthyLinks.Count == 1, "a connected native link is never timed out");
        healthyLinks[0].Connected = false;
        healthyClock += 60 * Stopwatch.Frequency; healthy.Advance();
        Check(healthyLinks.Count == 1, "an established link's transient disconnect does not become a handshake timeout");

        long clock = 0;
        var offererLinks = new List<FakeLink>(); var sent = new List<(string Type, JsonElement Data)>();
        // "a" < "b": during adaptive preflight the lexically smaller id offers.
        var offerer = new PeerRoomClient("a", "lobby", () => { var link = new FakeLink(); offererLinks.Add(link); return link; },
            (type, data) => sent.Add((type, JsonSerializer.SerializeToElement(data))), _ => { }, () => []) { Clock = () => clock };
        offerer.Handle(Status("a", "b"));
        Check(offererLinks.Count == 1 && offererLinks[0].Offer == true, "the designated offerer creates the first link");
        offererLinks[0].Raise(new { kind = "sdp", type = "offer", sdp = "first" });
        Check(sent.Last().Data.GetProperty("payload").GetProperty("g").GetInt32() == 0, "the first attempt's signals carry generation 0");

        offererLinks[0].Connected = true; offerer.Advance();
        offererLinks[0].Connected = false; offerer.Advance();
        Check(offererLinks.Count == 1 && offerer.Recoveries == 0, "healthy and transiently disconnected links are never recreated");

        offererLinks[0].Failed = true; offerer.Advance();
        Check(offererLinks.Count == 2 && offererLinks[0].Disposed && offererLinks[1].Offer == true && offerer.Recoveries == 1,
            "a terminally failed link is replaced by a fresh offer, not re-offered on the closed peer");
        offererLinks[1].Raise(new { kind = "sdp", type = "offer", sdp = "second" });
        Check(sent.Last().Data.GetProperty("payload").GetProperty("g").GetInt32() == 1, "the replacement attempt carries generation 1");

        offerer.Handle(Signal("b", new { kind = "sdp", type = "answer", sdp = "late", g = 0 }));
        Check(offererLinks[1].Accepted.Count == 0, "an answer for the replaced attempt is ignored");
        offerer.Handle(Signal("b", new { kind = "sdp", type = "answer", sdp = "current", g = 1 }));
        Check(offererLinks[1].Accepted.Count == 1, "the current attempt's answer is accepted");

        offererLinks[1].Failed = true; offerer.Advance();
        Check(offererLinks.Count == 2, "recovery waits for its backoff instead of retrying every frame");
        for (int attempt = 0; attempt < 10; attempt++) { clock += 30 * Stopwatch.Frequency; offererLinks[^1].Failed = true; offerer.Advance(); }
        Check(offerer.Recoveries == PeerRoomClient.RecoveryAttempts, "terminal recovery is bounded per authority epoch");

        long attempts = offerer.Recoveries;
        var fresh = new List<FakeLink>(); clock = 0;
        var recovered = new PeerRoomClient("a", "lobby", () => { var link = new FakeLink(); fresh.Add(link); return link; }, (_, _) => { }, _ => { }, () => []) { Clock = () => clock };
        recovered.Handle(Status("a", "b"));
        fresh[0].Failed = true; recovered.Advance();
        fresh[1].Connected = true; recovered.Advance();
        clock += 30 * Stopwatch.Frequency; fresh[1].Connected = false; fresh[1].Failed = true; recovered.Advance();
        Check(recovered.Recoveries == 2 && fresh.Count == 3, "a successful reconnection resets backoff while preserving the epoch attempt count");

        for (int failure = 0; failure < 10; failure++)
        {
            fresh[^1].Connected = true; recovered.Advance();
            fresh[^1].Connected = false; fresh[^1].Failed = true; recovered.Advance();
        }
        Check(recovered.Recoveries == PeerRoomClient.RecoveryAttempts, "brief successes cannot bypass the per-epoch recovery cap");

        var answererLinks = new List<FakeLink>(); var answers = new List<JsonElement>();
        var answerer = new PeerRoomClient("b", "lobby", () => { var link = new FakeLink(); answererLinks.Add(link); return link; },
            (type, data) => { if (type == "peer.signal") answers.Add(JsonSerializer.SerializeToElement(data)); }, _ => { }, () => []) { Clock = () => clock };
        answerer.Handle(Status("a", "b"));
        clock += 60 * Stopwatch.Frequency; answerer.Advance();
        Check(answererLinks.Count == 1 && answerer.Recoveries == 0, "a stalled answerer never starts a competing offer");
        Check(answererLinks.Count == 1 && answererLinks[0].Offer == false, "the other member answers instead of offering");
        answerer.Handle(Signal("a", new { kind = "ice", media = "0", index = 0, candidate = "early", g = 2 }));
        Check(answererLinks.Count == 2 && answererLinks[0].Disposed && answererLinks[1].Accepted.Count == 1,
            "new-attempt ICE reaches a fresh answerer before SDP, never the old connection");
        int beforeStaleCallback = answers.Count;
        answererLinks[0].Raise(new { kind = "sdp", type = "answer", sdp = "disposed" });
        Check(answers.Count == beforeStaleCallback, "a replaced link cannot publish callbacks stamped as the current attempt");
        answerer.Handle(Signal("a", new { kind = "sdp", type = "offer", sdp = "retry", g = 2 }));
        answerer.Handle(Signal("a", new { kind = "ice", media = "0", index = 0, candidate = "stale", g = 1 }));
        answerer.Handle(Signal("a", new { kind = "ice", media = "0", index = 0, candidate = "current", g = 2 }));
        Check(answererLinks[1].Accepted.Count == 3 && answererLinks[1].Accepted.All(s => s.GetProperty("g").GetInt32() == 2),
            "the answerer follows a newer attempt and drops stale ICE from a replaced one");
        answererLinks[1].Raise(new { kind = "sdp", type = "answer", sdp = "reply" });
        Check(answers.Last().GetProperty("payload").GetProperty("g").GetInt32() == 2, "answers carry the attempt they reply to");
        answerer.Handle(StatusVersion(2, "a", "b"));
        var rejoined = answererLinks[^1];
        answerer.Handle(Signal("a", new { kind = "sdp", type = "offer", sdp = "rejoined", g = 0 }));
        Check(rejoined.Accepted.Count == 1, "a new coordinator connection starts fresh attempt generations");
        recovered.Handle(StatusVersion(2, "a", "b"));
        fresh[^1].Raise(new { kind = "sdp", type = "offer", sdp = "rejoined" });
        Check(fresh[^1].Offer == true, "the designated offerer is preserved across a member reconnect");
        Check(attempts == PeerRoomClient.RecoveryAttempts, "recovery counters are reported");
        var observedLinks = new List<FakeLink>();
        using var observed = new PeerRoomClient("b", "lobby", () => { var link = new FakeLink { CaptureDiagnostics = true }; observedLinks.Add(link); return link; },
            (_, _) => { }, _ => { }, () => []);
        observed.Handle(Status("a", "b"));
        Check(observedLinks[0].Context is { LocalPlayerId: "b", RemotePlayerId: "a", Epoch: 4, Offerer: false, LocalGeneration: 0, RemoteGeneration: 0 },
            "QA context identifies the actual role, members, epoch and generation before native Start");
        observed.Handle(Signal("a", new { kind = "sdp", type = "offer", sdp = "retry", g = 2 }));
        Check(observedLinks[^1].Context is { RemoteGeneration: 2, AcceptedSignalGeneration: 2 } && observedLinks[0].Disposed,
            "replacement and accepted wire generation are retained without changing recovery ownership");
        Console.WriteLine($"PASS {checks} peer link recovery checks");
        return checks;
    }
}

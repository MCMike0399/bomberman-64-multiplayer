using System.Text.Json;
using Bomber.Game.Online;
using Burbuja.Realtime.Abstractions;

internal static class PublicMatchmakingChecks
{
    public static int Run()
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); passed++; Console.WriteLine("PASS " + name); }

        var timing = new Fixture();
        timing.Join("one"); timing.Join("two"); timing.At(8);
        Check(timing.Waiting && !timing.Context.Frames.Any(f => f.Type == "stage"),
            "Quick Play must give two anonymous players a full 60-second lobby countdown before starting");
        Check(timing.StartAt == 60000, "the second player immediately receives the shared one-minute deadline");
        timing.At(59.9);
        Check(timing.Waiting && timing.Context.Claims.Count == 0, "no gameplay or execution capacity is taken before the deadline");
        timing.At(60);
        Check(!timing.Waiting && timing.Status.GetProperty("match").GetInt32() == 1, "stable two-player roster starts automatically at sixty seconds");
        Check(timing.StartAt is null && timing.Context.Claims.GetValueOrDefault("simulation") == 1,
            "starting clears the deadline and reserves one simulation");
        Check(!timing.Room.AcceptsNewPlayers, "public matchmaking never backfills strangers into a match already in progress");
        timing.Join("reserved-before-start");
        Check(timing.Context.Kicked.Contains("reserved-before-start") && timing.Status.GetProperty("players").GetArrayLength() == 2,
            "a new socket with an earlier reservation cannot join after the public match has started");

        var permissions = new Fixture();
        var owner = permissions.Join("owner");
        var originalRules = permissions.Status.GetProperty("settings").GetRawText();
        permissions.Send(owner, "party.settings", new { wins = "9", bots = "7", stage = "up-and-down" });
        Check(permissions.Status.GetProperty("settings").GetRawText() == originalRules,
            "the first anonymous player cannot change global rules while waiting alone");
        permissions.At(120);
        Check(permissions.Waiting && permissions.StartAt is null && permissions.Context.Claims.Count == 0,
            "a lone player waits without a countdown or gameplay");
        var guest = permissions.Join("guest");
        long? deadline = permissions.StartAt;
        permissions.Send(owner, "party.start", new { }); permissions.Send(guest, "party.start", new { });
        Check(permissions.Waiting && permissions.StartAt == deadline && permissions.Context.Claims.Count == 0,
            "neither anonymous owner nor guest can bypass or shorten the countdown");
        Check(!permissions.Status.GetProperty("canStart").GetBoolean(), "public status never grants manual-start permission");
        permissions.Send(owner, "party.settings", new { wins = "9" });
        permissions.Send(guest, "party.settings", new { bots = "7" });
        Check(permissions.Status.GetProperty("settings").GetRawText() == originalRules && permissions.StartAt == deadline,
            "public rule edits cannot alter or cancel an active countdown");
        permissions.Send(owner, "appearance", new { index = 7 });
        Check(permissions.StartAt == deadline && permissions.Status.GetProperty("players")[0].GetProperty("appearance").GetInt32() == 7,
            "personal suit changes remain available without resetting matchmaking");

        var churn = new Fixture();
        var first = churn.Join("first"); var second = churn.Join("second");
        churn.At(59); churn.Join("third");
        Check(churn.StartAt == 119000, "a late arrival restarts the full minute for everyone");
        churn.At(60); Check(churn.Waiting, "the superseded deadline cannot start a changed roster");
        churn.At(70); churn.Leave(first);
        Check(churn.StartAt == 130000 && churn.Status.GetProperty("leaderId").GetString() == second.PlayerId,
            "owner departure with two remaining players restarts the minute and transfers bookkeeping leadership");
        churn.Send(second, "party.start", new { });
        Check(churn.Waiting, "replacement leadership grants no public start override");
        churn.At(80); churn.Disconnect(second);
        Check(churn.StartAt is null && churn.Waiting, "dropping below two connected players cancels the visible timer immediately");
        churn.At(140); Check(churn.Waiting && churn.Context.Claims.Count == 0, "an old timer cannot start the remaining lone player");
        churn.Reconnect(second);
        Check(churn.StartAt == 200000, "a retained player reconnect receives a fresh full minute");
        churn.At(199.9); Check(churn.Waiting, "reconnect does not inherit elapsed time from an abandoned countdown");
        churn.At(200); Check(!churn.Waiting, "a stable roster starts after reconnect's new deadline");

        var cleanup = new Fixture();
        cleanup.Join("one"); cleanup.Join("two"); var leaving = cleanup.Join("three");
        cleanup.At(10); cleanup.Disconnect(leaving);
        Check(cleanup.StartAt == 70000, "three-to-two disconnect grants the remaining players a fresh minute");
        cleanup.At(20); cleanup.Leave(leaving);
        Check(cleanup.StartAt == 70000, "retiring an already disconnected seat does not restart the same connected roster again");
        cleanup.At(70); Check(!cleanup.Waiting, "disconnect cleanup cannot strand the remaining two players");

        var full = new Fixture();
        for (int i = 0; i < 8; i++) { full.Context.Tick = i * 30; full.Join("p" + i); }
        Check(full.Waiting && full.StartAt == 67000, "even a full eight-player room honors the latest arrival's minute");
        full.At(66.9); Check(full.Waiting, "a full room has no early-start shortcut");
        full.At(67);
        Check(!full.Waiting && full.Status.GetProperty("players").GetArrayLength() == 8, "eight stable players start together at the deadline");

        var capacity = new Fixture(); capacity.Context.Limits["simulation"] = 0;
        capacity.Join("one"); capacity.Join("two"); capacity.At(60);
        Check(capacity.Waiting && capacity.StartAt == 60000 && capacity.Context.Claims.Count == 0,
            "expired countdown waits safely when no execution capacity is available");
        Check(capacity.Status.GetProperty("notice").GetString()!.Contains("capacity"), "capacity wait has an explicit player-facing explanation");
        int statuses = capacity.Context.Frames.Count(f => f.Type == "party.status");
        capacity.At(62); capacity.At(64);
        Check(capacity.Context.Frames.Count(f => f.Type == "party.status") == statuses, "unchanged capacity retries do not churn lobby UI");
        capacity.At(65); var newcomer = capacity.Join("newcomer"); capacity.Context.Limits["simulation"] = 1;
        Check(capacity.StartAt == 125000 && capacity.Status.GetProperty("notice").GetString() == "",
            "a new arrival clears stale capacity text and restarts the minute after an expired wait");
        capacity.At(66); Check(capacity.Waiting, "free capacity cannot revive a start for the old roster");
        capacity.At(125); Check(!capacity.Waiting, "capacity recovery starts only after the new roster's full minute");
        capacity.Disconnect(newcomer);
        Check(!capacity.Waiting && capacity.StartAt is null, "ordinary guest disconnect does not restart a healthy dedicated match");
        capacity.Reconnect(newcomer);
        Check(!capacity.Waiting && capacity.Status.GetProperty("match").GetInt32() == 1,
            "an in-progress retained-seat reconnect keeps the match and does not create a lobby timer");

        var cancelled = new Fixture(); cancelled.Context.Limits["simulation"] = 0;
        var ca = cancelled.Join("a"); cancelled.Join("b"); cancelled.At(60); cancelled.Leave(ca);
        cancelled.Context.Limits["simulation"] = 1; cancelled.At(62);
        Check(cancelled.Waiting && cancelled.StartAt is null && cancelled.Context.Claims.Count == 0,
            "departure cancels expired automatic-start retries before capacity becomes free");

        var forged = new AdaptiveRoomChecks.RoomContext();
        var rules = forged.Settings.ToDictionary(); rules["bots"] = "7"; rules["wins"] = "9"; rules["hosting"] = "peer";
        var forgedRoom = new PublicAdaptiveRoomGame().CreateSession(new("forged", "blast-online", rules, 64));
        forgedRoom.OnPlayerJoined(forged, forged.Add("owner", 0));
        Check(forged.Status.GetProperty("bots").GetInt32() == 0 && forged.Status.GetProperty("settings").GetProperty("wins").GetString() == "2",
            "custom creation settings cannot give an anonymous player control over global rules");

        var peer = new Fixture(); peer.Context.Limits["simulation"] = 0;
        var peerOwner = peer.Join("owner"); var peerGuest = peer.Join("guest"); peer.Join("third");
        peer.Context.Tick = 59 * 30; peer.QualifyPeers(); peer.At(60);
        Check(!peer.Waiting && peer.Status.GetProperty("hosting").GetString() == "peer" && peer.Context.Claims.GetValueOrDefault("peer-relay") == 1,
            "an expired public countdown can automatically select a qualified fallback player host");
        Check(!peer.Status.GetProperty("notice").GetString()!.Contains("owner"), "public fallback-host notice never promises private-room owner powers");
        int peerEpoch = peer.Status.GetProperty("epoch").GetInt32();
        peer.Leave(peerGuest);
        Check(!peer.Waiting && peer.StartAt is null && peer.Status.GetProperty("epoch").GetInt32() == peerEpoch,
            "normal guest departure preserves a running public peer match");
        peer.Context.Tick = 61 * 30; peer.Disconnect(peerOwner);
        Check(peer.Waiting && peer.StartAt is null && peer.Context.Claims.Count == 0,
            "fallback host loss releases the claim and leaves one player waiting without a stale timer");
        peer.Reconnect(peerOwner);
        Check(peer.StartAt == 121000, "fallback-host recovery starts a fresh minute instead of resurrecting the interrupted match");

        var rematch = new Fixture(); rematch.Context.Limits["simulation"] = 0;
        var winner = rematch.Join("winner"); rematch.Join("other");
        rematch.Context.Tick = 59 * 30; rematch.QualifyPeers(); rematch.At(60);
        rematch.Send(winner, "peer.finished", new { epoch = rematch.Status.GetProperty("epoch").GetInt32(), result = new { reason = "complete", winners = new[] { winner.PlayerId } } });
        Check(rematch.Status.GetProperty("phase").GetString() == "results" && rematch.StartAt is null && rematch.Context.Claims.Count == 0 && !rematch.Room.AcceptsNewPlayers,
            "public results release execution capacity without admitting strangers or counting down behind results");
        rematch.At(65);
        Check(rematch.Waiting && rematch.StartAt == 125000 && rematch.Room.AcceptsNewPlayers,
            "returning from results reopens public matchmaking with a fresh minute");
        rematch.Context.Limits["simulation"] = 1; rematch.At(124.9);
        Check(rematch.Waiting, "automatic rematches honor the same one-minute lobby rule");
        rematch.At(125);
        Check(!rematch.Waiting && rematch.Status.GetProperty("match").GetInt32() == 2,
            "automatic rematch starts with a monotonic match number after the new deadline");

        Console.WriteLine($"PASS {passed} public matchmaking checks");
        return passed;
    }

    private sealed class Fixture
    {
        public AdaptiveRoomChecks.RoomContext Context { get; } = new();
        public GameSession Room { get; }
        public JsonElement Status => Context.Status;
        public long? StartAt => Status.GetProperty("startAt").ValueKind == JsonValueKind.Number ? Status.GetProperty("startAt").GetInt64() : null;
        public bool Waiting => Status.GetProperty("phase").GetString() == "lobby";
        public Fixture() => Room = (GameSession)new PublicAdaptiveRoomGame().CreateSession(new("public-qa", "blast-online", Context.Settings, 64));
        public RealtimePlayer Join(string id)
        { var player = Context.Add(id, Context.Players.Count); Room.OnPlayerJoined(Context, player); return player; }
        public void At(double seconds) { Context.Tick = (long)Math.Round(seconds * 30); Room.OnTick(Context); }
        public void Send(RealtimePlayer player, string type, object data) => Room.OnMessage(Context, player, new(type, JsonSerializer.SerializeToElement(data), null, Context.Now));
        public void Disconnect(RealtimePlayer player) { Context.Connected.Remove(player.PlayerId); Room.OnPlayerConnectionChanged(Context, player, false); }
        public void Reconnect(RealtimePlayer player) { Context.Connected.Add(player.PlayerId); Room.OnPlayerConnectionChanged(Context, player, true); }
        public void Leave(RealtimePlayer player) { Context.Remove(player); Room.OnPlayerLeft(Context, player, LeaveReason.Left); }
        public void QualifyPeers()
        {
            foreach (var player in Context.Players) Send(player, "peer.hello", new { version = OnlineProtocol.Revision });
            int epoch = Status.GetProperty("epoch").GetInt32();
            foreach (var player in Context.Players) Send(player, "peer.quality", new { epoch, serverRtt = 120d, frameGap = 16d,
                links = Context.Players.Where(p => p != player).Select(p => new { id = p.PlayerId, rtt = 20d, loss = 0d, direct = false, samples = 5 }) });
        }
    }
}

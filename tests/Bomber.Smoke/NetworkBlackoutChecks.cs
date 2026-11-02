using System.Diagnostics;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;

/// <summary>Fault measurements through real product sessions; never targets production.</summary>
internal static class NetworkBlackoutChecks
{
    public static async Task Run(Uri server, string[] args)
    {
        if (!server.IsLoopback || server.Scheme != "http") throw new ArgumentException("Blackout fixtures require an owned loopback server.");
        string Option(string name, string fallback) => args.FirstOrDefault(x => x.StartsWith(name + "="))?.Split('=', 2)[1] ?? fallback;
        string scenario = Option("--blackout-case", "blackout-10");
        string output = Option("--blackout-output", "artifacts/network-blackout.json");
        string driver = Option("--blackout-driver", "proxy");
        double seconds = scenario switch { "blackout-3" => 3, "blackout-10" => 10, "blackout-45" => 45,
            "blackout-60" => 60, "upstream" or "downstream" => 10, "jitter" => 15, "normal" => 600, "reset" => 0,
            _ => throw new ArgumentException("Unknown blackout case") };
        if (driver is not ("proxy" or "tc")) throw new ArgumentException("Unknown fault driver.");
        var rows = new List<object>();
        var seen = new JsonElement?[3];
        var counts = new int[3];
        var lastAt = new double[3];
        var maxGap = new double[3];
        var clock = Stopwatch.StartNew();
        double? firstSnapshotAfterEnd = null, firstFreshAfterEnd = null, firstMovementAfterEnd = null;
        double start = double.PositiveInfinity, end = double.PositiveInfinity;
        double? heldXAtHalfSecond = null, heldXAtOneSecond = null;
        float? postAckX = null;
        PlayerInput? postFaultInput = null;
        (int Generation, long Sequence)? postFaultSent = null;
        int feedbackSamples = 0;
        var notices = new Dictionary<string, int>();
        int misleadingReconnectSamples = 0;
        object? failure = null;
        await using var proxy = new NetworkFaultProxy(server);
        await using var netem = driver == "tc" && scenario is not ("normal" or "reset") ? new NetemFault(proxy.Origin.Port) : null;
        var settings = new SessionSettings(Bots: 0, RoundSeconds: 300, SuddenDeath: false)
            { Hosting = "server", Advanced = new AdvancedRules { BlockPercent = 0, DropPercent = 0 } };
        await using var host = await NetworkSession.ConnectAsync(server, "Blackout control A", settings, NetworkMode.CreatePrivate);
        await using var control = await NetworkSession.ConnectAsync(server, "Blackout control B", settings, NetworkMode.JoinCode, host.JoinCode);
        await using var affected = await NetworkSession.ConnectAsync(proxy.Origin, "Blackout affected", settings, NetworkMode.JoinCode, host.JoinCode);
        NetworkSession[] sessions = [host, control, affected];
        JsonElement Player(JsonElement frame) => frame.GetProperty("d").GetProperty("players").EnumerateArray().Single(x => x.GetProperty("id").GetString() == affected.PlayerId);
        long ServerTime(JsonElement frame) => frame.GetProperty("d").GetProperty("serverTime").GetInt64();
        string? Feedback(NetworkSession session) => session.GetType().GetProperty("ConnectionNotice")?.GetValue(session) as string;
        void Pump()
        {
            for (int i = 0; i < sessions.Length; i++)
            {
                var session = sessions[i]; session.Advance(1d / 60);
                foreach (var frame in session.DrainMessages())
                {
                    if (frame.GetProperty("t").GetString() != "state") continue;
                    double at = clock.Elapsed.TotalSeconds;
                    if (lastAt[i] > 0) maxGap[i] = Math.Max(maxGap[i], at - lastAt[i]);
                    lastAt[i] = at; counts[i]++; seen[i] = frame.Clone();
                    if (!frame.GetProperty("d").GetProperty("players").EnumerateArray().Any(x => x.GetProperty("id").GetString() == affected.PlayerId)) continue;
                    var player = Player(frame); float x = player.GetProperty("x").GetSingle();
                    if (i == 0 && at >= start + .5 && heldXAtHalfSecond is null) heldXAtHalfSecond = x;
                    if (i == 0 && at >= start + 1 && heldXAtOneSecond is null) heldXAtOneSecond = x;
                    if (i == 2 && at >= end)
                    {
                        firstSnapshotAfterEnd ??= at - end;
                        if (seen[0] is { } healthy && ServerTime(healthy) - ServerTime(frame) <= 250) firstFreshAfterEnd ??= at - end;
                        if (postFaultSent is { } sent && sent.Generation == session.ConnectionGeneration && player.GetProperty("ack").GetInt64() >= sent.Sequence)
                        {
                            if (postAckX is { } before && postFaultInput is { } requested && (x - before) * requested.X > .02f)
                                firstMovementAfterEnd ??= at - end;
                            postAckX ??= x;
                        }
                    }
                    if (rows.Count < 40000) rows.Add(new { atSeconds = at, client = i, serverTime = ServerTime(frame),
                        tick = frame.GetProperty("k").GetInt64(), x, z = player.GetProperty("z").GetSingle(),
                        alive = player.GetProperty("alive").GetBoolean(), ack = player.GetProperty("ack").GetInt64(),
                        status = session.Status, generation = session.ConnectionGeneration, notice = Feedback(session) });
                }
            }
            if (postFaultInput is { } expected && affected.LatestInputSend is { } sentInput &&
                sentInput.Input == expected && sentInput.CapturedSequence == sentInput.SentSequence)
                postFaultSent ??= (sentInput.Generation, sentInput.SentSequence);
            if (clock.Elapsed.TotalSeconds >= start && Feedback(affected) is { Length: > 0 } notice)
            {
                feedbackSamples++; notices[notice] = notices.GetValueOrDefault(notice) + 1;
                if (notice.StartsWith("Reconnecting", StringComparison.Ordinal) && !affected.Status.StartsWith("Reconnecting", StringComparison.Ordinal)) misleadingReconnectSamples++;
            }
        }
        async Task Until(Func<bool> ready, double timeout, string reason)
        {
            var wait = Stopwatch.StartNew();
            while (!ready())
            {
                if (wait.Elapsed.TotalSeconds > timeout) throw new TimeoutException(reason);
                Pump(); await Task.Delay(10);
            }
        }
        try
        {
            await Until(() => host.Party?.CanStart == true, 15, "Three test peers did not become startable");
            host.RequestStart();
            await Until(() => seen.All(x => x is not null) && seen[0]!.Value.GetProperty("d").GetProperty("phase").GetInt32() == 1,
                15, "The owned match did not enter active play");
            string lobby = affected.LobbyId!; int match = affected.Party!.Match;
            int generation = affected.ConnectionGeneration;
            float initialX = Player(seen[0]!.Value).GetProperty("x").GetSingle();
            float direction = initialX < 10 ? 1 : -1;
            affected.SendInput(0, new PlayerInput(direction, 0));
            await Until(() => Math.Abs(Player(seen[0]!.Value).GetProperty("x").GetSingle() - initialX) > .05f, 5, "Movement witness never started");
            if (netem is not null) await netem.Apply(scenario != "downstream", scenario != "upstream", scenario == "jitter");
            start = clock.Elapsed.TotalSeconds; end = start + seconds;
            if (scenario == "reset") proxy.DropConnections();
            else if (netem is null && scenario == "jitter") proxy.Jitter(TimeSpan.FromSeconds(seconds));
            else if (netem is null && scenario != "normal") proxy.Pause(TimeSpan.FromSeconds(seconds), scenario != "downstream", scenario != "upstream");
            bool released = false;
            string? statusDuring = null;
            int healthyBefore = counts[0];
            while (clock.Elapsed.TotalSeconds < end)
            {
                Pump();
                if (clock.Elapsed.TotalSeconds >= start + 1.5) statusDuring ??= affected.Status;
                if (!released && clock.Elapsed.TotalSeconds >= start + .6) { affected.ReleaseInputs(); released = true; }
                await Task.Delay(10);
            }
            if (netem is not null) { await netem.Restore(); end = clock.Elapsed.TotalSeconds; }
            int healthyAfterFault = counts[0];
            postFaultInput = new PlayerInput(-direction, 0);
            affected.SendInput(0, postFaultInput.Value);
            var recovery = Stopwatch.StartNew();
            while (recovery.Elapsed.TotalSeconds < 8 && (firstFreshAfterEnd is null || firstMovementAfterEnd is null))
            { Pump(); await Task.Delay(10); }
            affected.ReleaseInputs(); Pump();
            bool terminalCase = scenario is "blackout-45" or "blackout-60";
            bool cleanTerminal = affected.IsEnded && affected.Status.Contains("Connection lost", StringComparison.OrdinalIgnoreCase);
            var checks = new Dictionary<string, bool>
            {
                ["healthy_control_kept_receiving"] = healthyAfterFault - healthyBefore > Math.Max(0, seconds * 5),
                ["healthy_control_max_gap_under_500ms"] = maxGap[0] < .5 && maxGap[1] < .5,
                ["same_match_preserved_or_cleanly_ended"] = terminalCase && cleanTerminal || !affected.IsEnded && affected.LobbyId == lobby && affected.Party?.Match == match,
                ["fresh_catchup_within_500ms"] = terminalCase && cleanTerminal || !affected.IsEnded && firstFreshAfterEnd is <= .5,
                ["fresh_input_accepted_and_movement_resumed"] = terminalCase && cleanTerminal || !affected.IsEnded && firstMovementAfterEnd is not null,
                ["reconnecting_notice_is_truthful"] = misleadingReconnectSamples == 0,
            };
            if (scenario is "blackout-3" or "blackout-10" or "downstream" or "upstream") checks["interruption_feedback_shown"] = feedbackSamples > 0;
            if (scenario is "blackout-3" or "blackout-10" or "blackout-45" or "blackout-60" or "upstream")
                checks["held_movement_neutral_after_500ms"] = heldXAtOneSecond is not null && heldXAtHalfSecond is not null && Math.Abs(heldXAtOneSecond.Value - heldXAtHalfSecond.Value) < .02;
            if (seconds >= 30 && scenario != "normal") checks["dead_network_ended_cleanly"] = affected.IsEnded && affected.Status.Contains("Connection lost", StringComparison.OrdinalIgnoreCase);
            if (scenario == "reset") checks["actual_reconnect_occurred"] = affected.ConnectionGeneration > generation;
            if (scenario == "jitter") checks["instability_feedback_shown"] = notices.Keys.Any(x => x.Contains("unstable", StringComparison.OrdinalIgnoreCase));
            if (scenario == "normal") checks["no_spurious_feedback_or_reconnect"] = feedbackSamples == 0 && affected.ConnectionGeneration == generation;
            var result = new { measurementCompleted = true, conforms = checks.Values.All(x => x), checks, scenario, driver,
                limitation = driver == "tc" ? "Managed product sessions, no renderer claim; port-scoped actual packet loss on hosted Linux." : "Managed product sessions, no renderer claim; proxy pauses are not packet-loss/RTO measurements.",
                platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                faultSeconds = seconds, faultStartSeconds = start, faultEndSeconds = end,
                affectedStatusDuring = statusDuring, affectedStatusAfter = affected.Status,
                generationBefore = generation, generationAfter = affected.ConnectionGeneration,
                sameLobby = affected.LobbyId == lobby, sameMatch = affected.Party?.Match == match,
                healthySnapshotsDuringFault = healthyAfterFault - healthyBefore,
                firstSnapshotAfterEndSeconds = firstSnapshotAfterEnd, firstFreshSnapshotAfterEndSeconds = firstFreshAfterEnd,
                firstMovementAfterEndSeconds = firstMovementAfterEnd, maxVisibleSnapshotGapSeconds = maxGap,
                heldMovementFromHalfToOneSecond = heldXAtOneSecond - heldXAtHalfSecond,
                connectionFeedbackSamples = feedbackSamples, noticeSamples = notices, misleadingReconnectSamples,
                ended = affected.IsEnded, connectionEvents = affected.ConnectionEvents,
                visibleSamples = rows };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            if (args.Contains("--assert-resilience") && checks.Values.Any(x => !x)) Environment.ExitCode = 1;
            Console.WriteLine($"MEASURED {scenario}: fresh-state after fault={firstFreshAfterEnd:F3}s; status={affected.Status}; generation={generation}->{affected.ConnectionGeneration}");
        }
        catch (Exception error)
        {
            failure = new { error = error.GetType().Name, error.Message, scenario, driver, status = affected.Status,
                connectionEvents = affected.ConnectionEvents, visibleSamples = rows };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(failure, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
    }
}

using BlastCircuit.Testing.Fixtures;
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
        async Task<NetworkSession> Connect(Uri origin, string name, NetworkMode mode, string? code = null, string? diagnostics = null)
        {
            var method = typeof(NetworkSession).GetMethod(nameof(NetworkSession.ConnectAsync))!;
            var values = method.GetParameters().Select(parameter => parameter.Name switch
            {
                "server" => (object)origin, "displayName" => name, "settings" => settings,
                "mode" => mode, "joinCode" => code, "diagnosticsDirectory" => diagnostics,
                "fastReceiveRecovery" => Option("--fast-receive-recovery", "on") != "off",
                _ => parameter.DefaultValue,
            }).ToArray();
            return await (Task<NetworkSession>)method.Invoke(null, values)!;
        }
        await using var host = await Connect(server, "Blackout control A", NetworkMode.CreatePrivate);
        await using var control = await Connect(server, "Blackout control B", NetworkMode.JoinCode, host.JoinCode);
        await using var affected = await Connect(proxy.Origin, "Blackout affected", NetworkMode.JoinCode, host.JoinCode,
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "network"));
        NetworkSession[] sessions = [host, control, affected];
        JsonElement Player(JsonElement frame) => frame.GetProperty("d").GetProperty("players").EnumerateArray().Single(x => x.GetProperty("id").GetString() == affected.PlayerId);
        long? Ack(JsonElement player) => player.TryGetProperty("ack", out var ack) && ack.ValueKind == JsonValueKind.Number && ack.TryGetInt64(out long value) ? value : null;
        long ServerTime(JsonElement frame) => frame.GetProperty("d").GetProperty("serverTime").GetInt64();
        string? Feedback(NetworkSession session) => session.GetType().GetProperty("ConnectionNotice")?.GetValue(session) as string;
        double? terminalAt = null;
        void Pump()
        {
            // The actual app samples held controls every rendered frame. A
            // reconnect deliberately clears cached input, so re-sample here too.
            if (postFaultInput is { } held) affected.SendInput(0, held);
            if (affected.IsEnded) terminalAt ??= clock.Elapsed.TotalSeconds - start;
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
                        if (postFaultSent is { } sent && sent.Generation == session.ConnectionGeneration && Ack(player) is { } acceptedSequence && acceptedSequence >= sent.Sequence &&
                            seen[0] is { } currentControl && ServerTime(currentControl) - ServerTime(frame) <= 250)
                        {
                            if (postAckX is { } before && postFaultInput is { } requested && (x - before) * requested.X > .02f)
                                firstMovementAfterEnd ??= at - end;
                            postAckX ??= x;
                        }
                    }
                    if (rows.Count < 40000) rows.Add(new { atSeconds = at, client = i, serverTime = ServerTime(frame),
                        tick = frame.GetProperty("k").GetInt64(), x, z = player.GetProperty("z").GetSingle(),
                        alive = player.GetProperty("alive").GetBoolean(), ack = Ack(player),
                        status = session.Status, generation = session.ConnectionGeneration, notice = Feedback(session), sentSequence = session.InputSequence });
                }
            }
            if (postFaultInput is { } expected && affected.LatestInputSend is { } sentInput &&
                sentInput.Input == expected && sentInput.CapturedSequence == sentInput.SentSequence)
                if (postFaultSent is null || postFaultSent.Value.Generation != sentInput.Generation)
                { postFaultSent = (sentInput.Generation, sentInput.SentSequence); postAckX = null; }
            for (int client = 0; client < sessions.Length; client++)
                if (lastAt[client] > 0 && !sessions[client].IsEnded)
                    maxGap[client] = Math.Max(maxGap[client], clock.Elapsed.TotalSeconds - lastAt[client]);
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
            while (!affected.IsEnded && recovery.Elapsed.TotalSeconds < 8 && (firstFreshAfterEnd is null || firstMovementAfterEnd is null))
            { Pump(); await Task.Delay(10); }
            postFaultInput = null; affected.ReleaseInputs(); Pump();
            double? feedbackClearedAfterEnd = null;
            var settle = Stopwatch.StartNew();
            while (!affected.IsEnded && !string.IsNullOrEmpty(Feedback(affected)) && settle.Elapsed.TotalSeconds < 8)
            { Pump(); await Task.Delay(10); }
            if (string.IsNullOrEmpty(Feedback(affected))) feedbackClearedAfterEnd = clock.Elapsed.TotalSeconds - end;
            bool terminalCase = scenario is "blackout-45" or "blackout-60";
            bool cleanTerminal = affected.IsEnded && affected.Status.Contains("Connection lost", StringComparison.OrdinalIgnoreCase);
            var checks = new Dictionary<string, bool>
            {
                ["healthy_control_kept_receiving"] = seconds > 0 ? healthyAfterFault - healthyBefore > seconds * 5 : counts[0] > healthyBefore,
                ["healthy_control_max_gap_under_500ms"] = maxGap[0] < .5 && maxGap[1] < .5,
                ["same_match_preserved_or_cleanly_ended"] = terminalCase && cleanTerminal || !affected.IsEnded && affected.LobbyId == lobby && affected.Party?.Match == match,
                ["latest_snapshot_catchup_within_500ms_of_first_frame"] = terminalCase && cleanTerminal || !affected.IsEnded && firstFreshAfterEnd is { } fresh && firstSnapshotAfterEnd is { } first && fresh - first <= .5,
                ["fresh_input_accepted_and_movement_resumed"] = terminalCase && cleanTerminal || !affected.IsEnded && firstMovementAfterEnd is not null,
                ["reconnecting_notice_is_truthful"] = misleadingReconnectSamples == 0,
                ["feedback_clears_after_recovery"] = terminalCase && cleanTerminal || !affected.IsEnded && string.IsNullOrEmpty(Feedback(affected)),
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
                clientAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(NetworkSession).Assembly.Location))).ToLowerInvariant(),
                gameAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(PlayerInput).Assembly.Location))).ToLowerInvariant(),
                fastReceiveRecoveryRequested = Option("--fast-receive-recovery", "on") != "off",
                faultSeconds = seconds, faultStartSeconds = start, faultEndSeconds = end,
                affectedStatusDuring = statusDuring, affectedStatusAfter = affected.Status,
                generationBefore = generation, generationAfter = affected.ConnectionGeneration,
                sameLobby = affected.LobbyId == lobby, sameMatch = affected.Party?.Match == match,
                healthySnapshotsDuringFault = healthyAfterFault - healthyBefore,
                firstSnapshotAfterEndSeconds = firstSnapshotAfterEnd, firstFreshSnapshotAfterEndSeconds = firstFreshAfterEnd,
                firstMovementAfterEndSeconds = firstMovementAfterEnd, maxVisibleSnapshotGapSeconds = maxGap,
                heldMovementFromHalfToOneSecond = heldXAtOneSecond - heldXAtHalfSecond,
                connectionFeedbackSamples = feedbackSamples, noticeSamples = notices, misleadingReconnectSamples,
                terminalAfterFaultStartSeconds = terminalAt,
                feedbackClearedAfterEndSeconds = feedbackClearedAfterEnd,
                connectionLogPath = affected.GetType().GetProperty("ConnectionLogPath")?.GetValue(affected),
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

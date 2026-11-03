using System.Diagnostics;
using System.Text.Json;
using BlastCircuit.InputDevices;
using BlastCircuit.Testing.Fixtures;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Godot;

namespace BlastCircuit.Testing;

/// <summary>Rendered feedback through real sessions and an owned loopback fault path.</summary>
public partial class PlaytestDriver
{
    private async Task NetworkInterruption()
    {
        await using var scope = new NetworkJourneyScope((phase, state, seconds) =>
            Event("network-phase", new { phase, state, journeySeconds = seconds, engineSeconds = _clock.Elapsed.TotalSeconds }));
        scope.Phase("scenario-enter");
        var origin = new Uri(_server);
        Check(origin.IsLoopback && origin.Scheme == "http", "fault journey refuses external servers");
        scope.Phase("resize-start");
        await ResizeWindow(new Vector2I(1024, 640));
        scope.Phase("resize-complete");
        await using var proxyLease = scope.Own("fault-proxy", new NetworkFaultProxy(origin));
        var proxy = proxyLease.Resource;
        var settings = new SessionSettings(Bots: 0, RoundSeconds: 300, SuddenDeath: false)
            { Hosting = "server", Advanced = new AdvancedRules { BlockPercent = 0, DropPercent = 0 } };
        await using var hostLease = scope.Own("keyboard-host", await NetworkSession.ConnectAsync(origin, "HUD control A", settings, NetworkMode.CreatePrivate));
        var host = hostLease.Resource;
        await using var controlLease = scope.Own("keyboard-peer", await NetworkSession.ConnectAsync(origin, "HUD control B", settings, NetworkMode.JoinCode, host.JoinCode));
        var control = controlLease.Resource;
        int controlSnapshots = 0;
        var controlAuthorityTimes = new long[2];
        async Task Wait(Func<bool> condition, string label, double seconds = 15)
        {
            scope.Phase("wait-start: " + label);
            var timer = Stopwatch.StartNew();
            bool ready = condition();
            while (!ready && timer.Elapsed.TotalSeconds < seconds)
            {
                var controls = new[] { host, control };
                for (int index = 0; index < controls.Length; index++)
                {
                    var peer = controls[index];
                    peer.Advance(1d / 60);
                    foreach (var frame in peer.DrainMessages())
                        if (frame.GetProperty("t").GetString() == "state")
                        { controlSnapshots++; controlAuthorityTimes[index] = frame.GetProperty("d").GetProperty("serverTime").GetInt64(); }
                }
                await Frames(1); await Task.Delay(10);
                ready = condition();
            }
            // A protocol observation is consumed once. Re-evaluating a
            // successful new-sample predicate would reject the same sample.
            Check(ready, label);
            scope.Phase("wait-complete: " + label);
        }
        Label Footer() => _root.FindChild("ConnectionStatus", true, false) as Label
            ?? throw new InvalidOperationException("Connection footer missing");
        bool Notice(string text) => Footer().IsVisibleInTree() && Footer().Text.Contains(text, StringComparison.Ordinal);
        bool ConnectionLostRendered() => _root.ScreenName == "Results" && _root.FindChildren("*", "Label", true, false)
            .OfType<Label>().Any(label => label.IsVisibleInTree() && label.Text == "Connection lost");
        var observations = new List<object>();
        async Task Shot(string name, string expectedText)
        {
            scope.Phase("capture-start: " + name);
            var footer = Footer();
            Check(footer.Text.StartsWith(expectedText, StringComparison.Ordinal), "rendered connection state is ready for capture: " + name);
            Check(footer.GetGlobalRect().Size.X >= footer.GetMinimumSize().X, "connection feedback fits the minimum window: " + name);
            await Capture(name, 0);
            Check(footer.Text.StartsWith(expectedText, StringComparison.Ordinal), "connection capture retains its expected rendered state: " + name);
            observations.Add(new { name, text = footer.Text, rectangle = footer.GetGlobalRect().ToString(),
                platform = OS.GetName(), frame = Engine.GetProcessFrames(), drawn = Engine.GetFramesDrawn(),
                status = _root.ActiveSession?.Status, snapshots = _root.SnapshotCount });
            scope.Phase("capture-complete: " + name);
        }
        try
        {
            await _root.StartOnlineAsync(proxy.Origin, "HUD affected", settings, NetworkMode.JoinCode, host.JoinCode);
            var affected = (NetworkSession)_root.ActiveSession!;
            await Wait(() => host.Party?.CanStart == true, "three real peers are ready");
            host.RequestStart();
            await Wait(() => _root.ScreenName == "Match" && _root.SnapshotCount > 0, "real authority enters the rendered battle");
            // First arena draw may compile renderer pipelines. Record that
            // separately from sampling several authoritative states; proxy I/O
            // remains off this dispatcher throughout both phases.
            int firstDraw = Engine.GetFramesDrawn();
            await Frames(6);
            Check(Engine.GetFramesDrawn() > firstDraw, "arena warmup produces real rendered frames");
            await Wait(() => _root.SnapshotCount > 5, "rendered battle samples fresh authority after warmup");
            Check(affected.ConnectionGeneration == 1 && affected.ConnectionNotice is null, "owned transport is healthy before the first injected fault");
            string lobby = affected.LobbyId!;
            int match = affected.Party?.Match ?? throw new InvalidOperationException("Missing match identity");
            await Shot("network-healthy-keyboard", "Connected");
            int drawn = Engine.GetFramesDrawn(); var controlTimesBefore = controlAuthorityTimes.ToArray();
            proxy.Pause(TimeSpan.FromSeconds(10), upstream: true, downstream: true);
            await Wait(() => Notice("Connection interrupted"), "stale state shows interruption, without claiming a retry", 3);
            Check(affected.Status == "Connected", "interruption precedes an actual reconnect attempt");
            await Shot("network-interrupted-keyboard", "Connection interrupted");
            await Wait(() => Notice("Reconnecting"), "only an actual reconnect attempt says Reconnecting", 6);
            Check(affected.Status.StartsWith("Reconnecting", StringComparison.Ordinal), "rendered retry count comes from the real session");
            await Shot("network-reconnecting-keyboard", "Reconnecting");
            await Wait(() => affected.Status == "Connected" && affected.ConnectionNotice is null
                && Footer().Text.StartsWith("Connected", StringComparison.Ordinal), "same match recovers and the rendered warning clears", 25);
            Check(affected.LobbyId == lobby && affected.Party?.Match == match && _root.ScreenName == "Match", "recovered player stays in the original battle");
            Check(Engine.GetFramesDrawn() > drawn && Enumerable.Range(0, 2).All(index => controlAuthorityTimes[index] - controlTimesBefore[index] >= 5000),
                "rendering and healthy peers continue during the outage");
            Event("healthy-control-progress", new { controlTimesBefore, controlAuthorityTimes, renderedDrainCount = controlSnapshots });
            await Shot("network-recovered-keyboard", "Connected");
            (long Time, long Ack) Progress()
            {
                var state = _root.LatestState ?? throw new InvalidOperationException("Missing authority state");
                var player = state.GetProperty("players").EnumerateArray().Single(x => x.GetProperty("id").GetString() == affected.PlayerId);
                return (state.GetProperty("serverTime").GetInt64(), player.GetProperty("ack").GetInt64());
            }
            var initial = Progress();
            int outgoingGeneration = affected.ConnectionGeneration;
            long SentSequence() => affected.LatestInputSend is { } send && send.Generation == outgoingGeneration ? send.SentSequence : 0;
            var direction = new NetworkDirectionWitness(initial.Time, initial.Ack, SentSequence());
            Event("outgoing-fault-start", new { initial.Time, initial.Ack, sentSequence = SentSequence(), generation = outgoingGeneration, snapshots = _root.SnapshotCount });
            proxy.Pause(TimeSpan.FromSeconds(3), upstream: true, downstream: false);
            await Wait(() =>
            {
                var sample = Progress(); bool advanced = direction.Observe(sample.Time, sample.Ack, SentSequence());
                return advanced && Notice("Connection interrupted") && direction.IncomingWhileAcknowledgmentsStall;
            }, "outgoing-only ACK loss is visible while incoming authority advances", 3);
            var observed = Progress();
            Event("outgoing-fault-witness", new { direction.AuthorityAdvanceMilliseconds, direction.UnchangedAcknowledgmentMilliseconds, direction.SentSequenceAdvance,
                snapshots = _root.SnapshotCount, observation = new { observed.Time, observed.Ack, sentSequence = SentSequence(), generation = affected.ConnectionGeneration } });
            Check(direction.IncomingWhileAcknowledgmentsStall, "ACK-loss warning appears while incoming authority continues");
            Check(affected.Status == "Connected" && affected.ConnectionGeneration == outgoingGeneration, "outgoing-only loss does not claim an actual reconnect");
            await Shot("network-outgoing-only", "Connection interrupted");
            await Wait(() => affected.ConnectionNotice is null, "outgoing ACK progress clears feedback", 15);
            proxy.Pause(TimeSpan.FromSeconds(45), upstream: true, downstream: true);
            await Wait(() => affected.IsEnded && ConnectionLostRendered(), "long dead path has a bounded rendered terminal menu", 36);
            Check(affected.Status.Contains("Connection lost", StringComparison.Ordinal), "terminal result uses the standard connection-loss message");
            await Capture("network-terminal-keyboard", 0);
            Check(ConnectionLostRendered(), "keyboard terminal capture shows the real Connection lost title");
            await KeyPress(Key.Escape);
            Check(_root.ScreenName == "Online" && _root.ActiveSession is null, "keyboard Back releases the dead session and returns to the Online menu");
            await Capture("network-menu-keyboard", 0);
            proxy.ClearFaults();
            // A second real terminal session exercises the controller Back path,
            // using the established canonical-event fixture, not a physical-device claim.
            ControllerHub.Current.OverrideDevicesForPlaytest(QaPads);
            await using var controllerHostLease = scope.Own("controller-host", await NetworkSession.ConnectAsync(origin, "Controller control A", settings, NetworkMode.CreatePrivate));
            var controllerHost = controllerHostLease.Resource;
            await using var controllerPeerLease = scope.Own("controller-peer", await NetworkSession.ConnectAsync(origin, "Controller control B", settings, NetworkMode.JoinCode, controllerHost.JoinCode));
            await _root.StartOnlineAsync(proxy.Origin, "HUD controller", settings, NetworkMode.JoinCode, controllerHost.JoinCode);
            var controllerSession = (NetworkSession)_root.ActiveSession!;
            await Wait(() => controllerHost.Party?.CanStart == true, "controller cohort is ready");
            controllerHost.RequestStart();
            await Wait(() => _root.ScreenName == "Match", "controller session enters its owned battle");
            // Register an actual canonical event before labeling the capture
            // controller-mode. Device inventory alone does not select prompts.
            CtlButton(12, JoyButton.RightShoulder, true); await Frames(1);
            CtlButton(12, JoyButton.RightShoulder, false); await Frames(3);
            Check(ControllerHub.Current.ActiveFamily == ControllerFamily.Xbox, "canonical controller events select the real controller prompts");
            await Shot("network-healthy-controller", "Connected");
            proxy.Pause(TimeSpan.FromSeconds(45), upstream: true, downstream: true);
            await Wait(() => controllerSession.IsEnded && controllerSession.Status.Contains("Connection lost", StringComparison.Ordinal)
                && ConnectionLostRendered(), "controller session also reaches the rendered Connection lost menu", 36);
            await Capture("network-terminal-controller", 0);
            Check(ConnectionLostRendered(), "controller terminal capture shows the real Connection lost title");
            CtlButton(12, JoyButton.B, true); await Frames(1);
            CtlButton(12, JoyButton.B, false); await Frames(4);
            Check(_root.ActiveSession is null && _root.ScreenName == "Online", "controller Back releases the dead session and returns to the Online menu");
            await Capture("network-menu-controller", 0);
            Event("network-input-evidence", new { syntheticCanonicalControllerEvents = true, physicalControllerTested = false });
            scope.Phase("body-complete");
        }
        finally
        {
            scope.BeginCleanup();
            scope.Phase("ui-cleanup-start");
            CtlReleaseAll(); ControllerHub.Current.RestorePhysicalDevicesForPlaytest();
            _root.Back();
            File.WriteAllText(Path.Combine(_output, "network-hud.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
            scope.Phase("ui-cleanup-complete");
        }
    }
}

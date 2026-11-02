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
        var origin = new Uri(_server);
        Check(origin.IsLoopback && origin.Scheme == "http", "fault journey refuses external servers");
        await ResizeWindow(new Vector2I(1024, 640));
        await using var proxy = new NetworkFaultProxy(origin);
        var settings = new SessionSettings(Bots: 0, RoundSeconds: 300, SuddenDeath: false)
            { Hosting = "server", Advanced = new AdvancedRules { BlockPercent = 0, DropPercent = 0 } };
        await using var host = await NetworkSession.ConnectAsync(origin, "HUD control A", settings, NetworkMode.CreatePrivate);
        await using var control = await NetworkSession.ConnectAsync(origin, "HUD control B", settings, NetworkMode.JoinCode, host.JoinCode);
        int controlSnapshots = 0;
        async Task Wait(Func<bool> condition, string label, double seconds = 15)
        {
            var timer = Stopwatch.StartNew();
            while (!condition() && timer.Elapsed.TotalSeconds < seconds)
            {
                foreach (var peer in new[] { host, control })
                {
                    peer.Advance(1d / 60);
                    controlSnapshots += peer.DrainMessages().Count(frame => frame.GetProperty("t").GetString() == "state");
                }
                await Frames(1); await Task.Delay(10);
            }
            Check(condition(), label);
        }
        Label Footer() => _root.FindChild("ConnectionStatus", true, false) as Label
            ?? throw new InvalidOperationException("Connection footer missing");
        bool Notice(string text) => Footer().IsVisibleInTree() && Footer().Text.Contains(text, StringComparison.Ordinal);
        var observations = new List<object>();
        async Task Shot(string name)
        {
            var footer = Footer();
            Check(footer.GetGlobalRect().Size.X >= footer.GetMinimumSize().X, "connection feedback fits the minimum window: " + name);
            observations.Add(new { name, text = footer.Text, rectangle = footer.GetGlobalRect().ToString(),
                platform = OS.GetName(), frame = Engine.GetProcessFrames(), drawn = Engine.GetFramesDrawn(),
                status = _root.ActiveSession?.Status, snapshots = _root.SnapshotCount });
            await Capture(name, 0);
        }
        try
        {
            await _root.StartOnlineAsync(proxy.Origin, "HUD affected", settings, NetworkMode.JoinCode, host.JoinCode);
            var affected = (NetworkSession)_root.ActiveSession!;
            await Wait(() => host.Party?.CanStart == true, "three real peers are ready");
            host.RequestStart();
            await Wait(() => _root.ScreenName == "Match" && _root.SnapshotCount > 5, "real authority enters the rendered battle");
            string lobby = affected.LobbyId!;
            await Shot("network-healthy-keyboard");
            int drawn = Engine.GetFramesDrawn(); int before = controlSnapshots;
            proxy.Pause(TimeSpan.FromSeconds(10), upstream: true, downstream: true);
            await Wait(() => Notice("Connection interrupted"), "stale state shows interruption, without claiming a retry", 3);
            Check(affected.Status == "Connected", "interruption precedes an actual reconnect attempt");
            await Shot("network-interrupted-keyboard");
            await Wait(() => Notice("Reconnecting"), "only an actual reconnect attempt says Reconnecting", 6);
            Check(affected.Status.StartsWith("Reconnecting", StringComparison.Ordinal), "rendered retry count comes from the real session");
            await Shot("network-reconnecting-keyboard");
            await Wait(() => affected.Status == "Connected" && affected.ConnectionNotice is null, "same match recovers and warning clears", 25);
            Check(affected.LobbyId == lobby && _root.ScreenName == "Match", "recovered player stays in the original battle");
            Check(Engine.GetFramesDrawn() > drawn && controlSnapshots > before + 50, "rendering and healthy peers continue during the outage");
            await Shot("network-recovered-keyboard");
            before = _root.SnapshotCount;
            proxy.Pause(TimeSpan.FromSeconds(3), upstream: true, downstream: false);
            await Wait(() => Notice("Connection interrupted"), "outgoing-only ACK loss is visible", 3);
            Check(_root.SnapshotCount > before + 5, "ACK-loss warning appears while incoming authority continues");
            await Shot("network-outgoing-only");
            await Wait(() => affected.ConnectionNotice is null, "outgoing ACK progress clears feedback", 15);
            proxy.Pause(TimeSpan.FromSeconds(45), upstream: true, downstream: true);
            await Wait(() => affected.IsEnded && _root.ScreenName == "Results", "long dead path has a bounded terminal menu", 36);
            Check(affected.Status.Contains("Connection lost", StringComparison.Ordinal), "terminal result uses the standard connection-loss message");
            await Capture("network-terminal-keyboard", 0);
            await KeyPress(Key.Escape);
            Check(_root.ScreenName != "Results" && _root.ActiveSession is null, "keyboard Back releases the dead session and returns to a usable menu");
            await Capture("network-menu-keyboard", 0);
            // A second real terminal session exercises the controller Back path,
            // using the established canonical-event fixture, not a physical-device claim.
            ControllerHub.Current.OverrideDevicesForPlaytest(QaPads);
            await using var controllerHost = await NetworkSession.ConnectAsync(origin, "Controller control A", settings, NetworkMode.CreatePrivate);
            await using var controllerPeer = await NetworkSession.ConnectAsync(origin, "Controller control B", settings, NetworkMode.JoinCode, controllerHost.JoinCode);
            await _root.StartOnlineAsync(proxy.Origin, "HUD controller", settings, NetworkMode.JoinCode, controllerHost.JoinCode);
            await Wait(() => controllerHost.Party?.CanStart == true, "controller cohort is ready");
            controllerHost.RequestStart();
            await Wait(() => _root.ScreenName == "Match", "controller session enters its owned battle");
            await Shot("network-healthy-controller");
            proxy.Pause(TimeSpan.FromSeconds(45), upstream: true, downstream: true);
            await Wait(() => _root.ScreenName == "Results", "controller session also reaches the terminal menu", 36);
            await Capture("network-terminal-controller", 0);
            CtlButton(12, JoyButton.B, true); await Frames(1);
            CtlButton(12, JoyButton.B, false); await Frames(4);
            Check(_root.ActiveSession is null && _root.ScreenName != "Results", "controller Back releases the dead session and returns to a usable menu");
            await Capture("network-menu-controller", 0);
            Event("network-input-evidence", new { syntheticCanonicalControllerEvents = true, physicalControllerTested = false });
        }
        finally
        {
            CtlReleaseAll(); ControllerHub.Current.RestorePhysicalDevicesForPlaytest();
            _root.Back();
            File.WriteAllText(Path.Combine(_output, "network-hud.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

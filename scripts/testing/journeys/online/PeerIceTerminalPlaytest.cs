using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using BlastCircuit.App;
using Bomber.Client.Peer;
using Godot;

namespace BlastCircuit.Testing;

/// <summary>Real native late-ICE boundary, before the owner polls/replaces a terminal adapter.</summary>
public partial class PlaytestDriver
{
    private async Task PeerIceTerminal()
    {
        using var left = new GodotPeerLink(useStun: false);
        using var right = new GodotPeerLink(useStun: false);
        left.SetDiagnosticContext(new("fixture-left", "fixture-right", null, true, 0, 0));
        right.SetDiagnosticContext(new("fixture-right", "fixture-left", null, false, 0, 0));
        var leftIce = new List<JsonElement>();
        var leftSdp = new List<JsonElement>(); var rightSdp = new List<JsonElement>();
        left.Signal += signal => { if (signal.GetProperty("kind").GetString() == "ice") leftIce.Add(signal.Clone()); else leftSdp.Add(signal.Clone()); right.AcceptSignal(signal); };
        right.Signal += signal => { if (signal.GetProperty("kind").GetString() == "sdp") rightSdp.Add(signal.Clone()); left.AcceptSignal(signal); };
        string stage = "connect";
        bool immediateReflectionProbe = OS.GetEnvironment("BLAST_QA_ICE_IMMEDIATE_CLOSE_PROBE") == "true";
        object? closeObservation = null;
        try
        {
            right.Start(false); left.Start(true);
            var clock = Stopwatch.StartNew();
            while (!(left.Connected && right.Connected && leftIce.Count > 0) && clock.Elapsed.TotalSeconds < 5)
            { left.Poll(); right.Poll(); await Frames(1); await Task.Delay(5); }
            Check(left.Connected && right.Connected && leftIce.Count > 0, "real native pair connects before terminal-close injection");
            stage = "native-close";
            var native = (WebRtcPeerConnection)(typeof(GodotPeerLink).GetField("_peer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(right)
                ?? throw new InvalidOperationException("Missing native peer"));
            native.Close();
            var terminalWait = Stopwatch.StartNew();
            while (!immediateReflectionProbe && !right.Failed && terminalWait.Elapsed.TotalSeconds < 1)
            { right.Poll(); await Frames(1); await Task.Delay(5); }
            WebRtcDataChannel Channel(string field) => (WebRtcDataChannel)typeof(GodotPeerLink).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(right)!;
            closeObservation = new { connection = native.GetConnectionState().ToString(), signaling = native.GetSignalingState().ToString(),
                gathering = native.GetGatheringState().ToString(), reliable = Channel("_channel").GetReadyState().ToString(), snapshots = Channel("_states").GetReadyState().ToString(),
                adapterConnected = right.Connected, adapterFailed = right.Failed, observation = right.SafeSignalDiagnostics };
            if (!immediateReflectionProbe) Check(right.Failed, "native terminal state becomes observable before owner recovery");
            stage = "delayed-ice";
            right.AcceptSignal(leftIce[0]);
            var observation = JsonSerializer.SerializeToElement(right.SignalDiagnostics);
            Check(observation.GetProperty("failedIce").GetInt32() == 0,
                "delayed valid ICE after native terminal close must not call a destroyed native transport");
            Check(!observation.GetProperty("remoteDescriptionSet").GetBoolean() && observation.GetProperty("pendingIce").GetInt32() == 0,
                "observed terminal state clears SDP readiness and pending ICE");
            stage = "late-sdp";
            int starts = observation.GetProperty("nativeStarts").GetInt32();
            foreach (var sdp in leftSdp) right.AcceptSignal(sdp);
            right.Start(false);
            Check(right.Failed && !right.Connected && JsonSerializer.SerializeToElement(right.SignalDiagnostics).GetProperty("nativeStarts").GetInt32() == starts,
                "late offer and repeated Start cannot revive an observed-terminal adapter");

            stage = "fresh-replacement";
            using var freshLeft = new GodotPeerLink(useStun: false);
            using var freshRight = new GodotPeerLink(useStun: false);
            freshLeft.SetDiagnosticContext(new("fixture-left", "fixture-right", null, true, 1, 1));
            freshRight.SetDiagnosticContext(new("fixture-right", "fixture-left", null, false, 1, 1));
            var freshOffer = new List<JsonElement>(); var freshAnswer = new List<JsonElement>();
            freshLeft.Signal += signal => { if (signal.GetProperty("kind").GetString() == "sdp") freshOffer.Add(signal.Clone()); freshRight.AcceptSignal(signal); };
            freshRight.Signal += signal => { if (signal.GetProperty("kind").GetString() == "sdp") freshAnswer.Add(signal.Clone()); freshLeft.AcceptSignal(signal); };
            freshRight.Start(false); freshLeft.Start(true);
            clock.Restart();
            while (!(freshLeft.Connected && freshRight.Connected) && clock.Elapsed.TotalSeconds < 5)
            { freshLeft.Poll(); freshRight.Poll(); await Frames(1); await Task.Delay(5); }
            Check(freshLeft.Connected && freshRight.Connected, "fresh replacement adapters negotiate real native channels");
            foreach (var sdp in freshOffer) right.AcceptSignal(sdp);
            Check(right.Failed && !right.Connected, "even a different valid offer cannot resurrect the retired native object");
            bool delivered = false; freshRight.Received += packet => delivered |= packet.AsSpan().SequenceEqual("fresh-native-replacement"u8);
            Check(freshLeft.Send("fresh-native-replacement"u8.ToArray()), "fresh replacement accepts an actual reliable packet");
            clock.Restart();
            while (!delivered && clock.Elapsed.TotalSeconds < 5)
            { freshLeft.Poll(); freshRight.Poll(); await Frames(1); await Task.Delay(5); }
            Check(delivered, "fresh replacement delivers an actual native packet");

            stage = "adapter-owned-dispose";
            freshLeft.Dispose();
            foreach (var sdp in freshAnswer) freshLeft.AcceptSignal(sdp);
            foreach (var ice in leftIce) freshLeft.AcceptSignal(ice);
            Check(!freshLeft.Connected && typeof(GodotPeerLink).GetField("_peer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(freshLeft) is null,
                "actual adapter-owned disposal rejects late SDP/ICE without retaining a native object");
            stage = "complete";
        }
        finally
        {
            File.WriteAllText(Path.Combine(_output, "peer-ice-terminal.json"), JsonSerializer.Serialize(new
                { stage, closeObservation, immediateReflectionProbe,
                    scope = "Isolated in-process signalling/real native UDP; no room epoch. Generation labels are controlled fixture identities.",
                    injection = "Reflection QA closes native resource, bypassing adapter-owned Dispose; actual Dispose path is a separate control", left = left.SafeSignalDiagnostics, right = right.SafeSignalDiagnostics },
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

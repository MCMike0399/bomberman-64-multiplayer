using System.Diagnostics;
using System.Text.Json;
using BlastCircuit.App;

namespace BlastCircuit.Testing;

/// <summary>Native transport regression: trickled candidates may arrive before their remote SDP.</summary>
public partial class PlaytestDriver
{
    private async Task PeerIceOrder()
    {
        using var left = new GodotPeerLink(useStun: false);
        using var right = new GodotPeerLink(useStun: false);
        var leftSignals = new List<JsonElement>(); var rightSignals = new List<JsonElement>();
        left.Signal += signal => leftSignals.Add(signal.Clone());
        right.Signal += signal => rightSignals.Add(signal.Clone());
        left.Start(true); right.Start(false);
        await Until(() => leftSignals.Any(Ice) && leftSignals.Any(Sdp), "native offer and candidates are produced");
        foreach (var signal in leftSignals.Where(Ice)) right.AcceptSignal(signal);
        foreach (var signal in leftSignals.Where(Sdp)) right.AcceptSignal(signal);
        await Until(() => rightSignals.Any(Ice) && rightSignals.Any(Sdp), "native answer and candidates are produced");
        foreach (var signal in rightSignals.Where(Ice)) left.AcceptSignal(signal);
        foreach (var signal in rightSignals.Where(Sdp)) left.AcceptSignal(signal);
        await Until(() => left.Connected && right.Connected, "native peers connect when ICE arrives before SDP");
        bool received = false;
        right.Received += packet => received |= packet.AsSpan().SequenceEqual("ordered-ice-proof"u8);
        Check(left.Send("ordered-ice-proof"u8.ToArray()), "reordered native link accepts an actual data packet");
        await Until(() => received, "actual native data traverses the reordered signalling link");

        static bool Ice(JsonElement signal) => signal.GetProperty("kind").GetString() == "ice";
        static bool Sdp(JsonElement signal) => signal.GetProperty("kind").GetString() == "sdp";
        async Task Until(Func<bool> condition, string label)
        {
            var clock = Stopwatch.StartNew();
            while (!condition() && clock.Elapsed.TotalSeconds < 5)
            { left.Poll(); right.Poll(); await Frames(1); await Task.Delay(5); }
            Check(condition(), label);
        }
    }
}

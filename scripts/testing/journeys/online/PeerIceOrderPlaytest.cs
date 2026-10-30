using System.Diagnostics;
using System.Text.Json;
using BlastCircuit.App;

namespace BlastCircuit.Testing;

/// <summary>Native transport regression: all real trickled candidates precede remote SDP.</summary>
public partial class PlaytestDriver
{
    private async Task PeerIceOrder()
    {
        using var left = new GodotPeerLink(useStun: false);
        using var right = new GodotPeerLink(useStun: false);
        var leftSignals = new List<JsonElement>(); var rightSignals = new List<JsonElement>();
        left.Signal += signal => leftSignals.Add(signal.Clone());
        right.Signal += signal => rightSignals.Add(signal.Clone());
        int leftDelivered=0,rightDelivered=0;string stage="offer";
        try
        {
            left.Start(true);right.Start(false);
            await Until(()=>left.IceGatheringComplete&&leftSignals.Any(Ice)&&leftSignals.Any(Sdp),"native offer and complete host candidates are produced");
            left.Poll();right.Poll(); // Drain callbacks already queued before gathering completed.
            Check(CandidateFreeSdp(leftSignals),"offer SDP cannot bypass the early-ICE proof with embedded candidates");
            foreach(var signal in leftSignals.Where(Ice))right.AcceptSignal(signal);
            foreach(var signal in leftSignals.Where(Sdp))right.AcceptSignal(signal);
            leftDelivered=leftSignals.Count;stage="answer";
            await Until(()=>right.IceGatheringComplete&&rightSignals.Any(Ice)&&rightSignals.Any(Sdp),"native answer and complete host candidates are produced");
            left.Poll();right.Poll();
            Check(CandidateFreeSdp(rightSignals),"answer SDP cannot bypass the early-ICE proof with embedded candidates");
            foreach(var signal in rightSignals.Where(Ice))left.AcceptSignal(signal);
            foreach(var signal in rightSignals.Where(Sdp))left.AcceptSignal(signal);
            rightDelivered=rightSignals.Count;stage="connect";
            await Until(()=>left.Connected&&right.Connected,"native peers connect when ICE arrives before SDP");
            Check(leftSignals.Count==leftDelivered&&rightSignals.Count==rightDelivered,"no later signal substitutes for the exclusively early-ICE proof");
            bool received=false;
            right.Received+=packet=>received|=packet.AsSpan().SequenceEqual("ordered-ice-proof"u8);
            Check(left.Send("ordered-ice-proof"u8.ToArray()),"reordered native link accepts an actual data packet");
            stage="packet";
            await Until(()=>received,"actual native data traverses the reordered signalling link");stage="complete";
        }
        finally
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output,"peer-ice-order-diagnostics.json"),
                JsonSerializer.Serialize(Diagnostics(),new JsonSerializerOptions{WriteIndented=true}));
        }
        static bool Ice(JsonElement signal)=>signal.GetProperty("kind").GetString()=="ice";
        static bool Sdp(JsonElement signal)=>signal.GetProperty("kind").GetString()=="sdp";
        static bool CandidateFreeSdp(List<JsonElement> signals)=>signals.Where(Sdp).All(s=>!s.GetProperty("sdp").GetString()!.Contains("a=candidate:",StringComparison.Ordinal));
        object Diagnostics()=>new{stage,leftSignals=leftSignals.Count,rightSignals=rightSignals.Count,leftDelivered,rightDelivered,
            leftIce=leftSignals.Count(Ice),rightIce=rightSignals.Count(Ice),leftGathered=left.IceGatheringComplete,rightGathered=right.IceGatheringComplete,
            leftConnected=left.Connected,leftFailed=left.Failed,rightConnected=right.Connected,rightFailed=right.Failed,
            leftObservation=left.SignalDiagnostics,rightObservation=right.SignalDiagnostics};
        async Task Until(Func<bool> condition,string label)
        {
            var clock=Stopwatch.StartNew();
            while(!condition()&&clock.Elapsed.TotalSeconds<5)
            {left.Poll();right.Poll();await Frames(1);await Task.Delay(5);}
            Check(condition(),label+"; "+JsonSerializer.Serialize(Diagnostics()));
        }
    }
}

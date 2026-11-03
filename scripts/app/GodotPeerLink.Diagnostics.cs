using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace BlastCircuit.App;

public sealed partial class GodotPeerLink
{
    internal static bool CaptureSignalDiagnostics{get;set;}
    private static int _nextDiagnosticOrdinal;
    private readonly bool _observe=CaptureSignalDiagnostics;
    private readonly long _createdTick=CaptureSignalDiagnostics?Stopwatch.GetTimestamp():0;
    private readonly long? _createdUtc=CaptureSignalDiagnostics?DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():null;
    private readonly int _diagnosticOrdinal=CaptureSignalDiagnostics?Interlocked.Increment(ref _nextDiagnosticOrdinal):0;
    private readonly List<object>? _candidateEvents=CaptureSignalDiagnostics?new():null;
    private readonly List<object>? _sdpEvents=CaptureSignalDiagnostics?new():null;
    private int _sdpDrops;
    private readonly List<object>? _stateEvents=CaptureSignalDiagnostics?new():null;
    private int _localSdpCallbacks,_localSdpErrors,_remoteSdpReceived,_remoteSdpErrors,_duplicateOffers;
    private int _starts,_localSdp,_localIce,_remoteSdp,_remoteIce,_earlyIce,_appliedIce,_failedIce,_candidateDrops,_stateDrops;
    private bool _observedStun;
    private long _nativeStartTick,_firstLocalCandidateTick,_remoteDescriptionTick,_firstRemoteAppliedTick,_lastStateTick,_lastPollTick;
    private double _maxPollGapMs;
    private (WebRtcPeerConnection.ConnectionState? Connection,WebRtcPeerConnection.GatheringState? Gathering,
        WebRtcPeerConnection.SignalingState? Signaling,WebRtcDataChannel.ChannelState? Reliable,
        WebRtcDataChannel.ChannelState? Snapshots) _observedState;
    private double? At(long tick)=>tick==0?null:(tick-_createdTick)*1000d/Stopwatch.Frequency;
    internal object SignalDiagnostics=>new
    {
        enabled=_observe,ordinalScope="QA-observed managed adapters; not all native allocations.",processId=System.Environment.ProcessId,linkOrdinal=_diagnosticOrdinal,createdUtcUnixMs=_createdUtc,
        nativeStarts=_starts,nativeStartAfterCreationMs=At(_nativeStartTick),ageMs=_observe?At(Stopwatch.GetTimestamp()):null,
        useStun=_observedStun,localSdpCallbacks=_localSdpCallbacks,localSdpSignals=_localSdp,localSdpReturnErrors=_localSdpErrors,
        localIce=_localIce,remoteSdpReceived=_remoteSdpReceived,remoteSdpApplied=_remoteSdp,remoteSdpApplyFailures=_remoteSdpErrors,
        duplicateOffersIgnored=_duplicateOffers,remoteIce=_remoteIce,
        earlyIce=_earlyIce,appliedIce=_appliedIce,failedIce=_failedIce,pendingIce=_pendingIce.Count,
        remoteDescriptionSet=_remoteDescriptionSet,firstLocalCandidateObservedMs=At(_firstLocalCandidateTick),
        terminal=_terminal,terminalSignalsDropped=_terminalSignalsDropped,context=_diagnosticContext,
        nativeState=NativeState(),
        firstRemoteDescriptionAppliedMs=At(_remoteDescriptionTick),firstRemoteCandidateAcceptedMs=At(_firstRemoteAppliedTick),
        lastStateChangeObservedMs=At(_lastStateTick),lastPollMs=At(_lastPollTick),maxPollGapMs=_maxPollGapMs,
        candidates=_candidateEvents?.ToArray()??[],candidateEventsDropped=_candidateDrops,sdpEvents=_sdpEvents?.ToArray()??[],sdpEventsDropped=_sdpDrops,
        states=_stateEvents?.ToArray()??[],stateEventsDropped=_stateDrops,
        endpointScope="Signalled endpoints and API acceptance only; selected ICE pair and native transition instants are not observed."
    };
    private void ObserveStart(bool stun)
    {if(_observe){_starts++;_observedStun=stun;_nativeStartTick=Stopwatch.GetTimestamp();}}
    private void ObserveLocalDescriptionCallback(){if(_observe)_localSdpCallbacks++;}
    private static string Fingerprint(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private void ObserveSdp(bool local,string type,string sdp,string? result,long at,int? sourceStart=null,ulong? sourceInstance=null)
    {
        if(_sdpEvents!.Count==16){_sdpDrops++;return;}
        _sdpEvents.Add(new{local,type,nativeStart=_starts,sourceStart,sourceInstance,afterCreationMs=At(at),sha256=Fingerprint(sdp),result});
    }
    private void ObserveLocalDescription(string type,string sdp,Error result,int sourceStart,ulong sourceInstance)
    {
        if(!_observe)return;
        long now=Stopwatch.GetTimestamp();if(result==Error.Ok)_localSdp++;else _localSdpErrors++;
        ObserveSdp(true,type,sdp,result.ToString(),now,sourceStart,sourceInstance);
    }
    private void ObserveRemoteDescriptionReceived(){if(_observe)_remoteSdpReceived++;}
    private void ObserveDuplicateOffer(string sdp)
    {if(_observe){_duplicateOffers++;ObserveSdp(false,"offer",sdp,"duplicate-ignored",Stopwatch.GetTimestamp());}}
    private void ObserveRemoteDescriptionResult(string type,string sdp,Error? result)
    {
        if(!_observe)return;
        long now=Stopwatch.GetTimestamp();
        if(result==Error.Ok){_remoteSdp++;if(_remoteDescriptionTick==0)_remoteDescriptionTick=now;}else _remoteSdpErrors++;
        ObserveSdp(false,type,sdp,result?.ToString(),now);
        if (result is not null && result != Error.Ok) RecordLifecycle("failed-remote-sdp", new { type, sdpSha256 = Fingerprint(sdp), result = result.ToString() }, failed: true);
    }
    private void ObserveCandidate(string action,bool local,string media,long index,string candidate,Error? result=null,int? sourceStart=null,ulong? sourceInstance=null)
    {
        if(!_observe)return;
        long now=Stopwatch.GetTimestamp();
        if(action=="produced"){_localIce++;if(_firstLocalCandidateTick==0)_firstLocalCandidateTick=now;}
        if(action=="received"){_remoteIce++;if(!_remoteDescriptionSet)_earlyIce++;}
        if(action=="applied")
        {
            if(result==Error.Ok){_appliedIce++;if(_firstRemoteAppliedTick==0)_firstRemoteAppliedTick=now;}
            else _failedIce++;
        }
        if(_candidateEvents!.Count==128){_candidateDrops++;return;}
        var fields=candidate.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        int? port=fields.Length>5&&int.TryParse(fields[5],out int parsed)&&parsed is >0 and <=65535?parsed:null;
        _candidateEvents.Add(new{action,local,nativeStart=_starts,sourceStart,sourceInstance,afterCreationMs=At(now),media,index,
            sha256=Fingerprint(candidate),foundation=fields.Length>0?fields[0]:null,component=fields.Length>1?fields[1]:null,transport=fields.Length>2?fields[2]:null,
            address=fields.Length>4?fields[4]:null,port,type=fields.Length>7?fields[7]:null,result=result?.ToString()});
    }
    private void ObservePoll()
    {
        if(!_observe)return;
        long now=Stopwatch.GetTimestamp(),previous=_lastPollTick==0?_createdTick:_lastPollTick;
        _maxPollGapMs=Math.Max(_maxPollGapMs,(now-previous)*1000d/Stopwatch.Frequency);_lastPollTick=now;
    }
    private void ObserveState()
    {
        if(!_observe)return;
        var state=(_peer?.GetConnectionState(),_peer?.GetGatheringState(),_peer?.GetSignalingState(),_channel?.GetReadyState(),_states?.GetReadyState());
        if(state==_observedState)return;
        _observedState=state;_lastStateTick=Stopwatch.GetTimestamp();
        if(_stateEvents!.Count==32){_stateDrops++;return;}
        _stateEvents.Add(new{nativeStart=_starts,observedAfterCreationMs=At(_lastStateTick),connection=state.Item1?.ToString(),gathering=state.Item2?.ToString(),
            signaling=state.Item3?.ToString(),reliable=state.Item4?.ToString(),snapshots=state.Item5?.ToString()});
    }
    private void ApplyIce(string media,int index,string candidate)
    {
        if (ObserveTerminal()) { if (_observe) _terminalSignalsDropped++; return; }
        var checkedConnection = _lastCheckedConnection;
        var result=_peer!.AddIceCandidate(media,index,candidate);
        ObserveCandidate("applied",false,media,index,candidate,result);
        if (result != Error.Ok) RecordLifecycle("failed-ice-apply", new { media, index, candidateSha256 = Fingerprint(candidate),
            connectionAtGuard = checkedConnection?.ToString(), result = result.ToString() }, failed: true);
    }
    private object NativeState() => new { instance = _peer?.GetInstanceId(), connection = _peer?.GetConnectionState().ToString(), signaling = _peer?.GetSignalingState().ToString(),
        gathering = _peer?.GetGatheringState().ToString(), reliable = _channel?.GetReadyState().ToString(), snapshots = _states?.GetReadyState().ToString() };
    private void RecordLifecycle(string reason, object? detail = null, bool failed = false)
    {
        if (!_observe) return;
        var snapshot = JsonSerializer.SerializeToElement(new { reason, utc = DateTimeOffset.UtcNow, detail, observation = SafeSignalDiagnostics });
        if (failed)
        {
            if (FailedApplyDiagnostics.Count < 16) FailedApplyDiagnostics.Add(snapshot);
            else _failedDiagnosticsDropped++;
        }
        else
        {
            if (RetiredDiagnostics.Count == 64) { RetiredDiagnostics.Dequeue(); _retiredDiagnosticsDropped++; }
            RetiredDiagnostics.Enqueue(snapshot);
        }
    }
    internal JsonElement SafeSignalDiagnostics
    {
        get
        {
            var safe = JsonSerializer.SerializeToNode(SignalDiagnostics)!.AsObject();
            foreach (var candidate in safe["candidates"]!.AsArray())
            { candidate!.AsObject().Remove("address"); candidate.AsObject().Remove("port"); }
            safe["endpointScope"] = "Candidate/SDP hashes and order only; addresses and ports omitted from QA receipts.";
            return JsonSerializer.SerializeToElement(safe);
        }
    }
    internal static object LifecycleDiagnostics => new { failed = FailedApplyDiagnostics.ToArray(), failedDropped = _failedDiagnosticsDropped,
        retired = RetiredDiagnostics.ToArray(), retiredDropped = _retiredDiagnosticsDropped,
        scope = "QA-only bounded immutable snapshots; no native resources retained; no candidate addresses/ports." };
}

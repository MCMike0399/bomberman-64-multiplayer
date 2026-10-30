using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    private void ObserveSdp(bool local,string type,string sdp,string? result,long at)
    {
        if(_sdpEvents!.Count==16){_sdpDrops++;return;}
        _sdpEvents.Add(new{local,type,nativeStart=_starts,afterCreationMs=At(at),sha256=Fingerprint(sdp),result});
    }
    private void ObserveLocalDescription(string type,string sdp,Error result)
    {
        if(!_observe)return;
        long now=Stopwatch.GetTimestamp();if(result==Error.Ok)_localSdp++;else _localSdpErrors++;
        ObserveSdp(true,type,sdp,result.ToString(),now);
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
    }
    private void ObserveCandidate(string action,bool local,string media,long index,string candidate,Error? result=null)
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
        _candidateEvents.Add(new{action,local,nativeStart=_starts,afterCreationMs=At(now),media,index,
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
        var result=_peer!.AddIceCandidate(media,index,candidate);
        ObserveCandidate("applied",false,media,index,candidate,result);
    }
}

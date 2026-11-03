using System.Text.Json;
using Bomber.Client.Peer;
using Godot;

namespace BlastCircuit.App;

/// <summary>Official webrtc-native adapter; ICE/DTLS/SCTP remain inside the maintained transport library.</summary>
public sealed partial class GodotPeerLink(bool useStun = true) : IPeerLink
{
    private WebRtcPeerConnection? _peer;
    private WebRtcDataChannel? _channel;
    private WebRtcDataChannel? _states;
    private string? _remoteOffer;
    private bool _remoteDescriptionSet;
    private bool _terminal;
    private int _terminalSignalsDropped;
    private PeerLinkDiagnosticContext? _diagnosticContext;
    private WebRtcPeerConnection.ConnectionState? _lastCheckedConnection;
    private static readonly Queue<JsonElement> RetiredDiagnostics = new();
    private static readonly List<JsonElement> FailedApplyDiagnostics = new();
    private static int _retiredDiagnosticsDropped, _failedDiagnosticsDropped;
    private readonly Queue<(string Media,int Index,string Candidate)> _pendingIce=[];
    internal bool IceGatheringComplete => _peer?.GetGatheringState() == WebRtcPeerConnection.GatheringState.Complete;
    public bool Failed => ObserveTerminal();
    public bool CaptureDiagnostics => _observe;
    public void SetDiagnosticContext(PeerLinkDiagnosticContext context) { if (_observe) _diagnosticContext = context; }
    public bool Connected => !_terminal && _peer?.GetConnectionState() == WebRtcPeerConnection.ConnectionState.Connected &&
        _channel?.GetReadyState() == WebRtcDataChannel.ChannelState.Open && _states?.GetReadyState() == WebRtcDataChannel.ChannelState.Open;
    public event Action<JsonElement>? Signal;
    public event Action<byte[]>? Received;
    public bool DisposableSnapshots => _states is not null && !_states.IsOrdered() && _states.GetMaxRetransmits() == 0;
    public void Start(bool offer)
    {
        if (_terminal) return; // Owner recovery must create a fresh adapter.
        ObserveStart(useStun);
        _peer = new WebRtcPeerConnection();
        // STUN discovers public candidates. If UDP is blocked, the authenticated cluster relay remains available.
        if (_peer.Initialize(new Godot.Collections.Dictionary
        {
            ["iceServers"] = useStun ? new Godot.Collections.Array { new Godot.Collections.Dictionary
                { ["urls"] = new Godot.Collections.Array { "stun:stun.cloudflare.com:3478" } } } : new Godot.Collections.Array()
        }) != Error.Ok) { Dispose(); return; }
        int sourceStart = _starts;
        ulong sourceInstance = _observe ? _peer.GetInstanceId() : 0;
        _peer.SessionDescriptionCreated += (type, sdp) =>
        {
            ObserveLocalDescriptionCallback();var result=_peer.SetLocalDescription(type,sdp);ObserveLocalDescription(type,sdp,result,sourceStart,sourceInstance);
            if(result==Error.Ok)Signal?.Invoke(JsonSerializer.SerializeToElement(new{kind="sdp",type,sdp}));
        };
        _peer.IceCandidateCreated += (media, index, candidate) =>
        {ObserveCandidate("produced",true,media,index,candidate,sourceStart:sourceStart,sourceInstance:sourceInstance);Signal?.Invoke(JsonSerializer.SerializeToElement(new { kind = "ice", media, index, candidate }));};
        _channel = _peer.CreateDataChannel("blast-v1", new Godot.Collections.Dictionary { ["negotiated"] = true, ["id"] = 1, ["ordered"] = true });
        _states = _peer.CreateDataChannel("blast-state-v1", new Godot.Collections.Dictionary
        { ["negotiated"] = true, ["id"] = 2, ["ordered"] = false, ["maxRetransmits"] = 0 });
        if (offer) _peer.CreateOffer();
        ObserveState();
    }
    public void AcceptSignal(JsonElement signal)
    {
        if (_peer is null) return;
        if (ObserveTerminal()) { if (_observe) _terminalSignalsDropped++; return; }
        try
        {
            if (signal.GetProperty("kind").GetString() == "sdp")
            {
                ObserveRemoteDescriptionReceived();
                string type = signal.GetProperty("type").GetString()!;
                string sdp = signal.GetProperty("sdp").GetString()!;
                if (type == "offer")
                {
                    if (_remoteOffer == sdp) {ObserveDuplicateOffer(sdp);return;}
                    if (_remoteOffer is not null) { Dispose(); Start(false); }
                    _remoteOffer = sdp;
                }
                var result=type is "offer" or "answer"?_peer?.SetRemoteDescription(type,sdp):null;
                ObserveRemoteDescriptionResult(type,sdp,result);
                if(result==Error.Ok)
                {
                    if (ObserveTerminal()) return;
                    _remoteDescriptionSet=true;
                    while(_pendingIce.TryDequeue(out var pending))ApplyIce(pending.Media,pending.Index,pending.Candidate);
                }
            }
            else if (signal.GetProperty("kind").GetString() == "ice")
            {
                string? media=signal.GetProperty("media").GetString();
                int index=signal.GetProperty("index").GetInt32();
                string? candidate=signal.GetProperty("candidate").GetString();
                if(media is null||candidate is null||media.Length>256||candidate.Length>4096)return;
                ObserveCandidate("received",false,media,index,candidate);
                if(_remoteDescriptionSet)ApplyIce(media,index,candidate);
                else if(_pendingIce.Count<128)_pendingIce.Enqueue((media,index,candidate));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException) { }
    }
    public void Poll()
    {
        ObservePoll();
        if (ObserveTerminal()) return;
        _peer?.Poll();
        if (ObserveTerminal()) return;
        foreach (var channel in new[] { _channel, _states })
        {
            if (channel is null) continue;
            channel.Poll();
            for (int count = 0; count < 64 && channel.GetAvailablePacketCount() > 0; count++)
            {
                byte[] packet = channel.GetPacket();
                if (packet.Length <= 131072) Received?.Invoke(packet);
            }
        }
        ObserveState();
    }
    public bool Send(byte[] packet) => packet.Length <= PeerPacketCodec.MaximumWireBytes && Connected && _channel!.GetBufferedAmount() < 262144 && _channel.PutPacket(packet) == Error.Ok;
    public bool SendState(byte[] packet) => packet.Length <= PeerPacketCodec.MaximumWireBytes && Connected && _states!.GetBufferedAmount() < 131072 && _states.PutPacket(packet) == Error.Ok;
    public void Dispose()
    {
        if (_peer is not null) RecordLifecycle("retired");
        _channel?.Close(); _channel?.Dispose(); _channel = null;
        _states?.Close(); _states?.Dispose(); _states = null;
        _peer?.Close(); _peer?.Dispose(); _peer = null;
        _remoteOffer = null;
        _remoteDescriptionSet=false;_pendingIce.Clear();
    }
    private bool ObserveTerminal()
    {
        if (_terminal) return true;
        var connection = _peer?.GetConnectionState();
        if (_observe) _lastCheckedConnection = connection;
        if (connection is not (WebRtcPeerConnection.ConnectionState.Failed or WebRtcPeerConnection.ConnectionState.Closed)) return false;
        bool ready = _remoteDescriptionSet; int pending = _pendingIce.Count;
        _terminal = true; _remoteDescriptionSet = false; _pendingIce.Clear();
        ObserveState(); RecordLifecycle("terminal-observed", new { remoteDescriptionWasReady = ready, pendingIceCleared = pending });
        return true;
    }
}

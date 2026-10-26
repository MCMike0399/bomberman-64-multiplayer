using System.Text.Json;
using Bomber.Client.Peer;
using Godot;

namespace BlastCircuit.App;

/// <summary>Official webrtc-native adapter; ICE/DTLS/SCTP remain inside the maintained transport library.</summary>
public sealed class GodotPeerLink(bool useStun = true) : IPeerLink
{
    private WebRtcPeerConnection? _peer;
    private WebRtcDataChannel? _channel;
    private WebRtcDataChannel? _states;
    private string? _remoteOffer;
    public bool Connected => _peer?.GetConnectionState() == WebRtcPeerConnection.ConnectionState.Connected &&
        _channel?.GetReadyState() == WebRtcDataChannel.ChannelState.Open && _states?.GetReadyState() == WebRtcDataChannel.ChannelState.Open;
    public event Action<JsonElement>? Signal;
    public event Action<byte[]>? Received;
    public bool DisposableSnapshots => _states is not null && !_states.IsOrdered() && _states.GetMaxRetransmits() == 0;
    public void Start(bool offer)
    {
        _peer = new WebRtcPeerConnection();
        // STUN discovers public candidates. If UDP is blocked, the authenticated cluster relay remains available.
        if (_peer.Initialize(new Godot.Collections.Dictionary
        {
            ["iceServers"] = useStun ? new Godot.Collections.Array { new Godot.Collections.Dictionary
                { ["urls"] = new Godot.Collections.Array { "stun:stun.cloudflare.com:3478" } } } : new Godot.Collections.Array()
        }) != Error.Ok) { Dispose(); return; }
        _peer.SessionDescriptionCreated += (type, sdp) =>
        {
            if (_peer.SetLocalDescription(type, sdp) == Error.Ok)
                Signal?.Invoke(JsonSerializer.SerializeToElement(new { kind = "sdp", type, sdp }));
        };
        _peer.IceCandidateCreated += (media, index, candidate) =>
            Signal?.Invoke(JsonSerializer.SerializeToElement(new { kind = "ice", media, index, candidate }));
        _channel = _peer.CreateDataChannel("blast-v1", new Godot.Collections.Dictionary { ["negotiated"] = true, ["id"] = 1, ["ordered"] = true });
        _states = _peer.CreateDataChannel("blast-state-v1", new Godot.Collections.Dictionary
        { ["negotiated"] = true, ["id"] = 2, ["ordered"] = false, ["maxRetransmits"] = 0 });
        if (offer) _peer.CreateOffer();
    }
    public void AcceptSignal(JsonElement signal)
    {
        if (_peer is null) return;
        try
        {
            if (signal.GetProperty("kind").GetString() == "sdp")
            {
                string type = signal.GetProperty("type").GetString()!;
                string sdp = signal.GetProperty("sdp").GetString()!;
                if (type == "offer")
                {
                    if (_remoteOffer == sdp) return;
                    if (_remoteOffer is not null) { Dispose(); Start(false); }
                    _remoteOffer = sdp;
                }
                if (type is "offer" or "answer") _peer?.SetRemoteDescription(type, sdp);
            }
            else if (signal.GetProperty("kind").GetString() == "ice")
                _peer.AddIceCandidate(signal.GetProperty("media").GetString()!, signal.GetProperty("index").GetInt32(), signal.GetProperty("candidate").GetString()!);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException) { }
    }
    public void Poll()
    {
        _peer?.Poll();
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
    }
    public bool Send(byte[] packet) => packet.Length <= PeerPacketCodec.MaximumWireBytes && Connected && _channel!.GetBufferedAmount() < 262144 && _channel.PutPacket(packet) == Error.Ok;
    public bool SendState(byte[] packet) => packet.Length <= PeerPacketCodec.MaximumWireBytes && Connected && _states!.GetBufferedAmount() < 131072 && _states.PutPacket(packet) == Error.Ok;
    public void Dispose()
    {
        _channel?.Close(); _channel?.Dispose(); _channel = null;
        _states?.Close(); _states?.Dispose(); _states = null;
        _peer?.Close(); _peer?.Dispose(); _peer = null;
        _remoteOffer = null;
    }
}

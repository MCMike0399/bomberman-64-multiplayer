using System.Text.Json;
using Bomber.Client;
using Godot;

namespace BlastCircuit.App;

/// <summary>Official webrtc-native adapter; ICE/DTLS/SCTP remain inside the maintained transport library.</summary>
public sealed class GodotPeerLink(bool useStun = true) : IPeerLink
{
    private WebRtcPeerConnection? _peer;
    private WebRtcDataChannel? _channel;
    public bool Connected => _peer?.GetConnectionState() == WebRtcPeerConnection.ConnectionState.Connected && _channel?.GetReadyState() == WebRtcDataChannel.ChannelState.Open;
    public event Action<JsonElement>? Signal;
    public event Action<byte[]>? Received;
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
                if (type is "offer" or "answer") _peer.SetRemoteDescription(type, signal.GetProperty("sdp").GetString()!);
            }
            else if (signal.GetProperty("kind").GetString() == "ice")
                _peer.AddIceCandidate(signal.GetProperty("media").GetString()!, signal.GetProperty("index").GetInt32(), signal.GetProperty("candidate").GetString()!);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException) { }
    }
    public void Poll()
    {
        _peer?.Poll();
        if (_channel is null) return;
        _channel.Poll();
        for (int count = 0; count < 64 && _channel.GetAvailablePacketCount() > 0; count++)
        {
            byte[] packet = _channel.GetPacket();
            if (packet.Length <= 131072) Received?.Invoke(packet);
        }
    }
    public bool Send(byte[] packet) => Connected && _channel!.GetBufferedAmount() < 262144 && _channel.PutPacket(packet) == Error.Ok;
    public void Dispose()
    {
        _channel?.Close(); _channel?.Dispose(); _channel = null;
        _peer?.Close(); _peer?.Dispose(); _peer = null;
    }
}

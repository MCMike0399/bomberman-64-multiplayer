using System.Text.Json;
namespace Bomber.Client.Peer;

/// <summary>Engine-owned WebRTC link. Every method and callback runs on the session's polling thread.</summary>
public interface IPeerLink : IDisposable
{
    bool Connected { get; }
    /// <summary>The native connection reached a terminal state (failed/closed) and
    /// cannot carry traffic again; a transient disconnect is not terminal.</summary>
    bool Failed => false;
    event Action<JsonElement>? Signal;
    event Action<byte[]>? Received;
    void Start(bool offer);
    void AcceptSignal(JsonElement signal);
    void Poll();
    bool Send(byte[] packet);
    bool SendState(byte[] packet) => Send(packet);
}

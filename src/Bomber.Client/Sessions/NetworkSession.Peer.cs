using System.Net.WebSockets;
using System.Text.Json;
using Bomber.Client.Peer;
using Bomber.Game.Online;

namespace Bomber.Client.Sessions;

/// <summary>Peer-hosted rooms: routes coordinator frames to the peer client and queues its packets for the socket.</summary>
public sealed partial class NetworkSession
{
    private void PumpMessages()
    {
        if (Interlocked.Exchange(ref _peerResetPending, 0) != 0) { _peer?.Dispose(); _peer = null; }
        int generation = ConnectionGeneration;
        if (_messages.Overflowed)
        { if (!IsEnded) { Fail("The game cannot keep up with room control messages."); _lifetime.Cancel(); } return; }
        foreach (var message in _wireMessages.Drain())
        {
            if (ConnectionGeneration != generation) return;
            if (message.GetProperty("t").GetString() == "party.status" && message.GetProperty("d").TryGetProperty("hosting", out var hosting) && hosting.GetString() is "peer" or "auto")
                _peer ??= new PeerRoomClient(PlayerId!, _lobbyId!, _peerFactory,
                    (type, data) => { if (ConnectionGeneration == generation) QueuePeer(type, data); },
                    frame => { if (ConnectionGeneration == generation) _messages.Add(frame); }, TakePeerInput, () => Volatile.Read(ref _serverRtt));
            if (_peer?.Handle(message) != true) _messages.Add(message);
        }
    }
    private void QueuePeer(string type, object data)
    {
        var packet = JsonSerializer.SerializeToUtf8Bytes(new { t = type, d = data });
        if (type == "peer.state" && data is PeerFrameEnvelope state)
        {
            if (_latestPeerStates.Count < 8 || _latestPeerStates.ContainsKey(state.To)) _latestPeerStates[state.To] = packet;
            else { Fail("Too many pending peer routes."); _lifetime.Cancel(); }
            return;
        }
        if (_controlOutbound.Count >= 512)
        { Fail("The connection cannot keep up with reliable game messages."); _lifetime.Cancel(); return; }
        _controlOutbound.Enqueue(packet);
    }
    private byte[] TakePeerInput()
    {
        lock (_inputGate)
        {
            var input = _input;
            if (_buttons.TryDequeue(out var buttons)) input = input with { A = buttons.A, B = buttons.B, R = buttons.R, Aim = buttons.Aim, Stop = buttons.Stop };
            return EncodeInput(input, Interlocked.Increment(ref _sequence));
        }
    }
}

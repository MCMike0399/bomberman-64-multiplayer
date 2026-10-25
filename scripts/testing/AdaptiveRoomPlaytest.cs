using System.Diagnostics;
using System.Text.Json;
using Bomber.Client;
using Bomber.Game;
using BlastCircuit.App;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task AdaptiveRooms()
    {
        _root.Navigate("Online"); await Frames(3); await Capture("adaptive-online-options");
        Check(NamedButton("OnlineBack").IsVisibleInTree(), "automatic multiplayer uses the ordinary online screen");
        await _root.StartOnlineAsync(new Uri(_server), "Room owner", new SessionSettings(Stage: "top-rules", Bots: 0, Wins: 1) { Hosting = "peer" }, NetworkMode.CreatePrivate);
        var owner = (NetworkSession)_root.ActiveSession!;
        var guests = new List<NetworkSession>();
        var states = new Dictionary<string, JsonElement>();
        int dropGuest = -1, dropped = 0;
        var links = new List<SnapshotLossLink>();
        try
        {
            for (int index = 0; index < 7; index++)
            {
                int seat = index;
                guests.Add(await NetworkSession.ConnectAsync(new Uri(_server), $"Adaptive {index + 2}", new SessionSettings(Bots: 0), NetworkMode.JoinCode, owner.JoinCode,
                    peerFactory: () => { var link = new SnapshotLossLink(() => dropGuest == seat, () => dropped++, !new Uri(_server).IsLoopback); links.Add(link); return link; }));
            }
            await Until(() => guests.All(p => p.Party?.Adaptive == true && p.ConnectionQualitySamples >= 3) && owner.ConnectionQualitySamples >= 3,
                "eight actual clients measure candidate connections before starting");
            await Capture("adaptive-qualified-room");
            owner.RequestStart();
            await Until(() => owner.Party?.Hosting == "peer" && guests.All(p => p.Party?.HostId == owner.Party.HostId && p.Party?.Phase == "playing") &&
                _root.LatestState is { } state && state.GetProperty("players").GetArrayLength() == 8 && state.GetProperty("phase").GetInt32() == 1,
                "measured player authority starts an eight-player shared match");
            Check(owner.IsLeader && guests.All(p => !p.IsLeader), "simulation host selection does not transfer room ownership");
            Check(links.Where(l => l.Connected).All(l => l.DisposableSnapshots), "native snapshot channels are actually unordered with zero retransmissions");
            await Until(() => owner.DirectPacketsSent + owner.DirectPacketsReceived > 0 && guests.All(p => p.DirectPacketsSent + p.DirectPacketsReceived > 0),
                "gameplay crosses native WebRTC data channels for all eight players");
            await Capture("adaptive-eight-player-match");
            dropGuest = guests.FindIndex(p => p.PlayerId != owner.Party!.HostId);
            var affected = guests[dropGuest];
            int epoch = affected.Party!.Epoch, match = affected.Party.Match;
            affected.SendInput(0, new PlayerInput(0, 0, A: true));
            await Until(() => dropped >= 2 && _root.LatestState is { } state && state.GetProperty("bombs").GetArrayLength() > 0 &&
                states.TryGetValue(affected.PlayerId!, out var partial) && partial.GetProperty("bombs").GetArrayLength() > 0,
                "reliable bomb input survives intentional snapshot loss on the native link");
            affected.ReleaseInputs(); dropGuest = -1;
            await affected.DisposeAsync(); guests.Remove(affected);
            await Until(() => _root.LatestState is { } state && state.GetProperty("players").GetArrayLength() == 7,
                "guest departure removes its actor without restarting the match");
            Check(owner.Party?.Epoch == epoch && owner.Party.Match == match && owner.Party.Phase == "playing", "peer match generation survives ordinary guest departure");
            await Capture("adaptive-match-after-departure");
            Event("adaptive-network", new { players = 8, host = owner.Party!.HostId, owner = owner.PlayerId, droppedSnapshots = dropped, epoch, match });
        }
        finally
        {
            foreach (var guest in guests) await guest.DisposeAsync();
            _root.Back(); await owner.DisposeAsync();
        }
        async Task Until(Func<bool> ready, string description)
        {
            var clock = Stopwatch.StartNew();
            while (!ready() && clock.Elapsed.TotalSeconds < 30)
            {
                foreach (var guest in guests)
                {
                    guest.Advance(1d / 30);
                    foreach (var frame in guest.DrainMessages())
                        if (frame.GetProperty("t").GetString() == "state") states[guest.PlayerId!] = frame.GetProperty("d").Clone();
                }
                await Frames(1); await Task.Delay(10);
            }
            if (!ready()) Event("adaptive-timeout", new { description, owner = owner.Party, quality = owner.ConnectionQualitySamples, measurement = owner.ConnectionQuality,
                guests = guests.Select(p => new { p.PlayerId, p.Party, quality = p.ConnectionQualitySamples, measurement = p.ConnectionQuality }) });
            Check(ready(), description);
        }
    }

    private sealed class SnapshotLossLink(Func<bool> lose, Action dropped, bool useStun) : IPeerLink
    {
        private readonly GodotPeerLink _inner = new(useStun);
        private int _states;
        public bool Connected => _inner.Connected;
        public bool DisposableSnapshots => _inner.DisposableSnapshots;
        public event Action<JsonElement>? Signal;
        public event Action<byte[]>? Received;
        public void Start(bool offer)
        {
            _inner.Signal += signal => Signal?.Invoke(signal);
            _inner.Received += packet =>
            {
                if (lose())
                {
                    if (!PeerPacketCodec.TryDecode(packet, out var decoded)) return;
                    using var json = JsonDocument.Parse(decoded);
                    if (json.RootElement.TryGetProperty("payload", out var payload) && payload.TryGetProperty("t", out var type) &&
                        type.GetString() == "state" && ++_states % 2 == 0) { dropped(); return; }
                }
                Received?.Invoke(packet);
            };
            _inner.Start(offer);
        }
        public void AcceptSignal(JsonElement signal) => _inner.AcceptSignal(signal);
        public void Poll() => _inner.Poll();
        public bool Send(byte[] packet) => _inner.Send(packet);
        public bool SendState(byte[] packet) => _inner.SendState(packet);
        public void Dispose() => _inner.Dispose();
    }
}

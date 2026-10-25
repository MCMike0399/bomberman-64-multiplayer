using System.Text.Json;
using Bomber.Game.Simulation;
using Burbuja.Realtime.Abstractions;

namespace Bomber.Game;

/// <summary>A player-hosted authority with coordinator-authenticated identities. Owned by one client thread.</summary>
public sealed class PeerMatch : ISessionContext
{
    private readonly BlastSession _game;
    private readonly Dictionary<string, long> _sequences = [];
    private readonly Action<string?, string, object?> _emit;
    public PeerMatch(string lobbyId, IReadOnlyDictionary<string, string> settings, int seed,
        IReadOnlyList<RealtimePlayer> players, IReadOnlyDictionary<string, int> appearances, Action<string?, string, object?> emit)
    {
        LobbyId = lobbyId; Settings = settings; Random = new(seed); Players = players; _emit = emit;
        _game = new BlastSession(new(lobbyId, "blast-peer", settings, seed));
        foreach (var p in players)
        {
            _game.OnPlayerJoined(this, p);
            _game.OnMessage(this, p, new("appearance", JsonSerializer.SerializeToElement(new { index = appearances.GetValueOrDefault(p.PlayerId, p.Slot) }), null, Now));
        }
        State = LobbyState.Running; _game.OnMatchStarted(this);
    }
    public void Receive(string sender, JsonElement frame)
    {
        var player = Players.FirstOrDefault(p => p.PlayerId == sender);
        if (player is null || State != LobbyState.Running || !frame.TryGetProperty("t", out var t) || t.GetString() != "input" ||
            !frame.TryGetProperty("s", out var s) || !s.TryGetInt64(out long sequence) || sequence <= _sequences.GetValueOrDefault(sender) || !frame.TryGetProperty("d", out var d)) return;
        _sequences[sender] = sequence;
        _game.OnMessage(this, player, new("input", d, sequence, Now));
    }
    public void Advance() { if (State != LobbyState.Running) return; Tick++; _game.OnTick(this); }
    public void ReleaseInput(string sender)
    {
        var player = Players.FirstOrDefault(p => p.PlayerId == sender);
        if (player is not null) _game.OnPlayerConnectionChanged(this, player, false);
    }
    public string LobbyId { get; }
    public string GameId => "blast-peer";
    public LobbyState State { get; private set; } = LobbyState.Open;
    public IReadOnlyDictionary<string, string> Settings { get; }
    public IReadOnlyList<RealtimePlayer> Players { get; }
    public bool IsConnected(string id) => Players.Any(p => p.PlayerId == id);
    public long? LastSequence(string id) => _sequences.TryGetValue(id, out var s) ? s : null;
    public long Tick { get; private set; }
    public TimeSpan TickDelta => TimeSpan.FromSeconds(1d / LocalMatch.TickRate);
    public DateTimeOffset Now => DateTimeOffset.UnixEpoch + Elapsed;
    public TimeSpan Elapsed => TimeSpan.FromSeconds((double)Tick / LocalMatch.TickRate);
    public Random Random { get; }
    public void Send<T>(string id, string type, T data) => _emit(id, type, data);
    public void Broadcast<T>(string type, T data, string? exceptPlayerId = null)
    { foreach (var p in Players) if (p.PlayerId != exceptPlayerId) Send(p.PlayerId, type, data); }
    public void SendBinary(string id, string type, ReadOnlySpan<byte> data) => throw new NotSupportedException();
    public void BroadcastBinary(string type, ReadOnlySpan<byte> data, string? exceptPlayerId = null) => throw new NotSupportedException();
    public void EndMatch(MatchResult result)
    { if (State != LobbyState.Running) return; State = LobbyState.Ended; _emit(null, "peer.finished", result); }
    public void Kick(string id, string reason) => throw new NotSupportedException();
}

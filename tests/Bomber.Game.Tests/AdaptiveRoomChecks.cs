using System.Text.Json;
using Bomber.Game;
using Burbuja.Realtime.Abstractions;

internal static class AdaptiveRoomChecks
{
    public static int Run()
    {
        int passed = 0;
        void Check(bool value, string description) { if (!value) throw new Exception("FAIL " + description); passed++; }
        var context = new RoomContext();
        var room = new AdaptiveRoomGame().CreateSession(new("adaptive", "blast-room", context.Settings, 64));
        var host = context.Add("host", 0); var guest = context.Add("guest", 1);
        foreach (var player in context.Players)
        { room.OnPlayerJoined(context, player); Send(player, "peer.hello", new { version = OnlineProtocol.Revision }); }
        Send(host, "party.start", new { });
        Check(context.Status.GetProperty("hosting").GetString() == "cluster", "automatic room selects available dedicated authority without a qualified peer");
        Check(context.Claims.GetValueOrDefault("simulation") == 1, "only the active dedicated match reserves simulation capacity");
        Check(context.Frames.Any(f => f.Type == "stage"), "dedicated start initializes the real shared simulation");
        var fallback = new RoomContext(); fallback.Limits["simulation"] = 0;
        var adaptive = new AdaptiveRoomGame().CreateSession(new("fallback", "blast-room", fallback.Settings, 64));
        var a = fallback.Add("a", 0); var b = fallback.Add("b", 1); var d = fallback.Add("c", 2);
        foreach (var player in fallback.Players)
        { adaptive.OnPlayerJoined(fallback, player); Post(player, "peer.hello", new { version = OnlineProtocol.Revision }); }
        Post(a, "party.start", new { });
        Check(fallback.Status.GetProperty("phase").GetString() == "lobby", "without capacity or measurements the room stays waiting");
        int generation = fallback.Status.GetProperty("epoch").GetInt32();
        foreach (var player in fallback.Players)
        {
            var links = fallback.Players.Where(p => p.PlayerId != player.PlayerId).Select(p => new
            { id = p.PlayerId, rtt = player.PlayerId == "b" || p.PlayerId == "b" ? 30 : 150, loss = 0d, direct = true, samples = 5 });
            Post(player, "peer.quality", new { epoch = generation, serverRtt = 120d, frameGap = 16d, links });
        }
        Post(a, "party.start", new { });
        Check(fallback.Status.GetProperty("hosting").GetString() == "peer", "qualified peer runs the match when dedicated capacity is unavailable");
        Check(fallback.Status.GetProperty("hostId").GetString() == "b", "host selection protects the worst connection rather than choosing the oldest player");
        Check(fallback.Status.GetProperty("leaderId").GetString() == "a", "room leadership remains with its owner when another player hosts");
        Check(fallback.Claims.GetValueOrDefault("peer-relay") == 1 && !fallback.Claims.ContainsKey("simulation"), "peer match owns a fallback relay reservation instead of simulation capacity");
        var privateServer = new RoomContext(); privateServer.Limits["simulation"] = 0;
        var privateSettings = privateServer.Settings.ToDictionary(); privateSettings["hosting"] = "server"; privateServer.Settings = privateSettings;
        var serverOnly = new AdaptiveRoomGame().CreateSession(new("private-server", "blast-room", privateServer.Settings, 64));
        privateServer.Add("x", 0); privateServer.Add("y", 1);
        foreach (var player in privateServer.Players)
        { serverOnly.OnPlayerJoined(privateServer, player); Secure(player, "peer.hello", new { version = OnlineProtocol.Revision }); }
        foreach (var player in privateServer.Players)
            Secure(player, "peer.quality", new { epoch = privateServer.Status.GetProperty("epoch").GetInt32(), serverRtt = 120d, frameGap = 16d,
                links = privateServer.Players.Where(p => p != player).Select(p => new { id = p.PlayerId, rtt = 10d, loss = 0d, direct = true, samples = 5 }) });
        Secure(privateServer.Players[0], "party.start", new { });
        Check(privateServer.Status.GetProperty("phase").GetString() == "lobby" && privateServer.Claims.Count == 0, "server-only rooms never silently fall back to a player authority");
        Console.WriteLine($"PASS {passed} adaptive room checks");
        return passed;
        void Send(RealtimePlayer player, string type, object data) => room.OnMessage(context, player,
            new(type, JsonSerializer.SerializeToElement(data), null, context.Now));
        void Post(RealtimePlayer player, string type, object data) => adaptive.OnMessage(fallback, player,
            new(type, JsonSerializer.SerializeToElement(data), null, fallback.Now));
        void Secure(RealtimePlayer player, string type, object data) => serverOnly.OnMessage(privateServer, player,
            new(type, JsonSerializer.SerializeToElement(data), null, privateServer.Now));
    }

    internal sealed class RoomContext : ISessionContext
    {
        public string LobbyId => "adaptive"; public string GameId => "blast-room";
        public LobbyState State => LobbyState.Running;
        public IReadOnlyDictionary<string, string> Settings { get; set; } = new MatchOptions { HumanPlayers = 2, BotPlayers = 0 }.ToSettings();
        private readonly List<RealtimePlayer> _players = [];
        public IReadOnlyList<RealtimePlayer> Players => _players;
        public HashSet<string> Connected { get; } = [];
        public Dictionary<string, int> Limits { get; } = new() { ["simulation"] = 1, ["peer-relay"] = 1 };
        public Dictionary<string, int> Claims { get; } = [];
        public bool TryReserveResource(string name, int units)
        { if (units < 0 || !Limits.TryGetValue(name, out int limit) || units > limit) return false; if (units == 0) Claims.Remove(name); else Claims[name] = units; return true; }
        public RealtimePlayer Add(string id, int slot) { var p = new RealtimePlayer(id, id, slot, new Dictionary<string, string>()); _players.Add(p); Connected.Add(id); return p; }
        public bool IsConnected(string id) => Connected.Contains(id);
        public long? LastSequence(string id) => null;
        public long Tick { get; set; }
        public TimeSpan TickDelta => TimeSpan.FromSeconds(1d / 30);
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch + Elapsed;
        public TimeSpan Elapsed => TimeSpan.FromSeconds(Tick / 30d);
        public Random Random { get; } = new(64);
        public List<(string Recipient, string Type, JsonElement Data)> Frames { get; } = [];
        public JsonElement Status => Frames.Last(f => f.Type == "party.status").Data;
        public void Send<T>(string id, string type, T data) => Frames.Add((id, type, JsonSerializer.SerializeToElement(data, RealtimeJson.Default)));
        public void Broadcast<T>(string type, T data, string? exceptPlayerId = null) => Send("*", type, data);
        public void SendBinary(string id, string type, ReadOnlySpan<byte> data) => throw new NotSupportedException();
        public void BroadcastBinary(string type, ReadOnlySpan<byte> data, string? exceptPlayerId = null) => throw new NotSupportedException();
        public void EndMatch(MatchResult result) => throw new NotSupportedException();
        public List<string> Kicked { get; } = [];
        public void Kick(string id, string reason) => Kicked.Add(id);
    }
}

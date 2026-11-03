using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation;
using Burbuja.Realtime.Abstractions;
using static GameFixtures;

internal static class StaleInputChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string reason) { Assert(condition, reason); checks++; }
        var context = new Context();
        var game = new BlastSession(new GameSessionOptions("input-timeout", "blast", context.Settings, 64), FlatArena([(3, 3), (25, 25)], 30));
        foreach (var player in context.Players) game.OnPlayerJoined(context, player);
        context.State = LobbyState.Running; game.OnMatchStarted(context);
        void Step(int ticks) { for (int i = 0; i < ticks; i++) { context.Tick++; game.OnTick(context); } }
        void Input(float x, bool bomb = false) => game.OnMessage(context, context.Players[0], new InboundMessage("input",
            JsonSerializer.SerializeToElement(new { x, z = 0, a = bomb, b = false, r = false }), ++context.Sequence, context.Now));
        float X() => context.Latest.GetProperty("players")[0].GetProperty("x").GetSingle();
        Step(62); Input(1); Step(8);
        float before = X();
        Check(before > 3.5f, "fresh held movement advances before its input deadline");
        Step(8); float stopped = X(); Step(15);
        Check(Math.Abs(X() - stopped) < .01f, "a silent client's held movement is neutral after 500 ms");
        Check(context.Latest.GetProperty("timeLeft").GetSingle() < 119.5f,
            "world time continues while a silent player's movement is neutral");
        Input(-1); Step(4);
        Check(X() < stopped - .1f, "fresh input resumes immediately after stale movement was neutralized");
        Input(0, bomb: true); Step(4); Input(0); Step(12);
        var bombs = context.Latest.GetProperty("bombs");
        Check(bombs.GetArrayLength() == 1, "a real timed bomb exists during silent-player observation");
        float fuse = bombs[0].GetProperty("fuse").GetSingle(); Step(15);
        Check(context.Latest.GetProperty("bombs")[0].GetProperty("fuse").GetSingle() < fuse - .4f,
            "bomb simulation continues while stale movement is neutralized");

        // The local adapter refreshes the live input heartbeat, without
        // replaying a payload that a respawn or other rule may have cleared.
        var local = new LocalMatch(new MatchOptions { HumanPlayers = 2, BotPlayers = 0, SuddenDeath = false }, FlatArena([(3, 3), (25, 25)], 30));
        Fight(local); local.SetInput(0, new PlayerInput(1, 0));
        GameFixtures.Step(local, 30);
        Check(Player(local).GetProperty("x").GetSingle() > 6,
            "local held controls remain live beyond the stale-network timeout");
        local.SetInput(0, default); GameFixtures.Step(local, 4); float localStopped = Player(local).GetProperty("x").GetSingle();
        GameFixtures.Step(local, 15);
        Check(Math.Abs(Player(local).GetProperty("x").GetSingle() - localStopped) < .01f,
            "local explicit release still stops movement");
        JsonElement peerState = default;
        var peer = new PeerMatch("peer-input-timeout", new MatchOptions { HumanPlayers = 2, BotPlayers = 0,
            SuddenDeath = false, Advanced = new AdvancedRules { BlockPercent = 0, DropPercent = 0 } }.ToSettings(), 64,
            context.Players, new Dictionary<string, int>(), (_, type, payload) => { if (type == "state") peerState = JsonSerializer.SerializeToElement(payload); });
        for (int i = 0; i < 62; i++) peer.Advance();
        void PeerInput(float x, long sequence) => peer.Receive("p0", JsonSerializer.SerializeToElement(new
            { t = "input", s = sequence, d = new { x, z = 0, a = false, b = false, r = false } }));
        float PeerX() => peerState.GetProperty("players")[0].GetProperty("x").GetSingle();
        PeerInput(1, 1); for (int i = 0; i < 16; i++) peer.Advance();
        float peerStopped = PeerX(); for (int i = 0; i < 15; i++) peer.Advance();
        Check(Math.Abs(PeerX() - peerStopped) < .01f, "peer authority applies the same stale movement deadline");
        PeerInput(-1, 2); for (int i = 0; i < 4; i++) peer.Advance();
        Check(PeerX() < peerStopped - .1f, "peer authority resumes fresh input without a world reset");
        for (int i = 0; i < 1000; i++) Bomber.Game.Diagnostics.InputSilenceTelemetry.Record("test-room", "test-player", "stalled", i);
        Check(Bomber.Game.Diagnostics.InputSilenceTelemetry.Drain().Count == 256, "input-stall telemetry stays bounded even without a consumer");
        Console.WriteLine($"PASS {checks} stale input safety checks");
        return checks;
    }

    private sealed class Context : ISessionContext
    {
        public string LobbyId => "input-timeout";
        public string GameId => "blast";
        public LobbyState State { get; set; } = LobbyState.Open;
        public IReadOnlyDictionary<string, string> Settings { get; } = new MatchOptions { HumanPlayers = 2, BotPlayers = 0, SuddenDeath = false }.ToSettings();
        public IReadOnlyList<RealtimePlayer> Players { get; } = [new("p0", "P0", 0, new Dictionary<string, string>()), new("p1", "P1", 1, new Dictionary<string, string>())];
        public bool IsConnected(string playerId) => true;
        public long Sequence;
        public long? LastSequence(string playerId) => playerId == "p0" ? Sequence : 0;
        public long Tick { get; set; }
        public TimeSpan TickDelta => TimeSpan.FromSeconds(1d / 30);
        public TimeSpan Elapsed => TimeSpan.FromSeconds((double)Tick / 30);
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch + Elapsed;
        public Random Random { get; } = new(64);
        public JsonElement Latest;
        public void Send<T>(string playerId, string type, T payload) { }
        public void Broadcast<T>(string type, T payload, string? exceptPlayerId = null)
        { if (type == "state") Latest = JsonSerializer.SerializeToElement(payload); }
        public void SendBinary(string playerId, string type, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public void BroadcastBinary(string type, ReadOnlySpan<byte> payload, string? exceptPlayerId = null) => throw new NotSupportedException();
        public void EndMatch(MatchResult result) => State = LobbyState.Ended;
        public void Kick(string playerId, string reason) => throw new NotSupportedException();
    }
}

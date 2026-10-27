using System.Diagnostics;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Godot;

namespace BlastCircuit.Testing;

/// <summary>One real packaged client per OS, synchronized by the external runner after renderer startup.</summary>
public partial class PlaytestDriver
{
    private async Task CrossPlatformQuickPlay()
    {
        Check(!_headless, "three-platform Quick Play uses the real platform renderer");
        var args = OS.GetCmdlineUserArgs();
        string role = args.FirstOrDefault(a => a.StartsWith("--crossplay-role="))?.Split('=', 2)[1] ?? throw new InvalidOperationException("Missing crossplay role");
        string barrier = args.FirstOrDefault(a => a.StartsWith("--crossplay-release="))?.Split('=', 2)[1] ?? throw new InvalidOperationException("Missing crossplay release barrier");
        File.WriteAllText(Path.Combine(_output, "crossplay-ready.json"), JsonSerializer.Serialize(new { role, platform = OS.GetName() }));
        await Until(() => File.Exists(barrier), "all three rendered clients are ready", 300);

        string name = "QA " + role;
        await _root.StartOnlineAsync(new Uri(_server), name, new SessionSettings(Bots: 0), NetworkMode.QuickPlay);
        var session = (NetworkSession)_root.ActiveSession!;
        await Until(() => Roster().Length == 3 && PartyDeadline() is not null, "three operating systems join one public lobby", 60);
        string lobby = session.LobbyId!;
        long initial = PartyDeadline()!.Value;
        string oldWindows = LobbyStatus.Parse(_root.LatestParty!.Value).Players.Single(p => p.Name == "QA windows-x64" && p.Connected).Id;
        Check(session.Party is { PublicRoom: true, CanStart: false }, "public matchmaking grants no start permission");
        Check(!NamedButton("StartRoom").Visible && !NamedButton("EditRoomSettings").Visible, "public controls are absent on this platform");
        Check(VisibleText(_root).Contains("Shared rules"), "public rules are visible on this platform");
        await Capture("crossplay-three-os-countdown");

        if (role == "windows-x64")
        {
            // Give every renderer time to observe the original roster/deadline.
            await Pause(8);
            _root.Back(); await Frames(3); await Pause(1.5);
            await _root.StartOnlineAsync(new Uri(_server), name, new SessionSettings(Bots: 0), NetworkMode.QuickPlay);
            session = (NetworkSession)_root.ActiveSession!;
            Check(session.LobbyId == lobby, "departed Windows player rejoins the same waiting public lobby");
        }
        await Until(() => Roster().Length == 3 && !Roster().Contains(oldWindows) && PartyDeadline() > initial + 2000,
            "departure and rejoin produce a fresh common minute", 45);
        long deadline = PartyDeadline()!.Value;
        var roster = Roster();
        Check(deadline - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > 45000, "changed roster receives a fresh full-minute countdown");
        await Capture("crossplay-roster-reset");
        while (session.Party?.Phase == "lobby")
        {
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < deadline - 250 && _root.LatestState is not null)
                Check(false, "authority does not start gameplay before the public countdown deadline");
            await Frames(1); await Task.Delay(50);
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > deadline + 20000) throw new TimeoutException("Public countdown never started a match");
        }
        Check(true, "authority holds gameplay for the real public countdown");
        Check(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= deadline - 250, "the first playing transition does not precede the public deadline");
        await Until(() => _root.LatestState is { } state && state.GetProperty("phase").GetInt32() == 1,
            "public match automatically reaches fight after one real minute", 30);
        Check(State.GetProperty("serverTime").GetInt64() >= deadline, "automatic match starts after the shared authority deadline");
        Check(_root.FirstNetworkStateServerTime >= deadline, "the first received authoritative gameplay state does not precede the deadline");
        Check(session.Party?.Hosting == "cluster", "three different operating systems share the staged server authority");
        var players = State.GetProperty("players").EnumerateArray().Select(p => p.GetProperty("id").GetString()!).Order().ToArray();
        Check(players.SequenceEqual(roster), "all three platform identities become the actual match actors");
        int firstSnapshots = _root.SnapshotCount;
        long firstTime = State.GetProperty("serverTime").GetInt64();
        var positions = State.GetProperty("players").EnumerateArray().ToDictionary(p => p.GetProperty("id").GetString()!,
            p => new Vector2(p.GetProperty("x").GetSingle(), p.GetProperty("z").GetSingle()));
        var moved = new HashSet<string>(); var bombOwners = new HashSet<string>();
        long actionAt = deadline + 14000;
        await ObserveUntil(() => State.GetProperty("serverTime").GetInt64() >= actionAt, 20);
        foreach (var direction in new[] { Vector2.Right, Vector2.Down, Vector2.Left, Vector2.Up })
        {
            if (moved.Contains(session.PlayerId!)) break;
            Input(0, new PlayerInput(direction.X, direction.Y));
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < .8 && !moved.Contains(session.PlayerId!))
            { await Frames(1); await Task.Delay(16); Observe(); }
        }
        Input(0, default);
        Check(moved.Contains(session.PlayerId!), "ordinary local movement is accepted by the shared authority");
        await ObserveUntil(() => State.GetProperty("serverTime").GetInt64() >= actionAt + 6000, 15);
        // Hold carry plus primary is a normal input path. Keep its fuse paused
        // while the other OS clients finish observing the same accepted bomb.
        Input(0, new PlayerInput(0, 0, B: true)); await Frames(2); await Task.Delay(120);
        Input(0, new PlayerInput(0, 0, A: true, B: true));
        await Frames(2); await Task.Delay(120); Observe(); Input(0, new PlayerInput(0, 0, B: true));
        await ObserveUntil(() => players.All(bombOwners.Contains) && players.All(moved.Contains), 20);
        Check(true, "every platform observes movement and accepted bombs from all three players");
        Check(_root.SnapshotCount - firstSnapshots > 4 && State.GetProperty("serverTime").GetInt64() - firstTime > 5000,
            "all clients observe advancing authoritative gameplay");
        await Capture("crossplay-three-os-gameplay");
        bool lateExcluded = false;
        if (role == "linux-x64")
        {
            await using var late = await NetworkSession.ConnectAsync(new Uri(_server), "QA late stranger", new SessionSettings(Bots: 0), NetworkMode.QuickPlay);
            Check(late.LobbyId != lobby, "a stranger arriving during gameplay gets a different waiting room");
            lateExcluded = true;
        }
        Check(State.GetProperty("players").GetArrayLength() == 3, "late matchmaking cannot change this match roster");
        long lastTime = State.GetProperty("serverTime").GetInt64();
        int snapshots = _root.SnapshotCount - firstSnapshots;
        File.WriteAllText(Path.Combine(_output, "crossplay-actions.json"), JsonSerializer.Serialize(new { passed = true, role, lobbyId = lobby, deadline }));
        await Until(() => File.Exists(Path.Combine(_output, "finish")), "all three clients finish before any original participant leaves", 120);
        File.WriteAllText(Path.Combine(_output, "crossplay.json"), JsonSerializer.Serialize(new {
            passed = true, role, platform = OS.GetName(), lobbyId = lobby, roster, initialDeadline = initial,
            deadline, resetObserved = deadline > initial + 2000, moved = moved.Order().ToArray(), bombOwners = bombOwners.Order().ToArray(),
            snapshots, firstServerTime = firstTime, firstGameplayServerTime = _root.FirstNetworkStateServerTime,
            lastServerTime = lastTime, lateExcluded,
            hosting = session.Party?.Hosting,
            driver = RenderingServer.GetCurrentRenderingDriverName(), method = RenderingServer.GetCurrentRenderingMethod(), adapter = RenderingServer.GetVideoAdapterName()
        }, new JsonSerializerOptions { WriteIndented = true }));
        _root.Back(); await Frames(4);

        string[] Roster() => _root.LatestParty is { } raw
            ? LobbyStatus.Parse(raw).Players.Where(p => p.Connected).Select(p => p.Id).Order().ToArray() : [];
        long? PartyDeadline() => _root.LatestParty is { } raw && raw.TryGetProperty("startAt", out var at) && at.ValueKind == JsonValueKind.Number ? at.GetInt64() : null;
        async Task Pause(double seconds)
        { var clock = Stopwatch.StartNew(); while (clock.Elapsed.TotalSeconds < seconds) { await Frames(1); await Task.Delay(16); } }
        async Task Until(Func<bool> condition, string label, double seconds)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.Elapsed.TotalSeconds > seconds) throw new TimeoutException(label);
                await Frames(1); await Task.Delay(16);
            }
            Check(true, label);
        }
        void Observe()
        {
            foreach (var player in State.GetProperty("players").EnumerateArray())
            {
                string id = player.GetProperty("id").GetString()!;
                if (positions.TryGetValue(id, out var initialPosition) && initialPosition.DistanceTo(new(player.GetProperty("x").GetSingle(), player.GetProperty("z").GetSingle())) > .12f)
                    moved.Add(id);
            }
            foreach (var bomb in State.GetProperty("bombs").EnumerateArray())
                if (bomb.TryGetProperty("ownerId", out var owner) && owner.GetString() is { } id) bombOwners.Add(id);
        }
        async Task ObserveUntil(Func<bool> condition, double seconds)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.Elapsed.TotalSeconds > seconds) throw new TimeoutException("Three-platform movement/bomb witness timed out");
                await Frames(1); await Task.Delay(16); Observe();
            }
        }
    }
}

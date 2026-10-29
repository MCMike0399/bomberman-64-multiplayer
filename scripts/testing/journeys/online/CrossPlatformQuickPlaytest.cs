using System.Diagnostics;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Godot;

namespace BlastCircuit.Testing;

/// <summary>One real packaged client per OS, synchronized by the external runner after renderer startup.</summary>
public partial class PlaytestDriver
{
    private string _crossplayStage="initial";
    private long? _crossplayDeadline;
    private readonly List<object> _crossplayTimeline=[];
    private object CrossplayObservation()=>new
    {
        stage=_crossplayStage,localUtcMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),monotonic=Stopwatch.GetTimestamp(),
        frame=Engine.GetProcessFrames(),drawn=Engine.GetFramesDrawn(),captureFallbacks=_captureFallbacks,deadline=_crossplayDeadline,
        screen=_root.ScreenName,
        socket=_root.ActiveSession is NetworkSession connection?new{player=connection.PlayerId,lobby=connection.LobbyId,
            generation=connection.ConnectionGeneration,status=connection.Status,party=connection.Party,
            deadline=connection.DeadlineObserved,events=connection.ConnectionEvents}:null,
        renderedParty=_root.LatestParty,authority=_root.LatestState,firstAuthorityTime=_root.FirstNetworkStateServerTime,
        authorityTick=typeof(BlastCircuit.Arena.ArenaView).GetField("_snapshotTick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(LightingArena)
    };
    private void CrossplayMark(string stage)
    {
        _crossplayStage=stage;var observation=CrossplayObservation();Event("crossplay-transition",observation);
        if(_crossplayTimeline.Count<32)_crossplayTimeline.Add(observation);
    }
    private async Task CrossPlatformQuickPlay()
    {
        Check(!_headless, "three-platform Quick Play uses the real platform renderer");
        _crossplayStage="initial";_crossplayDeadline=null;_crossplayTimeline.Clear();
        try{await CrossPlatformQuickPlayCore();}
        catch(Exception error)
        {
            // Snapshot before Back/disposal clears the party and authority. A
            // screenshot failure must not replace the original failing predicate.
            try
            {
                var observation=CrossplayObservation();Event("crossplay-precleanup-failure",observation);
                File.WriteAllText(Path.Combine(_output,"crossplay-failure.json"),JsonSerializer.Serialize(new{
                    error=error.ToString(),observation,timeline=_crossplayTimeline},new JsonSerializerOptions{WriteIndented=true}));
                await Capture("crossplay-precleanup-failure",0);
            }
            catch(Exception diagnostic){Event("crossplay-diagnostic-failure",new{error=diagnostic.ToString()});}
            throw;
        }
        finally{Input(0,default);_root.Back();await Frames(4);}
    }
    private static JsonElement CommitCrossplayWitness(Func<object> capture)
    {
        var snapshot=JsonSerializer.SerializeToElement(capture());
        if(snapshot.GetProperty("hosting").GetString()!="cluster")throw new InvalidDataException("Completed witness did not use cluster authority");
        var roster=snapshot.GetProperty("roster").EnumerateArray().Select(x=>x.GetString()!).ToHashSet(StringComparer.Ordinal);
        if(roster.Count!=3||!roster.SetEquals(snapshot.GetProperty("moved").EnumerateArray().Select(x=>x.GetString()!))||
            !roster.SetEquals(snapshot.GetProperty("bombOwners").EnumerateArray().Select(x=>x.GetString()!)))
            throw new InvalidDataException("Completed witness lacks the same three actors/actions");
        return snapshot;
    }
    private Task CrossplayWitnessLifetime()
    {
        var actors=new[]{"a","b","c"};
        var source=new Dictionary<string,object>{["hosting"]="cluster",["match"]=2,["epoch"]=6,["roster"]=actors,["moved"]=actors,["bombOwners"]=actors};
        var committed=CommitCrossplayWitness(()=>source);
        actors[0]="changed-after-commit";
        source["hosting"]="auto";source["match"]=99;source["epoch"]=100;source["roster"]=new[]{"c"};
        var serialized=JsonSerializer.SerializeToElement(committed);
        Check(serialized.GetProperty("hosting").GetString()=="cluster"&&serialized.GetProperty("match").GetInt32()==2&&serialized.GetProperty("epoch").GetInt32()==6,
            "post-commit lobby mutation cannot rewrite the completed authority/match witness");
        Check(serialized.GetProperty("roster").GetArrayLength()==3&&serialized.GetProperty("roster")[0].GetString()=="a","completed witness retains original cohort after nested mutation and peer teardown");
        bool rejected=false;try{CommitCrossplayWitness(()=>source);}catch(InvalidDataException){rejected=true;}
        Check(rejected,"a witness actually committed under auto/lobby authority is rejected");
        return Task.CompletedTask;
    }
    private async Task CrossPlatformQuickPlayCore()
    {
        var args = OS.GetCmdlineUserArgs();
        string role = args.FirstOrDefault(a => a.StartsWith("--crossplay-role="))?.Split('=', 2)[1] ?? throw new InvalidOperationException("Missing crossplay role");
        string barrier = args.FirstOrDefault(a => a.StartsWith("--crossplay-release="))?.Split('=', 2)[1] ?? throw new InvalidOperationException("Missing crossplay release barrier");
        File.WriteAllText(Path.Combine(_output, "crossplay-ready.json"), JsonSerializer.Serialize(new { role, platform = OS.GetName() }));
        await Until(() => File.Exists(barrier), "all three rendered clients are ready", 300);

        CrossplayMark("renderers-ready");
        string name = "QA " + role;
        await _root.StartOnlineAsync(new Uri(_server), name, new SessionSettings(Bots: 0), NetworkMode.QuickPlay);
        var session = (NetworkSession)_root.ActiveSession!;
        await Until(() => Roster().Length == 3 && PartyDeadline() is not null, "three operating systems join one public lobby", 60);
        string lobby = session.LobbyId!;
        long initial = PartyDeadline()!.Value;CrossplayMark("initial-cohort");
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
        _crossplayDeadline=deadline;CrossplayMark("reset-cohort");
        Check(session.DeadlineObserved?.StartAt==deadline,"changed roster deadline reaches the socket and renderer coherently");
        await Capture("crossplay-roster-reset");
        var transition=Stopwatch.StartNew();string? previousPhase=null;
        while(true)
        {
            string? phase=session.Party?.Phase;
            if(phase!=previousPhase){CrossplayMark("party-phase:"+(phase??"missing"));previousPhase=phase;}
            if(_root.FirstNetworkStateServerTime is {} first&&first<deadline)
                Check(false,"authoritative gameplay never precedes its own server deadline");
            if(phase=="playing")break;
            if(phase is not ("lobby" or "starting"))throw new InvalidOperationException("Unexpected public countdown phase: "+phase);
            if(transition.Elapsed.TotalSeconds>120)throw new TimeoutException("Public countdown never entered playing");
            await Frames(1);await Task.Delay(50);
        }
        Check(true,"public countdown explicitly reaches playing");
        CrossplayMark("playing");
        // Exact reset-to-60s/no-early policy is checked on the server clock by
        // PublicMatchmakingChecks. Native propagation uses the same server's
        // first gameplay timestamp below, never a localUTC +/-250ms assumption.
        await Until(() => _root.LatestState is { } state && state.GetProperty("phase").GetInt32() == 1,
            "public match automatically reaches fight after one real minute", 30);
        Check(State.GetProperty("serverTime").GetInt64() >= deadline, "automatic match starts after the shared authority deadline");
        Check(_root.FirstNetworkStateServerTime >= deadline, "the first received authoritative gameplay state does not precede the deadline");
        CrossplayMark("fight");
        Check(session.Party?.Hosting == "cluster", "three different operating systems share the staged server authority");
        var players = State.GetProperty("players").EnumerateArray().Select(p => p.GetProperty("id").GetString()!).Order().ToArray();
        Check(players.SequenceEqual(roster), "all three platform identities become the actual match actors");
        int firstSnapshots = _root.SnapshotCount;
        long firstTime = State.GetProperty("serverTime").GetInt64();
        var positions = State.GetProperty("players").EnumerateArray().ToDictionary(p => p.GetProperty("id").GetString()!,
            p => new Vector2(p.GetProperty("x").GetSingle(), p.GetProperty("z").GetSingle()));
        var moved = new HashSet<string>(); var bombOwners = new HashSet<string>();
        var directions=State.GetProperty("players").EnumerateArray().ToDictionary(p=>p.GetProperty("id").GetString()!,SafeNetworkNudge);
        var firstAcks=State.GetProperty("players").EnumerateArray().ToDictionary(p=>p.GetProperty("id").GetString()!,p=>p.GetProperty("ack").GetInt64());
        int observedRound=State.GetProperty("round").GetInt32(),observedMatch=session.Party!.Match,observedEpoch=session.Party.Epoch;
        long actionAt = deadline + 14000;
        await ObserveUntil(() => State.GetProperty("serverTime").GetInt64() >= actionAt, 20);
        // Establish a persistent accepted bomb before moving. Both the injected
        // input and socket baseline retain carry until the final cohort barrier.
        var carry=new PlayerInput(0,0,B:true);
        await NetworkPulse(session,new PlayerInput(0,0,A:true,B:true),carry);
        await ObserveUntil(()=>bombOwners.Contains(session.PlayerId!),15);
        Check(true,"a healthy local actor owns and carries an accepted bomb");CrossplayMark("local-bomb");
        var direction=directions[session.PlayerId!];
        long movementSequence=await NetworkPulse(session,new PlayerInput(direction.X,direction.Y,B:true),carry);
        await ObserveUntil(()=>moved.Contains(session.PlayerId!)&&State.GetProperty("players").EnumerateArray()
            .Single(p=>p.GetProperty("id").GetString()==session.PlayerId).GetProperty("ack").GetInt64()>movementSequence,15);
        Check(true,"ordinary local movement is accepted by the shared authority without leaving supported ground");CrossplayMark("local-movement");
        await ObserveUntil(() => players.All(bombOwners.Contains) && players.All(moved.Contains), 20);
        Check(true, "every platform observes movement and accepted bombs from all three players");
        Check(State.GetProperty("players").EnumerateArray().All(HealthyNetworkActor)&&players.All(id=>
            State.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("ownerId").GetString()==id&&b.GetProperty("carrierId").GetString()==id)),
            "all three healthy actors retain their own carried bombs through the action barrier");
        Check(_root.SnapshotCount - firstSnapshots > 4 && State.GetProperty("serverTime").GetInt64() - firstTime > 5000,
            "all clients observe advancing authoritative gameplay");
        CrossplayMark("cohort-actions");await Capture("crossplay-three-os-gameplay");
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
        // Commit all match evidence before announcing readiness for peer cleanup.
        // Party/state can legitimately return to lobby while the finish file travels.
        var committedParty=session.Party??throw new InvalidDataException("Missing party at witness commit");
        var committedState=State.Clone();
        Check(committedParty.Phase=="playing"&&committedParty.Match==observedMatch&&committedParty.Epoch==observedEpoch&&
            committedState.GetProperty("phase").GetInt32()==1&&committedState.GetProperty("round").GetInt32()==observedRound&&
            committedState.GetProperty("players").EnumerateArray().Select(p=>p.GetProperty("id").GetString()).Order().SequenceEqual(roster),
            "completed witness remains in the same active match, round and cohort");
        var backend=BlastCircuit.Rendering.RendererBackend.Observe();
        var committed=CommitCrossplayWitness(()=>new {
            passed = true, role, platform = OS.GetName(), lobbyId = lobby, roster, initialDeadline = initial,
            deadline, resetObserved = deadline > initial + 2000, moved = moved.Order().ToArray(), bombOwners = bombOwners.Order().ToArray(),
            snapshots, firstServerTime = firstTime, firstGameplayServerTime = _root.FirstNetworkStateServerTime,
            lastServerTime = lastTime, lateExcluded,hosting=committedParty.Hosting,match=committedParty.Match,epoch=committedParty.Epoch,
            round=committedState.GetProperty("round").GetInt32(),authority=committedState,
            captures=_screenshots.Select(path=>new{file=Path.GetFileName(path),sha256=ReplayHash(File.ReadAllBytes(path))}).ToArray(),
            driver = backend.Driver, requestedDriver=backend.RequestedDriver, fallbackReason=backend.FallbackReason, method = RenderingServer.GetCurrentRenderingMethod(), adapter = RenderingServer.GetVideoAdapterName()
        });
        File.WriteAllText(Path.Combine(_output,"crossplay-committed.json"),committed.GetRawText());
        File.WriteAllText(Path.Combine(_output,"crossplay-actions.json"),committed.GetRawText());
        await Until(() => File.Exists(Path.Combine(_output, "finish")), "all three observers commit required match evidence before cleanup is permitted", 120);
        File.WriteAllText(Path.Combine(_output,"crossplay.json"),committed.GetRawText());
        CrossplayMark("finished");RecordProgress();

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
            var state=State;
            if(state.GetProperty("round").GetInt32()!=observedRound||state.GetProperty("phase").GetInt32()!=1)
                throw new InvalidOperationException("Crossplay actions must stay in the same live round");
            var actors=state.GetProperty("players").EnumerateArray().ToDictionary(p=>p.GetProperty("id").GetString()!);
            foreach (var (id,player) in actors)
            {
                if(!HealthyNetworkActor(player)||!positions.TryGetValue(id,out var initialPosition))continue;
                var displacement=new Vector2(player.GetProperty("x").GetSingle(),player.GetProperty("z").GetSingle())-initialPosition;
                var direction=directions[id];
                if(state.GetProperty("serverTime").GetInt64()>=actionAt&&player.GetProperty("ack").GetInt64()>firstAcks[id]&&
                    displacement.Dot(direction)>.12f&&displacement.Length()<1&&Math.Abs(displacement.Cross(direction))<.2f)
                    moved.Add(id);
            }
            foreach (var bomb in state.GetProperty("bombs").EnumerateArray())
                if (bomb.TryGetProperty("ownerId",out var owner)&&owner.GetString() is {} id&&
                    bomb.TryGetProperty("carrierId",out var carrier)&&carrier.GetString()==id&&
                    actors.TryGetValue(id,out var actor)&&HealthyNetworkActor(actor)&&actor.GetProperty("carrying").GetString()=="bomb")bombOwners.Add(id);
        }
        void RecordProgress()=>File.WriteAllText(Path.Combine(_output,"crossplay-progress.json"),JsonSerializer.Serialize(new{
            role,deadline,observedRound,moved=moved.Order(),bombOwners=bombOwners.Order(),state=State,
            connection=session.ConnectionEvents},new JsonSerializerOptions{WriteIndented=true}));
        async Task ObserveUntil(Func<bool> condition, double seconds)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.Elapsed.TotalSeconds > seconds){RecordProgress();throw new TimeoutException("Three-platform movement/bomb witness timed out");}
                await Frames(1); await Task.Delay(16); Observe();
            }
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Online;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var origin = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "http://127.0.0.1:18496";
using var http = new HttpClient { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(10) };
var clock = Stopwatch.StartNew();
var games = await Api(HttpMethod.Get, "/realtime/v1/games");
Check(games.GetProperty("value").EnumerateArray().Any(g => g.GetProperty("id").GetString() == "blast"), "blast game registered");

if(args.Contains("--up-and-down-only")){await UpAndDownNetworkChecks.Run(new Uri(origin));return;}
if(args.Contains("--aim-only")){await NetworkAimChecks.Run(new Uri(origin));return;}
if(args.Contains("--adaptive-only") || args.Contains("--adaptive-peer-only") || args.Contains("--adaptive-malformed-only"))
{await AdaptiveNetworkChecks.Run(new Uri(origin),args.Contains("--adaptive-peer-only"),args.Contains("--adaptive-malformed-only"));return;}
if(args.Contains("--appearance-only")){await LobbyAppearanceCatchup();return;}
if(args.Contains("--peer-only")){await PeerNetworkChecks.Run(new Uri(origin));return;}
if(args.Contains("--peer-reconnect")){await PeerReconnect();return;}
await UiContextNetworkChecks.Run(new Uri(origin));
if(args.Contains("--ui-context-only"))return;
await VersionCompatibilityChecks.Run(new Uri(origin));
if(args.Contains("--version-only"))return;
await QuickPlay();
if (args.Contains("--quick-only")) return;
await PartyNetworkChecks.Run(new Uri(origin));
await AdaptiveNetworkChecks.Run(new Uri(origin));
await AdaptiveNetworkChecks.Run(new Uri(origin),malformedSignals:true);
await PeerNetworkChecks.Run(new Uri(origin));
await PeerReconnect();
await ProductSessions();
await NetworkAimChecks.Run(new Uri(origin));
await UpAndDownNetworkChecks.Run(new Uri(origin));
await LobbyAppearanceCatchup();
if (args.Contains("--client-only")) return;

for (var humans = 2; humans <= 8; humans++)
{
    var clients = new List<Peer>();
    try
    {
        for (var i = 0; i < humans; i++) clients.Add(await Guest($"Smoke {humans}-{i}"));
        var created = await Api(HttpMethod.Post, "/realtime/v1/lobbies", CreateBody(0), clients[0].Token);
        var lobby = created.GetProperty("lobby");
        var lobbyId = lobby.GetProperty("lobbyId").GetString()!;
        var code = lobby.GetProperty("joinCode").GetString()!;
        await clients[0].Connect(created.GetProperty("reservation"), http.BaseAddress!);
        for (var i = 1; i < humans; i++)
        {
            var reservation = await Api(HttpMethod.Post, "/realtime/v1/lobbies/join", new { code }, clients[i].Token);
            await clients[i].Connect(reservation, http.BaseAddress!);
        }
        var welcomes = await Task.WhenAll(clients.Select(p => p.Wait("sys.welcome")));
        Check(welcomes.All(w => w.GetProperty("d").GetProperty("lobby").GetProperty("lobbyId").GetString() == lobbyId), $"{humans} clients share authoritative lobby");
        var snapshots = await Task.WhenAll(clients.Select(p => p.Wait("state", d => d.GetProperty("players").GetArrayLength() == humans)));
        var identities = snapshots[0].GetProperty("d").GetProperty("players").EnumerateArray().Select(p => p.GetProperty("id").GetString()).Order().ToArray();
        Check(snapshots.All(s => s.GetProperty("d").GetProperty("players").EnumerateArray().Select(p => p.GetProperty("id").GetString()).Order().SequenceEqual(identities)), "all peers see the same roster");
        if (humans == 2) await MovementAndReconnect(clients[0], clients[1], lobbyId);
        Console.WriteLine($"PASS {humans} simultaneous human clients, shared roster and state");
    }
    finally { foreach (var p in clients) await p.DisposeAsync(); }
}

await using (var solo = await Guest("Smoke solo"))
{
    var created = await Api(HttpMethod.Post, "/realtime/v1/lobbies", CreateBody(7), solo.Token);
    await solo.Connect(created.GetProperty("reservation"), http.BaseAddress!);
    await solo.Wait("sys.welcome");
    var frame = await solo.Wait("state", d => d.GetProperty("players").GetArrayLength() == 8);
    var players = frame.GetProperty("d").GetProperty("players").EnumerateArray().ToArray();
    Check(players.Count(p => p.GetProperty("bot").GetBoolean()) == 7, "one human and seven server bots");
    var before = players.Where(p => p.GetProperty("bot").GetBoolean()).ToDictionary(p => p.GetProperty("id").GetString()!, p => (p.GetProperty("x").GetSingle(),p.GetProperty("z").GetSingle()));
    await solo.Wait("state", d => d.GetProperty("players").EnumerateArray().Any(p => p.GetProperty("bot").GetBoolean() && before.TryGetValue(p.GetProperty("id").GetString()!, out var pos) && (Math.Abs(p.GetProperty("x").GetSingle()-pos.Item1) > .1 || Math.Abs(p.GetProperty("z").GetSingle()-pos.Item2) > .1)));
    Console.WriteLine("PASS one human + seven server bots; bot simulation advances without client AI");
}
Console.WriteLine($"ALL NETWORK SMOKE CHECKS PASSED ({clock.Elapsed.TotalSeconds:F1}s)");

async Task PeerReconnect()
{
    await using var host=await Guest("Reconnect peer host");
    await using var second=await Guest("Replacement peer host");
    await using var third=await Guest("Remaining peer");
    var created=await Api(HttpMethod.Post,"/realtime/v1/lobbies",new{game="blast-peer",visibility="private",settings=new{bots="0"}},host.Token);
    string id=created.GetProperty("lobby").GetProperty("lobbyId").GetString()!;
    string code=created.GetProperty("lobby").GetProperty("joinCode").GetString()!;
    await host.Connect(created.GetProperty("reservation"),http.BaseAddress!);
    foreach(var peer in new[]{second,third})await peer.Connect(await Api(HttpMethod.Post,"/realtime/v1/lobbies/join",new{code},peer.Token),http.BaseAddress!);
    foreach(var peer in new[]{host,second,third})await peer.Send(new{t="peer.hello",d=new{version=Bomber.Game.Online.OnlineProtocol.Revision}});
    await host.Wait("party.status",d=>d.GetProperty("canStart").GetBoolean());
    await host.Send(new{t="party.start",d=new{}});var playing=await second.Wait("party.status",d=>d.GetProperty("phase").GetString()=="playing");
    var runningDeadline=Stopwatch.StartNew();
    while((await Api(HttpMethod.Get,$"/realtime/v1/lobbies/{id}",token:host.Token)).GetProperty("state").GetString()!="running")
    {
        if(runningDeadline.Elapsed.TotalSeconds>10)throw new TimeoutException("Peer room did not enable reconnect grace");
        await host.Send(new{t="peer.heartbeat",d=new{epoch=playing.GetProperty("d").GetProperty("epoch").GetInt32()}});
        await Task.Delay(100);
    }
    await host.Disconnect();
    await second.Wait("party.status",d=>d.GetProperty("leaderId").GetString()==second.PlayerId&&d.GetProperty("phase").GetString()=="lobby");
    await second.Send(new{t="party.start",d=new{}});await second.Wait("party.status",d=>d.GetProperty("phase").GetString()=="playing");
    await host.Connect(await Api(HttpMethod.Post,$"/realtime/v1/lobbies/{id}/join",new{},host.Token),http.BaseAddress!);
    await host.Send(new{t="peer.hello",d=new{version=Bomber.Game.Online.OnlineProtocol.Revision}});
    var restored=await second.Wait("party.status",d=>d.GetProperty("phase").GetString()=="lobby"&&d.GetProperty("canStart").GetBoolean());
    Check(restored.GetProperty("d").GetProperty("leaderId").GetString()==second.PlayerId,"former host reconnect keeps elected host and resets peer roster safely");
    foreach(var peer in new[]{host,second,third})await Api(HttpMethod.Post,$"/realtime/v1/lobbies/{id}/leave",new{},peer.Token);
    Console.WriteLine("PASS real socket interruption, host replacement and former-host reconnect");
}

async Task QuickPlay()
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150));
    var connected = new List<NetworkSession>();
    var states = new Dictionary<string, JsonElement>();
    try
    {
        var first = await Join("Smoke quick first");
        Check(first.IsLeader && first.Party is { PublicRoom:true, CanStart:false, StartAt:null }, "first queued player waits without a public start override");
        bool rejected = false;
        try { first.SetRoomSettings(new SessionSettings(Bots:7)); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "product client rejects anonymous public rule changes");
        var second = await Join("Smoke quick second");
        Check(first.LobbyId == second.LobbyId, "quick-play clients share matched lobby");
        await Until(() => first.Party?.StartAt is not null && first.Party.StartAt == second.Party?.StartAt);
        long original = first.Party!.StartAt!.Value;
        Check(original - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() is > 55000 and <= 61000, "real server publishes a full one-minute shared deadline");
        first.RequestStart(); second.RequestStart();
        await Task.Delay(1100, timeout.Token); Pump();
        Check(connected.All(c => c.Party is { Phase:"lobby", CanStart:false }) && states.Count == 0, "neither public owner nor guest starts gameplay early");
        var third = await Join("Smoke quick late join");
        await Until(() => first.Party?.StartAt > original && connected.All(c => c.Party?.StartAt == first.Party.StartAt));
        long withThird = first.Party!.StartAt!.Value;
        Check(third.LobbyId == first.LobbyId, "late queued player fills the existing waiting room");
        await Task.Delay(1100, timeout.Token);
        await third.DisposeAsync(); connected.Remove(third);
        await Until(() => first.Party?.StartAt > withThird && first.Party.StartAt == second.Party?.StartAt);
        Check(states.Count == 0, "real three-to-two departure resets the minute without starting a match");
        await first.DisposeAsync(); connected.Remove(first);
        await Until(() => second.Party is { Phase:"lobby", StartAt:null });
        Check(second.IsLeader, "lone remaining player inherits bookkeeping leadership and a cancelled timer");
        var replacement = await Join("Smoke quick replacement");
        await Until(() => second.Party?.StartAt is not null && second.Party.StartAt == replacement.Party?.StartAt);
        long deadline = second.Party!.StartAt!.Value;
        var ids = connected.Select(c => c.PlayerId).Order().ToArray();
        await using var stale = await Guest("Smoke held seat");
        await Until(() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= deadline - 1500, 75);
        var heldReservation = await Api(HttpMethod.Post, $"/realtime/v1/lobbies/{second.LobbyId}/join", new { }, stale.Token);
        await Until(() => connected.All(c => c.Party?.Phase == "playing" && states.TryGetValue(c.PlayerId!, out var state) &&
            state.GetProperty("players").EnumerateArray().Select(p => p.GetProperty("id").GetString()).Order().SequenceEqual(ids)), 75);
        Check(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= deadline, "real Quick Play starts automatically only after the replacement roster's full minute");
        Check(connected.All(c => c.Party is { StartAt:null, Hosting:"cluster" }), "both clients enter the same dedicated match and clear their countdown");
        await stale.Connect(heldReservation, new Uri(origin));
        var rejectedAt = Stopwatch.StartNew();
        bool staleReleased = false;
        while (rejectedAt.Elapsed.TotalSeconds < 5)
        {
            Pump();
            var me = await Api(HttpMethod.Get, "/realtime/v1/me", token:stale.Token);
            if (me.GetProperty("lobbyId").ValueKind == JsonValueKind.Null) { staleReleased = true; break; }
            await Task.Delay(20, timeout.Token);
        }
        Check(staleReleased && connected.All(c => c.Party?.Phase == "playing" && states[c.PlayerId!].GetProperty("players").GetArrayLength() == 2),
            "a socket holding a pre-start reservation is released instead of entering the active public match");
        var late = await Join("Smoke quick next match");
        Check(late.LobbyId != second.LobbyId && late.Party is { Phase:"lobby", StartAt:null }, "new anonymous arrivals queue for another lobby instead of joining an active public match");
        Console.WriteLine("PASS product Quick Play full-minute countdown, churn, permissions, cancellation, automatic start and active-match admission");
    }
    finally { foreach (var session in connected) await session.DisposeAsync(); }

    async Task<NetworkSession> Join(string name)
    {
        var session = await NetworkSession.ConnectAsync(new Uri(origin), name, new SessionSettings(Bots:0), NetworkMode.QuickPlay, cancellationToken:timeout.Token);
        connected.Add(session); return session;
    }
    void Pump()
    {
        foreach (var session in connected)
        {
            session.Advance(1d/60);
            foreach (var frame in session.DrainMessages())
                if (frame.GetProperty("t").GetString() == "state") states[session.PlayerId!] = frame.GetProperty("d").Clone();
            if (session.IsEnded) throw new Exception(session.Status);
        }
    }
    async Task Until(Func<bool> predicate, double seconds = 10)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed.TotalSeconds < seconds)
        {
            Pump(); if (predicate()) return;
            await Task.Delay(20, timeout.Token);
        }
        throw new TimeoutException("Quick Play condition timed out: " + string.Join("; ", connected.Select(c => c.Party)));
    }
}

async Task LobbyAppearanceCatchup()
{
    await using var host=await Guest("Appearance room host");
    var created=await Api(HttpMethod.Post,"/realtime/v1/lobbies",CreateBody(0),host.Token);
    var lobby=created.GetProperty("lobby");var id=lobby.GetProperty("lobbyId").GetString()!;var code=lobby.GetProperty("joinCode").GetString()!;
    await host.Connect(created.GetProperty("reservation"),http.BaseAddress!);
    await host.Send(new {t="appearance",d=new {index=6}});
    await host.Wait("appearance",d=>d.GetProperty("playerId").GetString()==host.PlayerId&&d.GetProperty("index").GetInt32()==6);
    await using var joiner=await Guest("Appearance late joiner");
    var reservation=await Api(HttpMethod.Post,"/realtime/v1/lobbies/join",new {code},joiner.Token);
    await joiner.Connect(reservation,http.BaseAddress!);
    await joiner.Wait("appearance",d=>d.GetProperty("playerId").GetString()==host.PlayerId&&d.GetProperty("index").GetInt32()==6);
    var beforeStart=await Api(HttpMethod.Get,$"/realtime/v1/lobbies/{id}",token:host.Token);
    Check(beforeStart.GetProperty("state").GetString() is "open" or "countdown","late join receives selection before match starts");
    await joiner.Disconnect();await Task.Delay(100);
    var restored=await Api(HttpMethod.Post,$"/realtime/v1/lobbies/{id}/join",new {},joiner.Token);
    await joiner.Connect(restored,http.BaseAddress!);
    await joiner.Wait("appearance",d=>d.GetProperty("playerId").GetString()==host.PlayerId&&d.GetProperty("index").GetInt32()==6);
    var restoredLobby=await Api(HttpMethod.Get,$"/realtime/v1/lobbies/{id}",token:host.Token);
    Check(restoredLobby.GetProperty("state").GetString() is "open" or "countdown","reconnecting lobby client receives selection before match starts");
    Console.WriteLine("PASS private lobby late-join and reconnect receive existing host appearance before match snapshots");
}

async Task ProductSessions()
{
    await using (var local = new LocalSession(new SessionSettings(Bots:7)))
    {
        var initial = local.DrainMessages();
        Check(initial.Any(m => m.GetProperty("t").GetString() == "stage"), "local session initial stage");
        var state = initial.Single(m => m.GetProperty("t").GetString() == "state").GetProperty("d");
        Check(state.GetProperty("players").GetArrayLength() == 8, "local practice total participant count");
        for(var i=0;i<80;i++) local.Advance(1d/30);
        local.SendInput(0,new PlayerInput(1,0,true));
        for(var i=0;i<4;i++) local.Advance(1d/30);
        var next=local.DrainMessages().Last(m => m.GetProperty("t").GetString()=="state").GetProperty("d");
        Check(next.GetProperty("bombs").GetArrayLength() > 0, "local session delivers action input to authority");
    }
    await using var host = await NetworkSession.ConnectAsync(new Uri(origin),"Product host",new SessionSettings(Bots:0),appearance:4);
    Check(!string.IsNullOrEmpty(host.JoinCode), "product client creates a shareable join code");
    await using var joiner = await NetworkSession.ConnectAsync(new Uri(origin),"Product joiner",new SessionSettings(Bots:0), NetworkMode.JoinCode,host.JoinCode,appearance:7);
    Check(host.LobbyId == joiner.LobbyId,"product clients join same lobby");
    await PartyNetworkChecks.Start(host);
    var ready=await ProductState(host,d => d.GetProperty("phase").GetInt32()==1 && d.GetProperty("players").GetArrayLength()==2 &&
        d.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId&&p.GetProperty("appearance").GetInt32()==4) &&
        d.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==joiner.PlayerId&&p.GetProperty("appearance").GetInt32()==7));
    var before=ready.GetProperty("players").EnumerateArray().Single(p => p.GetProperty("id").GetString()==host.PlayerId);
    var beforeX=before.GetProperty("x").GetSingle();
    host.SendInput(0,new PlayerInput(1,0,true));
    await ProductState(joiner,d => d.GetProperty("bombs").GetArrayLength()>0 && d.GetProperty("players").EnumerateArray().Any(p => p.GetProperty("id").GetString()==host.PlayerId && Math.Abs(p.GetProperty("x").GetSingle()-beforeX)>.1));
    host.SendInput(0,default);
    host.SetAppearance(6);
    await ProductState(joiner,d=>d.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==host.PlayerId&&p.GetProperty("appearance").GetInt32()==6));
    Console.WriteLine("PASS product LocalSession practice and NetworkSession create/code join, selected looks, remote movement, bomb action and cosmetic update");
}
async Task<JsonElement> ProductState(NetworkSession session,Func<JsonElement,bool> predicate)
{
    var deadline=Stopwatch.StartNew();
    while(deadline.Elapsed<TimeSpan.FromSeconds(15))
    {
        foreach(var message in session.DrainMessages())
            if(message.GetProperty("t").GetString()=="state" && predicate(message.GetProperty("d"))) return message.GetProperty("d").Clone();
        if(session.IsEnded) throw new Exception(session.Status);
        await Task.Delay(10);
    }
    throw new TimeoutException("Product client waiting for authoritative state: "+session.Status);
}

object CreateBody(int bots) => new { game = "blast", visibility = "private", settings = new Dictionary<string,string> { ["stage"]="rock-garden", ["bots"]=bots.ToString(), ["wins"]="3", ["round-seconds"]="120", ["sudden-death"]="off", ["ghosts"]="on" } };
async Task<Peer> Guest(string name)
{
    var guest = await Api(HttpMethod.Post, "/realtime/v1/guests", new { displayName = name });
    return new Peer(guest.GetProperty("playerId").GetString()!, guest.GetProperty("token").GetString()!);
}
async Task<JsonElement> Api(HttpMethod method, string path, object? body = null, string? token = null)
{
    using var request = new HttpRequestMessage(method,path);
    request.Headers.Add(OnlineProtocol.HeaderName,OnlineProtocol.WireRevision);
    if (token != null) request.Headers.Add("x-player-token",token);
    if (body != null) request.Content = JsonContent.Create(body);
    using var response = await http.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new Exception($"{method} {path}: {(int)response.StatusCode} {text}");
    if(response.StatusCode==System.Net.HttpStatusCode.NoContent)return default;
    using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone();
}
async Task MovementAndReconnect(Peer mover, Peer observer, string lobbyId)
{
    var initial = await mover.Wait("state", d => d.GetProperty("phase").GetInt32() == 1);
    var player = initial.GetProperty("d").GetProperty("players").EnumerateArray().Single(p => p.GetProperty("id").GetString() == mover.PlayerId);
    var x = player.GetProperty("x").GetSingle(); var z = player.GetProperty("z").GetSingle();
    await mover.Send(new {t="appearance",d=new {index=6,slot=1,playerId=observer.PlayerId}});
    await observer.Wait("state",d=>d.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==mover.PlayerId&&p.GetProperty("appearance").GetInt32()==6));
    foreach(var data in new object[]{new {index=-1},new {index=8},new {index="2"},new {index=1.5},new {slot=1}})
        await mover.Send(new {t="appearance",d=data});
    await mover.Send(new {t="input",s=0,d=new {x=0,z=0,a=false,b=false,r=false}});
    var guarded=await observer.Wait("state",d=>d.GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()==mover.PlayerId&&p.GetProperty("ack").ValueKind==JsonValueKind.Number&&p.GetProperty("ack").GetInt64()==0));
    var guardedPlayers=guarded.GetProperty("d").GetProperty("players").EnumerateArray().ToArray();
    Check(guardedPlayers.Single(p=>p.GetProperty("id").GetString()==mover.PlayerId).GetProperty("appearance").GetInt32()==6,"malformed cosmetic packets leave sender appearance intact");
    Check(guardedPlayers.Single(p=>p.GetProperty("id").GetString()==observer.PlayerId).GetProperty("appearance").GetInt32()==1,"sender cannot target another player's cosmetic slot");
    await mover.Send(new { t="input", s=1, d=new { x=1, z=0, a=false, b=false, r=false } });
    await observer.Wait("state", d => d.GetProperty("players").EnumerateArray().Any(p => p.GetProperty("id").GetString() == mover.PlayerId && p.GetProperty("ack").ValueKind == JsonValueKind.Number && p.GetProperty("ack").GetInt64() == 1 && (Math.Abs(p.GetProperty("x").GetSingle()-x) > .1 || Math.Abs(p.GetProperty("z").GetSingle()-z) > .1)));
    await mover.Send(new { t="input", s=2, d=new { x=0,z=0,a=false,b=false,r=false } });
    await mover.Disconnect();
    // HTTP reserves the existing held seat, retaining player identity and kernel slot.
    await Task.Delay(150);
    var reservation = await Api(HttpMethod.Post, $"/realtime/v1/lobbies/{lobbyId}/join", new { }, mover.Token);
    Check(reservation.GetProperty("playerId").GetString() == mover.PlayerId, "reconnect preserves identity");
    await mover.Connect(reservation,http.BaseAddress!);
    var welcome = await mover.Wait("sys.welcome");
    Check(welcome.GetProperty("d").GetProperty("playerId").GetString() == mover.PlayerId, "reconnect welcome identity");
    await mover.Wait("stage");
    var restored=await mover.Wait("state", d => d.GetProperty("players").EnumerateArray().Count(p => p.GetProperty("id").GetString() == mover.PlayerId) == 1);
    Check(restored.GetProperty("d").GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==mover.PlayerId).GetProperty("appearance").GetInt32()==6,"reconnect preserves authoritative appearance without resending it");
    // Garden is enclosed: complete the round with a real self-bomb rather
    // than assuming outward movement can walk through its perimeter.
    await mover.Send(new {t="input",s=3,d=new {x=0,z=0,a=true,b=false,r=false}});
    await observer.Wait("state",d=>d.GetProperty("bombs").GetArrayLength()>0);
    await mover.Send(new {t="input",s=4,d=new {x=0,z=0,a=false,b=false,r=false}});
    var nextRound=await observer.Wait("state",d=>d.GetProperty("round").GetInt32()>=2);
    Check(nextRound.GetProperty("d").GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==mover.PlayerId).GetProperty("appearance").GetInt32()==6,"appearance persists through a real online round reset");
    await mover.Send(new {t="input",s=5,d=new {x=0,z=0,a=false,b=false,r=false}});
    Console.WriteLine("PASS sequenced movement, validated own-player cosmetics, reconnect persistence and online round reset");
}
void Check(bool condition,string description) { if (!condition) throw new Exception("FAIL " + description); }

sealed class Peer(string playerId,string token) : IAsyncDisposable
{
    public string PlayerId { get; } = playerId;
    public string Token { get; } = token;
    ClientWebSocket? socket;
    CancellationTokenSource? stop;
    Task? reader;
    readonly ConcurrentQueue<JsonElement> inbox = new();
    Exception? readError;
    public async Task Connect(JsonElement reservation, Uri origin)
    {
        while (inbox.TryDequeue(out _)) { }
        readError=null; socket=new(); stop=new();
        socket.Options.SetRequestHeader(OnlineProtocol.HeaderName,OnlineProtocol.WireRevision);
        var url=new Uri(origin,reservation.GetProperty("connectUrl").GetString()!);
        var builder=new UriBuilder(url) { Scheme=url.Scheme=="https" ? "wss" : "ws" };
        await socket.ConnectAsync(builder.Uri,stop.Token);
        reader=Read(socket,stop.Token);
    }
    async Task Read(ClientWebSocket ws,CancellationToken ct)
    {
        try
        {
            var buffer=new byte[65536];
            while (!ct.IsCancellationRequested)
            {
                using var stream=new MemoryStream(); WebSocketReceiveResult result;
                do { result=await ws.ReceiveAsync(new ArraySegment<byte>(buffer),ct); if(result.MessageType==WebSocketMessageType.Close) return; stream.Write(buffer,0,result.Count); } while(!result.EndOfMessage);
                using var doc=JsonDocument.Parse(stream.ToArray()); inbox.Enqueue(doc.RootElement.Clone());
                while(inbox.Count>512) inbox.TryDequeue(out _);
            }
        }
        catch(Exception e) when(ct.IsCancellationRequested) { _ = e; }
        catch(Exception e) { readError=e; }
    }
    public async Task<JsonElement> Wait(string type,Func<JsonElement,bool>? predicate=null)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            while(inbox.TryDequeue(out var frame))
            {
                if(frame.GetProperty("t").GetString()=="sys.error") throw new Exception("Kernel error: " + frame);
                if(frame.GetProperty("t").GetString()==type && (predicate==null || predicate(frame.GetProperty("d")))) return frame;
            }
            if(readError!=null) throw new Exception("WebSocket receive failed",readError);
            await Task.Delay(10);
        }
        throw new TimeoutException($"Player {PlayerId}: waiting for {type}");
    }
    public Task Send(object frame) => socket!.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame))),WebSocketMessageType.Text,true,CancellationToken.None);
    public async Task Disconnect()
    {
        stop?.Cancel();socket?.Abort();
        if(reader!=null) await reader;
        socket?.Dispose();stop?.Dispose();socket=null;stop=null;
    }
    public ValueTask DisposeAsync() => new(Disconnect());
}

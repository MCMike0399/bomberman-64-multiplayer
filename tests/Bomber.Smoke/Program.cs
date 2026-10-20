using System.Collections.Concurrent;
using System.Diagnostics;
using Bomber.Client;
using Bomber.Game;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var origin = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "http://127.0.0.1:18496";
using var http = new HttpClient { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(10) };
var clock = Stopwatch.StartNew();
var games = await Api(HttpMethod.Get, "/realtime/v1/games");
Check(games.GetProperty("value").EnumerateArray().Any(g => g.GetProperty("id").GetString() == "blast"), "blast game registered");

if(args.Contains("--appearance-only")){await LobbyAppearanceCatchup();return;}
await QuickPlay();
if (args.Contains("--quick-only")) return;
await ProductSessions();
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

async Task QuickPlay()
{
    using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
    {
        try
        {
            await using var unexpected = await NetworkSession.ConnectAsync(new Uri(origin),"Smoke cancel queue",new SessionSettings(Bots:0),NetworkMode.QuickPlay,cancellationToken:cancel.Token);
            throw new Exception("A lone quick-play client unexpectedly matched before cancellation.");
        }
        catch(OperationCanceledException) when(cancel.IsCancellationRequested) { }
    }
    // ConnectAsync waits for matching, so both product clients must initialize concurrently.
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var firstTask=NetworkSession.ConnectAsync(new Uri(origin),"Smoke quick first",new SessionSettings(Bots:0),NetworkMode.QuickPlay,cancellationToken:timeout.Token);
    var secondTask=NetworkSession.ConnectAsync(new Uri(origin),"Smoke quick second",new SessionSettings(Bots:0),NetworkMode.QuickPlay,cancellationToken:timeout.Token);
    var connected=await Task.WhenAll(firstTask,secondTask);
    await using var first=connected[0];
    await using var second=connected[1];
    Check(first.LobbyId==second.LobbyId,"quick-play clients share matched lobby");
    var ids=new[] { first.PlayerId, second.PlayerId }.Order().ToArray();
    foreach(var session in connected)
        await ProductState(session,d => d.GetProperty("players").GetArrayLength()==2 && d.GetProperty("players").EnumerateArray().Select(p => p.GetProperty("id").GetString()).Order().SequenceEqual(ids));
    Console.WriteLine("PASS product quick-play cancellation, two-client matchmaking and shared authoritative roster");
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
    if (token != null) request.Headers.Add("x-player-token",token);
    if (body != null) request.Content = JsonContent.Create(body);
    using var response = await http.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new Exception($"{method} {path}: {(int)response.StatusCode} {text}");
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
    await mover.Send(new {t="input",s=3,d=new {x=-1,z=0,a=false,b=false,r=false}});
    var nextRound=await observer.Wait("state",d=>d.GetProperty("round").GetInt32()>=2);
    Check(nextRound.GetProperty("d").GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetString()==mover.PlayerId).GetProperty("appearance").GetInt32()==6,"appearance persists through a real online round reset");
    await mover.Send(new {t="input",s=4,d=new {x=0,z=0,a=false,b=false,r=false}});
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

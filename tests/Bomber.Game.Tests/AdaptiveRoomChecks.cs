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
        var reconnectMatch = new PeerMatch("reconnect", context.Settings, 64, context.Players, new Dictionary<string,int>(), (_,_,_)=>{});
        JsonElement Input(long sequence) => JsonSerializer.SerializeToElement(new { t="input", s=sequence, d=new { x=0,z=0,a=false,b=false,r=false } });
        reconnectMatch.Receive(guest.PlayerId, Input(100));
        reconnectMatch.ReleaseInput(guest.PlayerId);
        reconnectMatch.Reconnect(guest.PlayerId);
        reconnectMatch.Receive(guest.PlayerId, Input(1));
        Check(reconnectMatch.LastSequence(guest.PlayerId)==1,"a retained-seat reconnect accepts the new connection's reset input sequence");
        var publicContext = new RoomContext();
        var publicGame = new PublicAdaptiveRoomGame().CreateSession(new("public", "blast-online", publicContext.Settings, 64));
        foreach (var player in new[] { publicContext.Add("one", 0), publicContext.Add("two", 1) }) publicGame.OnPlayerJoined(publicContext, player);
        publicGame.OnTick(publicContext);
        publicContext.Tick = 120;
        publicGame.OnPlayerJoined(publicContext, publicContext.Add("three", 2)); publicGame.OnTick(publicContext);
        publicContext.Tick = 240; publicGame.OnTick(publicContext);
        Check(publicContext.Status.GetProperty("phase").GetString()=="playing", "later public arrivals do not indefinitely restart the automatic countdown");
        bool rejected = false;
        try { new AdaptiveRoomGame().CreateSession(new("invalid", "blast-room", new Dictionary<string,string>{{"stage","unsupported"}},64)); }
        catch(ArgumentException) { rejected = true; }
        Check(rejected, "invalid initial room rules are rejected before a player authority receives them");
        var churn = new RoomContext();
        var churnRoom = new AdaptiveRoomGame().CreateSession(new("churn", "blast-room", churn.Settings, 64));
        var ca=churn.Add("a",0);var cb=churn.Add("b",1);var cc=churn.Add("c",2);
        foreach(var p in churn.Players)churnRoom.OnPlayerJoined(churn,p);
        churn.Connected.Remove(cb.PlayerId);churnRoom.OnPlayerLeft(churn,cb,LeaveReason.Left);
        var cd=churn.Add("d",1);churnRoom.OnPlayerJoined(churn,cd);
        churn.Connected.Remove(ca.PlayerId);churnRoom.OnPlayerLeft(churn,ca,LeaveReason.Left);
        Check(churn.Status.GetProperty("leaderId").GetString()=="c","owner turnover preserves join order after dictionary slot reuse");
        churnRoom.OnMessage(churn,cc,new("party.settings",JsonSerializer.SerializeToElement(new{wins="3"}),null,churn.Now));
        Check(churn.Status.GetProperty("settings").GetProperty("wins").GetString()=="3","replacement owner can edit dedicated room rules");
        churnRoom.OnMessage(churn,cc,new("party.start",JsonSerializer.SerializeToElement(new{}),null,churn.Now));
        Check(churn.Status.GetProperty("phase").GetString()=="playing","replacement owner can start dedicated authority");
        var cancelled = new RoomContext();cancelled.Limits["simulation"]=0;
        var cancelledRoom = new AdaptiveRoomGame().CreateSession(new("cancelled", "blast-room", cancelled.Settings,64));
        var qa=cancelled.Add("a",0);var qb=cancelled.Add("b",1);cancelled.Add("c",2);
        foreach(var p in cancelled.Players)cancelledRoom.OnPlayerJoined(cancelled,p);
        cancelledRoom.OnMessage(cancelled,qa,new("party.start",JsonSerializer.SerializeToElement(new{}),null,cancelled.Now));
        cancelled.Connected.Remove(qb.PlayerId);cancelledRoom.OnPlayerLeft(cancelled,qb,LeaveReason.Left);
        cancelled.Limits["simulation"]=1;cancelled.Tick=30;cancelledRoom.OnTick(cancelled);
        Check(cancelled.Status.GetProperty("phase").GetString()=="lobby"&&cancelled.Claims.Count==0,"departure cancels a pending start before later capacity becomes free");
        var quiet = new RoomContext(); quiet.Limits["simulation"] = 0;
        var quietRoom = new AdaptiveRoomGame().CreateSession(new("quiet", "blast-room", quiet.Settings,64));
        var qo=quiet.Add("owner",0);quiet.Add("guest",1);
        foreach(var p in quiet.Players)quietRoom.OnPlayerJoined(quiet,p);
        quietRoom.OnMessage(quiet,qo,new("party.start",JsonSerializer.SerializeToElement(new{}),null,quiet.Now));
        int statuses=quiet.Frames.Count(f=>f.Type=="party.status");
        for(int i=1;i<=10;i++){quiet.Tick=i*15;quietRoom.OnTick(quiet);}
        Check(quiet.Frames.Count(f=>f.Type=="party.status")==statuses,"unchanged capacity retries do not repeatedly rebuild lobby presentation");
        quietRoom.OnPlayerConnectionChanged(quiet,qo,true);
        int connection = quiet.Status.GetProperty("players").EnumerateArray().First(p=>p.GetProperty("playerId").GetString()==qo.PlayerId).GetProperty("connection").GetInt32();
        statuses=quiet.Frames.Count(f=>f.Type=="party.status");
        quietRoom.OnPlayerConnectionChanged(quiet,qo,true);
        Check(quiet.Frames.Count(f=>f.Type=="party.status")>statuses&&quiet.Status.GetProperty("players").EnumerateArray().First(p=>p.GetProperty("playerId").GetString()==qo.PlayerId).GetProperty("connection").GetInt32()>connection,
            "replacement sockets force fresh status even without an observed disconnected state");
        var relay = new RoomContext(); relay.Limits["simulation"] = 0;
        var relayRoom = new AdaptiveRoomGame().CreateSession(new("relay", "blast-room", relay.Settings, 64));
        foreach (var p in Enumerable.Range(0,8).Select(i=>relay.Add("r"+i,i)).ToArray())
        { relayRoom.OnPlayerJoined(relay,p); Relay(p,"peer.hello",new{version=OnlineProtocol.Revision}); }
        int relayEpoch=relay.Status.GetProperty("epoch").GetInt32();
        foreach(var p in relay.Players) Relay(p,"peer.quality",new {epoch=relayEpoch,serverRtt=120d,frameGap=16d,
            links=relay.Players.Where(other=>other!=p).Select(other=>new{id=other.PlayerId,rtt=20d,loss=0d,direct=false,samples=5})});
        Relay(relay.Players[0],"party.start",new{});
        var authority=relay.Players.Single(p=>p.PlayerId==relay.Status.GetProperty("hostId").GetString());
        relayEpoch=relay.Status.GetProperty("epoch").GetInt32(); relay.Frames.Clear();
        for(int tick=0;tick<15;tick++) foreach(var p in relay.Players.Where(p=>p!=authority))
            Relay(authority,"peer.state",new{epoch=relayEpoch,to=p.PlayerId,payload=new{t="state",d=new{terrain=new string('x',40000)}}});
        Check(relay.Frames.Count(f=>f.Type=="peer.state")==105,"adaptive authority relays all seven recipients at the normal large-stage cadence");
        void Relay(RealtimePlayer p,string type,object data)=>relayRoom.OnMessage(relay,p,new(type,JsonSerializer.SerializeToElement(data),null,relay.Now));
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

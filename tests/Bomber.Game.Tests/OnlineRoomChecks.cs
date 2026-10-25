using System.Text.Json;
using Bomber.Game;
using Burbuja.Realtime.Abstractions;

internal static class OnlineRoomChecks
{
    public static int Run()
    {
        int passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); passed++; }
        var legacy=new Context();var legacyRoom=new PeerRoomSession(new("legacy","blast-peer",legacy.Settings,64));
        var legacyHost=legacy.Add("old-host",0);var legacyGuest=legacy.Add("old-guest",1);
        foreach(var p in new[]{legacyHost,legacyGuest})
        {legacyRoom.OnPlayerJoined(legacy,p);legacyRoom.OnMessage(legacy,p,new("peer.hello",JsonSerializer.SerializeToElement(new{version=3}),null,legacy.Now));}
        legacyRoom.OnMessage(legacy,legacyHost,new("party.start",JsonSerializer.SerializeToElement(new{}),null,legacy.Now));
        Check(legacy.Frames.Last(f=>f.Type=="party.status").Data.GetProperty("phase").GetString()=="lobby","v0.29 peers cannot start the changed bomb-loadout protocol");
        legacyRoom.OnMessage(legacy,legacyHost,new("peer.hello",JsonSerializer.SerializeToElement(new{version=OnlineProtocol.Revision}),null,legacy.Now));
        Check(!legacy.Frames.Last(f=>f.Type=="party.status").Data.GetProperty("canStart").GetBoolean(),"mixed old/new peers cannot start");
        Check(legacy.Frames.Last(f=>f.Type=="party.status").Data.GetProperty("notice").GetString()!.Contains("Update"),"incompatible peers receive an update instruction");
        legacyRoom.OnMessage(legacy,legacyGuest,new("peer.hello",JsonSerializer.SerializeToElement(new{version=OnlineProtocol.Revision}),null,legacy.Now));
        Check(legacy.Frames.Last(f=>f.Type=="party.status").Data.GetProperty("canStart").GetBoolean(),"matching updated peers can start");
        var c = new Context(); var room = new PeerRoomSession(new("peer", "blast-peer", c.Settings, 64));
        var host = c.Add("host", 0); room.OnPlayerJoined(c, host);
        Send(host,"peer.hello",new{version=OnlineProtocol.Revision});
        Send(host, "party.start", new { });
        Check(Status().GetProperty("phase").GetString() == "lobby", "seven CPUs never satisfy two-human peer minimum");
        var second = c.Add("second", 3); room.OnPlayerJoined(c, second);
        var third = c.Add("third", 1); room.OnPlayerJoined(c, third);
        Send(host,"party.start",new{});
        Check(Status().GetProperty("phase").GetString()=="lobby","clients must negotiate peer protocol before starting");
        foreach(var p in new[]{host,second,third})Send(p,"peer.hello",new{version=OnlineProtocol.Revision});
        Check(Status().GetProperty("leaderId").GetString() == "host", "first peer owns room");
        Send(second, "party.start", new { }); Check(Status().GetProperty("phase").GetString() == "lobby", "guest cannot start");
        Send(host, "party.start", new { }); Check(Status().GetProperty("phase").GetString() == "playing", "host starts with two humans");
        int epoch = Status().GetProperty("epoch").GetInt32();
        int before = c.Frames.Count;
        Send(second, "peer.frame", new {epoch, to="third", payload=new {t="input",d=new {}}});
        Send(second, "peer.frame", new {epoch, to="host", payload=new {t="state",d=new {}}});
        Send(host, "peer.frame", new {epoch=epoch-1, to="second", payload=new {t="state",d=new {}}});
        Check(c.Frames.Count == before, "non-host routes, forged authority and old epochs rejected");
        Send(second, "peer.frame", new {epoch, to="host", from="host", payload=new {t="input",d=new {}}});
        Check(c.Frames.Last().Recipient == "host" && c.Frames.Last().Data.GetProperty("from").GetString() == "second", "relay uses authenticated sender");
        c.Connected.Remove("host"); room.OnPlayerConnectionChanged(c, host, false);
        Check(Status().GetProperty("leaderId").GetString() == "second", "takeover uses join order instead of reused slot order");
        Check(Status().GetProperty("phase").GetString() == "lobby" && Status().GetProperty("epoch").GetInt32() > epoch, "host loss invalidates interrupted match");
        Send(second,"party.start",new{});
        c.Connected.Add("host"); room.OnPlayerConnectionChanged(c, host, true);
        Check(Status().GetProperty("phase").GetString()=="lobby","reconnecting into a new peer match resets its roster safely");
        Send(host,"peer.hello",new{version=OnlineProtocol.Revision});
        Check(Status().GetProperty("leaderId").GetString() == "second", "former host reconnect cannot reclaim authority");
        Send(second, "party.start", new { }); epoch = Status().GetProperty("epoch").GetInt32();
        c.Tick = 210; room.OnTick(c);
        Check(Status().GetProperty("leaderId").GetString() == "host" && Status().GetProperty("phase").GetString() == "lobby", "stalled authority lease elects oldest eligible connected peer");
        Send(second, "peer.heartbeat", new {epoch});
        Check(Status().GetProperty("leaderId").GetString() == "host", "stale heartbeat cannot reverse election");
        var publicRoom = new PartySession(new("random","blast-lobby",c.Settings,64),2);
        var alone = new Context(); var solo = alone.Add("solo",0); publicRoom.OnPlayerJoined(alone,solo);
        alone.State = LobbyState.Running; publicRoom.OnMatchStarted(alone);
        publicRoom.OnMessage(alone,solo,new("party.start",JsonSerializer.SerializeToElement(new {}),null,alone.Now));
        Check(!alone.Frames.Last(f=>f.Type=="party.status").Data.GetProperty("canStart").GetBoolean(),"public room also requires two humans despite CPUs");
        var strategy = new WaitingRoomMatchmaker(); var rules = new PublicLobbyGame().Rules;
        var tickets = new[] { new MatchTicket("t2","p2","blast-lobby",null,0,DateTimeOffset.UnixEpoch.AddSeconds(1)),new MatchTicket("t1","p1","blast-lobby",null,0,DateTimeOffset.UnixEpoch) };
        var decisions = strategy.Decide(new("blast-lobby",rules,tickets,[],DateTimeOffset.UtcNow));
        Check(decisions.Single().TicketIds.SequenceEqual(new[]{"t1","t2"}),"new waiting-room reservations preserve FIFO");
        Check(strategy.Decide(new("blast-lobby",rules,tickets.Take(1).ToArray(),[],DateTimeOffset.UtcNow)).Count==1,"singleton queue can enter waiting room");
        decisions = strategy.Decide(new("blast-lobby",rules,tickets,[new("open","global",LobbyState.Open,1,0)],DateTimeOffset.UtcNow));
        Check(decisions[0] is BackfillDecision && decisions.SelectMany(d=>d.TicketIds).Distinct().Count()==2,"backfill and overflow never assign a ticket twice");
        var incompatible=c.Add("old-client",6);room.OnPlayerJoined(c,incompatible);
        Send(incompatible,"peer.hello",new{version=99});
        Check(!Status().GetProperty("canStart").GetBoolean(),"unsupported peer protocol blocks a partially compatible roster");
        c.Tick+=330;room.OnTick(c);
        Check(c.Kicked.Contains("old-client"),"unsupported clients are removed after bounded negotiation timeout");
        Send(host,"peer.frame",new{epoch="bad",to="second",payload=new{}});
        Send(host,"appearance",new{index="bad"});
        Check(Status().GetProperty("phase").GetString()=="lobby","malformed control scalars cannot unwind coordinator");
        var idle=new Context();var idleRoom=new PeerRoomSession(new("idle","blast-peer",idle.Settings,64));
        var idleHost=idle.Add("idle-host",0);var idlePeer=idle.Add("idle-peer",1);
        foreach(var p in new[]{idleHost,idlePeer})
        {idleRoom.OnPlayerJoined(idle,p);idleRoom.OnMessage(idle,p,new("peer.hello",JsonSerializer.SerializeToElement(new{version=OnlineProtocol.Revision}),null,idle.Now));}
        idle.Tick=210;idleRoom.OnTick(idle);
        var idleStatus=idle.Frames.Last(f=>f.Type=="party.status").Data;
        Check(idleStatus.GetProperty("leaderId").GetString()=="idle-peer","stalled lobby host cannot block start indefinitely");
        idleRoom.OnMessage(idle,idleHost,new("peer.heartbeat",JsonSerializer.SerializeToElement(new{epoch=idleStatus.GetProperty("epoch").GetInt32()}),null,idle.Now));
        idleRoom.OnMessage(idle,idlePeer,new("party.start",JsonSerializer.SerializeToElement(new{}),null,idle.Now));
        idle.Tick=420;idleRoom.OnTick(idle);
        Check(idle.Frames.Last(f=>f.Type=="party.status").Data.GetProperty("leaderId").GetString()=="idle-host","a responsive former host becomes eligible without stealing current leadership");
        // One authority sends each snapshot separately to seven authenticated relay recipients.
        var full = new Context(); var fullRoom = new PeerRoomSession(new("full", "blast-peer", full.Settings, 64));
        var roster = Enumerable.Range(0, 8).Select(i => full.Add("relay-" + i, i)).ToArray();
        foreach (var p in roster)
        { fullRoom.OnPlayerJoined(full, p); FullSend(p, "peer.hello", new { version = OnlineProtocol.Revision }); }
        FullSend(roster[0], "party.start", new { });
        int fullEpoch = full.Frames.Last(f => f.Type == "party.status").Data.GetProperty("epoch").GetInt32();
        full.Frames.Clear();
        var state = new { t = "state", d = new { terrain = new string('x', 40000) } };
        for (int tick = 0; tick < 15; tick++)
            foreach (var peer in roster.Skip(1))
                FullSend(roster[0], "peer.state", new { epoch = fullEpoch, to = peer.PlayerId, payload = state });
        Check(full.Frames.Count(f => f.Type == "peer.state") == 105,
            "eight-player relay sustains a full second of ordinary large-stage snapshots without periodic starvation");
        for (int i = 0; i < 400; i++)
            FullSend(roster[0], "peer.state", new { epoch = fullEpoch, to = roster[1].PlayerId, payload = state });
        Check(full.Frames.Count < 505, "authority relay flood remains bounded");
        full.Tick = 30; int delivered = full.Frames.Count;
        FullSend(roster[0], "peer.state", new { epoch = fullEpoch, to = roster[1].PlayerId, payload = state });
        Check(full.Frames.Count == delivered + 1, "relay allowance recovers in the next window");
        void FullSend(RealtimePlayer p, string type, object data) => fullRoom.OnMessage(full, p,
            new(type, JsonSerializer.SerializeToElement(data), null, full.Now));
        Console.WriteLine($"PASS {passed} online room authority checks"); return passed;
        void Send(RealtimePlayer player,string type,object data)=>room.OnMessage(c,player,new(type,JsonSerializer.SerializeToElement(data),null,c.Now));
        JsonElement Status()=>c.Frames.Last(f=>f.Type=="party.status").Data;
    }
    private sealed class Context : ISessionContext
    {
        public string LobbyId=>"peer"; public string GameId=>"blast-peer";
        public LobbyState State{get;set;}=LobbyState.Running;
        public IReadOnlyDictionary<string,string> Settings{get;}=new MatchOptions{BotPlayers=7}.ToSettings();
        private readonly List<RealtimePlayer> _players=[];
        public IReadOnlyList<RealtimePlayer> Players=>_players;
        public HashSet<string> Connected{get;}=[];
        public RealtimePlayer Add(string id,int slot){var p=new RealtimePlayer(id,id,slot,new Dictionary<string,string>());_players.Add(p);Connected.Add(id);return p;}
        public bool IsConnected(string id)=>Connected.Contains(id);public long? LastSequence(string id)=>null;
        public long Tick{get;set;}public TimeSpan TickDelta=>TimeSpan.FromSeconds(1d/30);
        public DateTimeOffset Now=>DateTimeOffset.UnixEpoch+Elapsed;public TimeSpan Elapsed=>TimeSpan.FromSeconds(Tick/30d);
        public Random Random{get;}=new(64);
        public List<(string Recipient,string Type,JsonElement Data)> Frames{get;}=[];
        public void Send<T>(string id,string type,T data)=>Frames.Add((id,type,JsonSerializer.SerializeToElement(data,RealtimeJson.Default)));
        public void Broadcast<T>(string type,T data,string? exceptPlayerId=null)=>Send("*",type,data);
        public void SendBinary(string id,string type,ReadOnlySpan<byte> data)=>throw new NotSupportedException();
        public void BroadcastBinary(string type,ReadOnlySpan<byte> data,string? exceptPlayerId=null)=>throw new NotSupportedException();
        public List<string> Kicked{get;}=[];
        public void EndMatch(MatchResult result)=>throw new NotSupportedException();public void Kick(string id,string reason)=>Kicked.Add(id);
    }
}

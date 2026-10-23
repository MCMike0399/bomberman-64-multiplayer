using System.Text.Json;
using Bomber.Game;
using Burbuja.Realtime.Abstractions;

internal static class PartySessionChecks
{
    public static int Run()
    {
        var alone = new Room(0);var loneHost=alone.Add("solo",0);
        var waiting=new BomberRealtimeGame().CreateSession(new("empty-party","blast",alone.Settings,64));
        waiting.OnPlayerJoined(alone,loneHost);alone.State=LobbyState.Running;waiting.OnMatchStarted(alone);
        waiting.OnMessage(alone,loneHost,new("party.start",JsonSerializer.SerializeToElement(new {}),null,alone.Now));
        for(int i=0;i<100;i++){alone.Tick++;waiting.OnTick(alone);}
        Check(!alone.Messages.Any(m=>m.Type=="state"),"one human without CPU cannot start an invalid match");
        var c = new Room();
        var game = new BomberRealtimeGame().CreateSession(new("party", "blast", c.Settings, 64));
        var host = c.Add("host", 0);
        game.OnPlayerJoined(c, host);
        c.State = LobbyState.Running; game.OnMatchStarted(c); Tick(180);
        Check(!c.Messages.Any(m => m.Type == "state"), "private room waits for leader even with bots");
        var peer = c.Add("peer", 1); game.OnPlayerJoined(c, peer);
        Send(peer, "party.start", new {leaderId="host"}); Tick(4);
        Check(!c.Messages.Any(m => m.Type == "state"), "guest cannot spoof leader start");
        var changed=new Dictionary<string,string>{{"stage","up-and-down"},{"bots","0"},{"wins","1"},{"round-seconds","180"},{"ghosts","off"},{"sudden-death","off"},{"bot-difficulty","easy"}};
        Send(peer,"party.settings",changed);
        Check(Last("party.status").GetProperty("settings").GetProperty("stage").GetString()=="rock-garden","guest cannot change room rules");
        Send(host,"party.settings",changed);
        Check(Last("party.status").GetProperty("settings").GetProperty("stage").GetString()=="up-and-down","leader edits room without changing identity");
        changed["wins"]="99";Send(host,"party.settings",changed);
        Check(Last("party.status").GetProperty("settings").GetProperty("wins").GetString()=="1","invalid rules are rejected atomically");
        Send(host,"party.settings",c.Settings);
        Send(host,"party.settings",new Dictionary<string,string>{{"round-seconds","90"}});
        Send(host, "appearance", new {index=6}); Send(host, "appearance", new {index="invalid"}); Send(host, "party.start", new {}); Tick(70);
        Check(c.Messages.Any(m => m.Type == "state"), "leader starts simulation");
        Check(Last("stage").GetProperty("roundSeconds").GetInt32()==90,"next match uses saved room rules");
        Send(host,"party.settings",new Dictionary<string,string>{{"round-seconds","300"}});
        Check(Last("party.status").GetProperty("settings").GetProperty("round-seconds").GetString()=="90","leader cannot edit rules during a match");
        Check(Last("state").GetProperty("players").EnumerateArray().Any(p=>p.GetProperty("id").GetString()=="host"&&p.GetProperty("appearance").GetInt32()==6), "lobby suit enters match");
        int match=Last("party.status").GetProperty("match").GetInt32();
        Send(host,"party.start",new {});
        Check(Last("party.status").GetProperty("match").GetInt32()==match,"duplicate start cannot reset match");
        Send(host,"input",new {x=0,z=0,a=true}); Send(peer,"input",new {x=0,z=0,a=true}); Tick(2);
        Send(host,"input",new {x=0,z=0,a=false}); Send(peer,"input",new {x=0,z=0,a=false});
        for(int i=0;i<18000&&!c.Messages.Any(m=>m.Type=="party.result");i++)Tick(1);
        Check(c.Messages.Any(m=>m.Type=="party.result")&&c.Result==null,"completed match retains room");
        Tick(180); Check(Last("party.status").GetProperty("phase").GetString()=="lobby","results return to lobby");
        int frames=c.Messages.Count(m=>m.Type=="state");Tick(180);
        Check(frames==c.Messages.Count(m=>m.Type=="state"),"lobby does not replay automatically");
        Send(host,"party.start",new {});Tick(4);
        Check(Last("party.status").GetProperty("match").GetInt32()==match+1,"same room supports leader rematch");
        Check(Last("state").GetProperty("players").EnumerateArray().All(p=>p.GetProperty("wins").GetInt32()==0),"rematch clears scores");
        c.Connected.Remove("host");game.OnPlayerConnectionChanged(c,host,false);
        Check(Last("party.status").GetProperty("leaderId").GetString()=="peer","disconnect transfers leadership");
        c.Connected.Add("host");game.OnPlayerConnectionChanged(c,host,true);
        Check(Last("party.status").GetProperty("leaderId").GetString()=="peer","reconnect preserves elected leader");
        Console.WriteLine("PASS 18 persistent party authority checks");return 18;
        void Tick(int ticks){for(int i=0;i<ticks;i++){c.Tick++;game.OnTick(c);}}
        void Send(RealtimePlayer p,string type,object data)=>game.OnMessage(c,p,new(type,JsonSerializer.SerializeToElement(data),null,c.Now));
        JsonElement Last(string type)=>c.Messages.Last(m=>m.Type==type).Data;
        static void Check(bool ok,string text){if(!ok)throw new Exception("FAIL: "+text);}
    }
    private sealed class Room(int bots=1) : ISessionContext
    {
        public string LobbyId=>"party";public string GameId=>"blast";
        public LobbyState State{get;set;}=LobbyState.Open;
        public IReadOnlyDictionary<string,string> Settings{get;}=new Dictionary<string,string>{{"party","on"},{"bots",bots.ToString()},{"wins","1"},{"stage","rock-garden"},{"round-seconds","60"}};
        private readonly List<RealtimePlayer> _players=[];
        public IReadOnlyList<RealtimePlayer> Players=>_players;
        public HashSet<string> Connected{get;}=[];
        public RealtimePlayer Add(string id,int slot){var p=new RealtimePlayer(id,id,slot,new Dictionary<string,string>());_players.Add(p);Connected.Add(id);return p;}
        public bool IsConnected(string id)=>Connected.Contains(id);public long? LastSequence(string id)=>null;
        public long Tick{get;set;}public TimeSpan TickDelta=>TimeSpan.FromSeconds(1d/30);
        public DateTimeOffset Now=>DateTimeOffset.UnixEpoch+Elapsed;public TimeSpan Elapsed=>TimeSpan.FromSeconds(Tick/30d);
        public Random Random{get;}=new(64);public MatchResult? Result{get;private set;}
        public List<(string Type,JsonElement Data)> Messages{get;}=[];
        public void Send<T>(string id,string type,T data)=>Messages.Add((type,JsonSerializer.SerializeToElement(data)));
        public void Broadcast<T>(string type,T data,string? exceptPlayerId=null)=>Send("",type,data);
        public void SendBinary(string id,string type,ReadOnlySpan<byte> data)=>throw new NotSupportedException();
        public void BroadcastBinary(string type,ReadOnlySpan<byte> data,string? exceptPlayerId=null)=>throw new NotSupportedException();
        public void EndMatch(MatchResult result)=>Result=result;public void Kick(string id,string reason)=>throw new NotSupportedException();
    }
}

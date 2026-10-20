using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;
using Burbuja.Realtime.Abstractions;

/// <summary>Real session callbacks with a deterministic arena; cosmetic tests send ordinary messages.</summary>
sealed class AppearanceFixture : ISessionContext
{
    public BlastSession Session {get;}
    public List<(string Recipient,string Type,JsonElement Data)> Sent {get;}=[];
    public JsonElement Snapshot {get;private set;}
    public string LobbyId=>"appearance-fixture";
    public string GameId=>"blast";
    public LobbyState State {get;private set;}=LobbyState.Open;
    public IReadOnlyDictionary<string,string> Settings {get;}=new MatchOptions {HumanPlayers=2,BotPlayers=0,Wins=5,SuddenDeath=false}.ToSettings();
    public IReadOnlyList<RealtimePlayer> Players {get;}
    public bool IsConnected(string id)=>true;
    public long? LastSequence(string id)=>null;
    public long Tick {get;private set;}
    public TimeSpan TickDelta=>TimeSpan.FromSeconds(1d/30);
    public DateTimeOffset Now=>DateTimeOffset.UnixEpoch+Elapsed;
    public TimeSpan Elapsed=>TimeSpan.FromSeconds(Tick/30d);
    public Random Random {get;}=new(64);
    public AppearanceFixture(bool startMatch=true,StageLayout? customStage=null,int humans=2)
    {
        Players=Enumerable.Range(0,humans).Select(i=>new RealtimePlayer("fixture-"+i,"Player "+i,i,new Dictionary<string,string>())).ToArray();
        var cells=new CellDef[9,9];for(var x=0;x<9;x++)for(var z=0;z<9;z++)cells[x,z]=x==0?CellDef.Water:CellDef.Floor0;
        var stage=new StageLayout {Id="fixture",Width=9,Height=9,Cells=cells,Spawns=[(1,3),(7,3)],SoftBlockDensity=0};
        Session=new BlastSession(new GameSessionOptions(LobbyId,GameId,Settings,64),customStage??stage);
        foreach(var player in Players)Session.OnPlayerJoined(this,player);
        if(startMatch){State=LobbyState.Running;Session.OnMatchStarted(this);Step(2);}
    }
    public void Message(int sender,string json,string type="appearance")
    {using var doc=JsonDocument.Parse(json);Session.OnMessage(this,Players[sender],new InboundMessage(type,doc.RootElement.Clone(),null,Now));}
    public void Step(int ticks){for(var i=0;i<ticks;i++){Tick++;Session.OnTick(this);}}
    public void Send<T>(string playerId,string type,T payload) => Sent.Add((playerId,type,JsonSerializer.SerializeToElement(payload,RealtimeJson.Default)));
    public void Broadcast<T>(string type,T payload,string? exceptPlayerId=null)
    {if(type=="state")Snapshot=JsonSerializer.SerializeToElement(payload,RealtimeJson.Default);}
    public void SendBinary(string playerId,string type,ReadOnlySpan<byte> payload)=>throw new NotSupportedException();
    public void BroadcastBinary(string type,ReadOnlySpan<byte> payload,string? exceptPlayerId=null)=>throw new NotSupportedException();
    public void EndMatch(MatchResult result)=>State=LobbyState.Ended;
    public void Kick(string playerId,string reason)=>throw new NotSupportedException();
}

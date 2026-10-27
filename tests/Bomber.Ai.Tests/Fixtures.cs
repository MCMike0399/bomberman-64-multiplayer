using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Stages;
using Burbuja.Realtime.Abstractions;

sealed class Fixture : ISessionContext
{
    public BlastSession Session {get;}
    public string LobbyId=>"ai-fixture";
    public string GameId=>"blast";
    public LobbyState State {get;set;}=LobbyState.Running;
    public IReadOnlyList<RealtimePlayer> Players=>Array.Empty<RealtimePlayer>();
    public IReadOnlyDictionary<string,string> Settings {get;}
    public Random Random {get;}
    public long Tick {get;private set;}
    public TimeSpan TickDelta=>TimeSpan.FromSeconds(1d/30);
    public DateTimeOffset Now=>DateTimeOffset.UnixEpoch+Elapsed;
    public TimeSpan Elapsed=>TimeSpan.FromSeconds(Tick/30d);
    public bool IsConnected(string id)=>true;
    public long? LastSequence(string id)=>null;
    public Fixture(StageLayout stage,int bots=8,int seed=64,string difficulty="normal",bool suddenDeath=false,int? startingBombs=null)
    {
        Settings=new Dictionary<string,string> { ["stage"]=stage.Id,["bots"]="0",["wins"]="5",["round-seconds"]="300",["sudden-death"]=suddenDeath?"on":"off",["ghosts"]="off",["bot-difficulty"]=difficulty };
        Random=new Random(seed);
        Session=new BlastSession(new GameSessionOptions(LobbyId,GameId,Settings,seed),stage);
        for(int i=0;i<bots;i++)Session.AiTestAddBot("cpu-"+i,i);
        Session.OnMatchStarted(this);
        if(startingBombs is {} capacity)foreach(var player in Session.AiTestPlayers)player.BombCapacity=capacity;
    }
    public void Step(int count=1){for(int i=0;i<count;i++){Tick++;Session.OnTick(this);}}
    public void Send<T>(string id,string type,T payload){}
    public void Broadcast<T>(string type,T payload,string? exceptPlayerId=null){}
    public void SendBinary(string id,string type,ReadOnlySpan<byte> payload)=>throw new NotSupportedException();
    public void BroadcastBinary(string type,ReadOnlySpan<byte> payload,string? exceptPlayerId=null)=>throw new NotSupportedException();
    public void EndMatch(MatchResult result){State=LobbyState.Ended;}
    public void Kick(string id,string reason){}
    public static StageLayout Flat(int width=13,int height=13,(int X,int Z)[]? spawns=null)
    {
        var cells=new CellDef[width,height];for(int x=0;x<width;x++)for(int z=0;z<height;z++)cells[x,z]=(x==0||z==0||x==width-1||z==height-1)?CellDef.Pillar:CellDef.Floor0;
        return new StageLayout {Id="ai-flat",Width=width,Height=height,Cells=cells,Spawns=spawns??[(3,3),(9,9),(3,9),(9,3),(6,2),(2,6),(10,6),(6,10)],SoftBlockDensity=0};
    }
}

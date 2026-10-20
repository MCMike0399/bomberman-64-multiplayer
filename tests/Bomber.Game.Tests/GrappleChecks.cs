using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;

internal static class GrappleChecks
{
    public static int Run()
    {
        var count=0;
        void Check(string name,Action action){action();count++;Console.WriteLine("PASS "+name);}
        Check("rolling bomb knockdown has a visible authoritative cause",()=>{
            var m=Hit();var e=Events(m).LastOrDefault(e=>e.GetProperty("kind").GetString()=="knockdown");
            Assert(e.ValueKind!=JsonValueKind.Undefined,"Rolling collision lacked a knockdown event.");
            Assert(e.GetProperty("reason").GetString()=="rolling-bomb"&&e.GetProperty("playerId").GetString()=="local-1","Knockdown cause/victim missing.");
            Assert(!P(m,0).GetProperty("stunned").GetBoolean(),"Kicker was hit by their outgoing bomb.");
        });
        Check("alternating stick and button presses accelerate recovery without latent bomb",()=>{
            var still=Hit();var active=Hit();
            for(int i=0;i<18&&P(active,1).GetProperty("stunned").GetBoolean();i++){active.SetInput(1,new PlayerInput(i%4<2?1:-1,0,i%4<2));active.Tick();still.Tick();}
            Step(active,3);
            Assert(!P(active,1).GetProperty("stunned").GetBoolean(),"Active victim failed to get up faster.");
            Assert(P(still,1).GetProperty("stunned").GetBoolean(),"Passive recovery was not slower.");
            Assert(State(active).GetProperty("bombs").GetArrayLength()==1,"Recovery mash placed a latent bomb.");
        });
        Check("holding one input does not count as repeated recovery effort",()=>{
            var m=Hit();m.SetInput(1,new PlayerInput(1,0,true));Step(m,18);
            Assert(P(m,1).GetProperty("stunned").GetBoolean(),"Held input repeatedly counted as mash.");
        });
        Check("grabbed player follows carrier and escapes with own input",()=>{
            var m=Grab();m.SetInput(2,new PlayerInput(1,0,B:true));Step(m,6);
            Assert(Math.Abs(F(P(m,1),"x")-F(P(m,2),"x"))<.3f,"Victim did not follow carrier.");
            Assert(F(P(m,1),"y")>F(P(m,2),"y")+.8f,"Victim not held above carrier.");
            for(int i=0;i<45;i++){m.SetInput(1,new PlayerInput(i%4<2?1:-1,0,i%4<2));m.Tick();}
            Assert(P(m,1).GetProperty("carriedBy").ValueKind==JsonValueKind.Null,"Victim could not escape hold.");
            Assert(P(m,2).GetProperty("carrying").ValueKind==JsonValueKind.Null,"Carrier retained stale hold after escape.");
        });
        Check("passive hold times out and player throw uses an arc",()=>{
            var passive=Grab();Step(passive,125);Assert(P(passive,1).GetProperty("carriedBy").ValueKind==JsonValueKind.Null,"Passive victim held forever.");
            var thrown=Grab();float x=F(P(thrown,1),"x"),z=F(P(thrown,1),"z");thrown.SetInput(2,default);Step(thrown,2);
            Assert(P(thrown,1).GetProperty("airborne").GetBoolean(),"Player throw teleported instead of flying.");
            Assert(Math.Abs(F(P(thrown,1),"x")-x)+Math.Abs(F(P(thrown,1),"z")-z)<1,"Player throw jumps multiple cells instantly.");
            Step(thrown,70);Assert(!P(thrown,1).GetProperty("airborne").GetBoolean(),"Thrown player never landed.");
        });
        Check("carrier disconnect drops safely instead of throwing and both leave paths detach",()=>{
            foreach(int leaving in new[]{1,2})
            {
                var f=SessionGrab();
                f.Session.OnPlayerLeft(f,f.Players[leaving],Burbuja.Realtime.Abstractions.LeaveReason.Disconnected);
                f.Step(2);
                foreach(var p in f.Snapshot.GetProperty("players").EnumerateArray())
                    Assert(p.GetProperty("carriedBy").ValueKind==JsonValueKind.Null&&p.GetProperty("carryingPlayerId").ValueKind==JsonValueKind.Null,"Leave retained dangling hold.");
            }
            var disconnected=SessionGrab();disconnected.Session.OnPlayerConnectionChanged(disconnected,disconnected.Players[2],false);disconnected.Step(2);
            var victim=disconnected.Snapshot.GetProperty("players")[1];
            Assert(victim.GetProperty("carriedBy").ValueKind==JsonValueKind.Null&&!victim.GetProperty("airborne").GetBoolean(),"Disconnect launched or retained the victim.");
        });
        return count;
    }
    static LocalMatch Hit()
    {
        var cells=new CellDef[9,9];for(int x=0;x<9;x++)for(int z=0;z<9;z++)cells[x,z]=CellDef.Floor0;
        var arena=new StageLayout {Id="fixture",Width=9,Height=9,Cells=cells,Spawns=[(2,3),(5,3),(5,4)],SoftBlockDensity=0,InitialItems=[(2,3,"remote")]};
        var m=new LocalMatch(new MatchOptions {HumanPlayers=3,BotPlayers=0,Practice=true},arena);Step(m,64);
        m.SetInput(0,new PlayerInput(1,0));Step(m,2);
        m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);
        m.SetInput(0,new PlayerInput(1,0));
        for(int i=0;i<45&&!P(m,1).GetProperty("stunned").GetBoolean();i++)m.Tick();
        m.SetInput(0,default);Assert(P(m,1).GetProperty("stunned").GetBoolean(),"Fixture: moving bomb did not hit victim. "+m.StateJson);return m;
    }
    static LocalMatch Grab(){var m=Hit();m.SetInput(2,new PlayerInput(0,0,B:true));Step(m,2);Assert(P(m,2).GetProperty("carrying").GetString()=="player","Fixture: carrier failed to grab victim.");return m;}
    static AppearanceFixture SessionGrab()
    {
        var cells=new CellDef[9,9];for(int x=0;x<9;x++)for(int z=0;z<9;z++)cells[x,z]=CellDef.Floor0;
        var f=new AppearanceFixture(customStage:new StageLayout {Id="fixture",Width=9,Height=9,Cells=cells,Spawns=[(2,3),(5,3),(5,4)],SoftBlockDensity=0,InitialItems=[(2,3,"remote")]},humans:3);
        void Input(int seat,float x=0,bool a=false,bool b=false)=>f.Message(seat,JsonSerializer.Serialize(new {x,z=0,a,b,r=false}),"input");
        f.Step(64);Input(0,1);f.Step(2);Input(0,a:true);f.Step(2);Input(0,1);
        for(int i=0;i<45&&!f.Snapshot.GetProperty("players")[1].GetProperty("stunned").GetBoolean();i++)f.Step(1);
        Input(0);Input(2,b:true);f.Step(2);
        Assert(f.Snapshot.GetProperty("players")[2].GetProperty("carrying").GetString()=="player","Callback fixture did not grab.");
        return f;
    }
    static void Step(LocalMatch m,int ticks){for(int i=0;i<ticks;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
    static JsonElement P(LocalMatch m,int slot)=>State(m).GetProperty("players")[slot];
    static JsonElement[] Events(LocalMatch m)=>State(m).GetProperty("events").EnumerateArray().ToArray();
    static float F(JsonElement p,string key)=>p.GetProperty(key).GetSingle();
    static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}

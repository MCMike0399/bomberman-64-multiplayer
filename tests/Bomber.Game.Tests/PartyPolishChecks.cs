using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;

internal static class PartyPolishChecks
{
    public static int Run()
    {
        int passed=0;var failures=new List<string>();
        void Check(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failures.Add(name+": "+e.Message);Console.WriteLine("FAIL "+failures[^1]);}}
        Check("water casualty returns to dry ground as a controllable ghost",()=>{
            var cells=new CellDef[15,15];for(int x=0;x<15;x++)for(int z=0;z<15;z++)cells[x,z]=x<3?CellDef.Water:CellDef.Floor1;
            var stage=new StageLayout {Id="water-return",Width=15,Height=15,Cells=cells,Spawns=[(3,4),(10,10),(12,12)],SoftBlockDensity=0};
            var m=new LocalMatch(new MatchOptions{HumanPlayers=3,BotPlayers=0},stage);Step(m,64);
            m.SetInput(0,new PlayerInput(-1,0));Step(m,5);m.SetInput(0,default);Step(m,30);
            var p=State(m).GetProperty("players")[0];
            Assert(p.GetProperty("alive").GetBoolean()&&p.GetProperty("ghost").GetBoolean()&&F(p,"x")>=3&&F(p,"y")>=1,"Ghost remained stranded in the water: "+p);
            float beforeZ=F(p,"z");m.SetInput(0,new PlayerInput(0,1));Step(m,6);
            Assert(F(State(m).GetProperty("players")[0],"z")>beforeZ+.3f,"Returned ghost cannot move.");
        });
        Check("kicks descend single-tier lips in every direction and alignment",()=>{
            foreach(var (dx,dz) in new (float,float)[]{(1,0),(-1,0),(0,1),(0,-1),(1,1),(-1,1),(1,-1),(-1,-1),(1,.3f),(-1,.3f),(.3f,1),(.3f,-1)})
            {
                var cells=new CellDef[31,31];for(int x=0;x<31;x++)for(int z=0;z<31;z++)cells[x,z]=x>=11&&x<=19&&z>=11&&z<=19?CellDef.Floor1:CellDef.Floor0;
                var stage=new StageLayout{Id="lip",Width=31,Height=31,Cells=cells,Spawns=[(15,15),(28,28)],SoftBlockDensity=0,InitialItems=[(15,15,"remote")]};
                var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=true},stage);Step(m,64);
                m.SetInput(0,new PlayerInput(dx,dz));Step(m,2);m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);Step(m,2);
                m.SetInput(0,new PlayerInput(dx,dz,A:true));Step(m,8);m.SetInput(0,default);Step(m,32);
                var b=State(m).GetProperty("bombs")[0];Assert(b.GetProperty("state").GetInt32()==1&&F(b,"y")==0,$"Kick ({dx},{dz}) failed descent: {b}");
            }
        });
        Check("shallow sideways ramp exits keep descending momentum",()=>{
            var cells=new CellDef[30,15];for(int x=0;x<30;x++)for(int z=0;z<15;z++)cells[x,z]=x<8?CellDef.Ramp0To1(0,1):CellDef.Floor0;
            var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=true},new StageLayout{Id="ramp-lip",Width=30,Height=15,Cells=cells,Spawns=[(3,3),(25,12)],SoftBlockDensity=0,InitialItems=[(3,3,"remote")]});Step(m,64);
            m.SetInput(0,new PlayerInput(0,-1));Step(m,3);m.SetInput(0,new PlayerInput(1,0));Step(m,2);
            m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);Step(m,2);m.SetInput(0,new PlayerInput(1,0,A:true));Step(m,8);m.SetInput(0,default);Step(m,28);
            var b=State(m).GetProperty("bombs")[0];Assert(F(b,"x")>12&&b.GetProperty("state").GetInt32()==1,"Ramp lip stopped a descending kick: "+b);
        });
        Check("batched press and release preserve bomb and kick-stop taps",()=>{
            var cells=new CellDef[15,15];for(int x=0;x<15;x++)for(int z=0;z<15;z++)cells[x,z]=CellDef.Floor0;
            var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0},new StageLayout{Id="input-batch",Width=15,Height=15,Cells=cells,Spawns=[(3,3),(12,12)],SoftBlockDensity=0});Step(m,64);
            // TCP can deliver both client frames before the next authority tick.
            m.SetInput(0,new PlayerInput(0,0,A:true));m.SetInput(0,default);Step(m,2);
            Assert(State(m).GetProperty("bombs").GetArrayLength()==1,"A press/release batch was overwritten before simulation.");
            m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,8);m.SetInput(0,default);Step(m,2);
            Assert(State(m).GetProperty("bombs")[0].GetProperty("state").GetInt32()==1,"Batched-input fixture never kicked.");
            m.SetInput(0,new PlayerInput(0,0,Stop:true));m.SetInput(0,default);Step(m,2);
            Assert(State(m).GetProperty("bombs")[0].GetProperty("state").GetInt32()==0,"Stop press/release batch was overwritten before simulation.");
            Assert(State(m).GetProperty("events").EnumerateArray().Count(e=>e.GetProperty("kind").GetString()=="kick-stop")==1,"Stop tap was missing or repeated.");
        });
        Check("disconnect clears accepted but unconsumed button transitions",()=>{
            var f=new AppearanceFixture();f.Step(64);
            f.Message(0,"{\"a\":true}","input");
            f.Session.OnPlayerConnectionChanged(f,f.Players[0],false);f.Step(2);
            Assert(f.Snapshot.GetProperty("bombs").GetArrayLength()==0,"Disconnected player's queued press fired after release.");
        });
        Check("temple rear does not contain a hidden walkable corridor",()=>{
            var stage=BlastStages.Get(BlastStages.SunkenTemple);
            Assert(Enumerable.Range(5,11).All(x=>!stage.At(x,1).IsPassable),"Rear row behind the pyramid remains walkable.");
        });
        if(failures.Count>0)throw new InvalidOperationException(string.Join("; ",failures));return passed;
    }
    static void Step(LocalMatch m,int ticks){for(int i=0;i<ticks;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
    static float F(JsonElement p,string key)=>p.GetProperty(key).GetSingle();
    static void Assert(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
}

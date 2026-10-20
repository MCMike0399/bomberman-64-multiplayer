using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;

internal static class BombActionsChecks
{
    public static int Run()
    {
        var count=0;
        void Check(string name,Action action){action();count++;Console.WriteLine("PASS "+name);}
        Check("A alone lifts a fresh bomb and latches carry until B throws",()=>{
            var m=Match();m.SetInput(0,new PlayerInput(0,0,true));Step(m,10);
            Assert(P(m).GetProperty("carrying").GetString()=="bomb","Holding A alone never lifted the fresh bomb.");
            var fuse=F(B(m),"fuse");Step(m,100);
            Assert(F(B(m),"fuse")==fuse&&B(m).GetProperty("pumps").GetInt32()==0,"Held A repeated pumping or advanced carried fuse.");
            m.SetInput(0,default);Step(m,2);
            Assert(P(m).GetProperty("carrying").GetString()=="bomb","Releasing A unexpectedly threw the bomb.");
            m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);
            Assert(B(m).GetProperty("pumps").GetInt32()==1,"Fresh A tap failed to inflate the carried bomb.");
            m.SetInput(0,new PlayerInput(0,0,B:true));Step(m,2);
            Assert(P(m).GetProperty("carrying").ValueKind==JsonValueKind.Null&&B(m).GetProperty("state").GetInt32()==3,"B press failed to throw latched carry.");
            Assert(F(B(m),"fuse")<fuse,"Thrown bomb fuse failed to resume.");
            Step(m,100);Assert(State(m).GetProperty("bombs").GetArrayLength()==0,"A-lifted thrown bomb never exploded.");
        });
        Check("bomb placement is ahead with room to retreat and a running fuse",()=>{
            var m=Match();m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);
            var p=P(m);var b=B(m);
            Assert(F(b,"z")-F(p,"z")>=.75f,"Bomb was placed under the player instead of ahead.");
            m.SetInput(0,new PlayerInput(0,-1));Step(m,12);
            Assert(F(P(m),"z")<F(p,"z")-1,"Placed bomb trapped its owner.");
            Step(m,72);Assert(State(m).GetProperty("bombs").GetArrayLength()==0,"Normal bomb never exploded.");
        });
        Check("A hold cannot create through stone or lift after walking away",()=>{
            var blocked=Match(wall:true);blocked.SetInput(0,new PlayerInput(0,0,true));Step(blocked,20);
            Assert(State(blocked).GetProperty("bombs").GetArrayLength()==0&&P(blocked).GetProperty("carrying").ValueKind==JsonValueKind.Null,"A hold bypassed blocked front placement.");
            var m=Match();m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);
            m.SetInput(0,new PlayerInput(0,-1,true));Step(m,12);
            Assert(P(m).GetProperty("carrying").ValueKind==JsonValueKind.Null&&B(m).GetProperty("state").GetInt32()==0,"A hold lifted a distant fresh bomb.");
            m.SetInput(0,default);Step(m,90);
            Assert(State(m).GetProperty("bombs").GetArrayLength()==0,"Distant A candidate paused its fuse.");
        });
        Check("placement rejects a blocked front instead of placing underfoot",()=>{
            var m=Match(wall:true);m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);
            Assert(State(m).GetProperty("bombs").GetArrayLength()==0,"Bomb placed into front wall or underneath player.");
        });
        Check("front placement never overlaps another bomb or player",()=>{
            var m=Match();m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);
            m.SetInput(0,default);Step(m,2);m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);
            Assert(State(m).GetProperty("bombs").GetArrayLength()==1,"Duplicate front bombs overlapped.");
            var occupied=Match(frontPlayer:true);occupied.SetInput(0,new PlayerInput(0,0,true));Step(occupied,2);
            Assert(State(occupied).GetProperty("bombs").GetArrayLength()==0,"Front bomb overlapped another player.");
        });
        Check("remote bomb waits for its command while normal bombs use a fuse",()=>{
            var m=Match(remote:true);m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);
            m.SetInput(0,new PlayerInput(0,-1));Step(m,12);m.SetInput(0,default);Step(m,90);
            Assert(State(m).GetProperty("bombs").GetArrayLength()==1&&B(m).GetProperty("remote").GetBoolean(),"Remote bomb incorrectly used a timed fuse.");
            m.SetInput(0,new PlayerInput(0,0,R:true));Step(m,2);
            Assert(State(m).GetProperty("bombs").GetArrayLength()==0,"Remote command failed to detonate a waiting bomb.");
            Assert(P(m).GetProperty("activeBombs").GetInt32()==0,"Detonation leaked owner capacity.");
        });
        Check("kick keeps the stick angle and runs decisively ahead of the kicker",()=>{
            var m=Match();m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);
            m.SetInput(0,new PlayerInput(.3f,1));Step(m,2);var start=B(m);
            Step(m,6);var end=B(m);var dx=F(end,"x")-F(start,"x");var dz=F(end,"z")-F(start,"z");
            Assert(dz>1.2f,"Kick advances too slowly and feels like gradual pushing.");
            Assert(dx/dz>.22f&&dx/dz<.38f,"Kick snapped continuous stick angle to a grid direction.");
            Assert(!P(m).GetProperty("stunned").GetBoolean(),"Outgoing kick stunned its kicker.");
        });
        Check("fast rolling bomb stops its full radius before stone",()=>{
            var m=Match(farWall:true);m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);
            m.SetInput(0,new PlayerInput(0,1));Step(m,12);m.SetInput(0,default);Step(m,8);
            var b=B(m);Assert(F(b,"z")<=6-.35f+.01f,"Rolling bomb penetrated stone with its radius.");
            Assert(b.GetProperty("state").GetInt32()==0,"Bomb did not stop at stone.");
        });
        return count;
    }
    static LocalMatch Match(bool wall=false,bool farWall=false,bool frontPlayer=false,bool remote=false)
    {
        var cells=new CellDef[11,11];for(int x=0;x<11;x++)for(int z=0;z<11;z++)cells[x,z]=CellDef.Floor0;
        if(wall)cells[3,4]=CellDef.Pillar;if(farWall)for(int x=0;x<11;x++)cells[x,6]=CellDef.Pillar;
        var m=new LocalMatch(new MatchOptions {HumanPlayers=2,BotPlayers=0,Practice=true},new StageLayout {Id="fixture",Width=11,Height=11,Cells=cells,Spawns=[(3,3),frontPlayer?(3,4):(9,9)],SoftBlockDensity=0,InitialItems=remote?[(3,3,"remote")]:[]});
        Step(m,64);return m;
    }
    static void Step(LocalMatch m,int n){for(int i=0;i<n;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
    static JsonElement P(LocalMatch m)=>State(m).GetProperty("players")[0];
    static JsonElement B(LocalMatch m)=>State(m).GetProperty("bombs")[0];
    static float F(JsonElement e,string key)=>e.GetProperty(key).GetSingle();
    static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}

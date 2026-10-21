using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;
using Burbuja.Realtime.Abstractions;

internal static class BombImpactChecks
{
    public static int Run()
    {
        int count=0;var failures=new List<string>();
        void Check(string name,Action check){try{check();count++;Console.WriteLine("PASS "+name);}catch(Exception e){failures.Add(name+": "+e.Message);Console.WriteLine("FAIL "+failures[^1]);}}
        Check("kick launches decisively with a short visible hop",()=>{
            var m=Match();Place(m);m.SetInput(0,new PlayerInput(0,1,A:true));AwaitKick(m);
            var first=Bomb(m);Step(m,4);var next=Bomb(m);
            Assert(F(first,"y")>.02f||F(next,"y")>.02f,"A kick has no launch hop; it only slides across the floor.");
            Assert(F(next,"z")-F(first,"z")>1.4f,"Kick lacks a decisive shooting impulse.");
        });
        Check("kicked bomb launches an opponent up and away without tunneling",()=>{
            var m=Match(victim:true);Place(m);m.SetInput(0,new PlayerInput(0,1,A:true));
            bool launched=false;float startZ=F(Player(m,1),"z");
            for(int i=0;i<15;i++){m.Tick();var p=Player(m,1);if(p.GetProperty("airborne").GetBoolean()&&F(p,"y")>.08f&&F(p,"z")>startZ+.08f)launched=true;}
            Assert(launched,"Fast bomb contact did not lift and displace its victim.");
            Assert(!Player(m,0).GetProperty("stunned").GetBoolean(),"Outgoing kick hit its owner from behind.");
        });
        Check("billiard kick transfers motion without restarting a normal fuse",()=>{
            var m=Match(chain:true);Place(m);m.SetInput(0,default);m.SetInput(1,new PlayerInput(0,0,true));Step(m,2);
            m.SetInput(1,new PlayerInput(1,0));Step(m,8);m.SetInput(1,default);
            var target=Bombs(m).EnumerateArray().Last();var id=target.GetProperty("id").GetString();var initial=F(target,"z");var fuse=F(target,"fuse");
            m.SetInput(0,new PlayerInput(0,1,A:true));Step(m,15);m.SetInput(0,default);
            target=Bombs(m).EnumerateArray().Single(b=>b.GetProperty("id").GetString()==id);
            Assert(F(target,"z")>initial+.4f,$"Kicked bomb did not transfer its impulse to next bomb: initial={initial}, target={target}, all={Bombs(m)}.");
            Assert(F(target,"fuse")<fuse,"Chain contact restarted or paused the normal fuse.");
            Step(m,100);Assert(Bombs(m).GetArrayLength()==0,"Billiard corner bomb retained capacity forever.");
        });
        Check("moving remote responds immediately to its command",()=>{
            var m=Match(remote:true);Place(m);m.SetInput(0,new PlayerInput(0,1,A:true));AwaitKick(m);
            Assert(Bomb(m).GetProperty("state").GetInt32()==1,"Moving remote fixture did not kick.");
            m.SetInput(0,new PlayerInput(0,0,R:true));Step(m,2);
            Assert(Bombs(m).GetArrayLength()==0,"Remote ignored command while moving, forcing the owner to wait for a wall.");
        });
        Check("corner collision keeps normal and pumped bombs on their fuse",()=>{
            foreach(bool pumped in new[]{false,true}){
                var m=Match(wall:true);
                if(pumped){
                    m.SetInput(0,new PlayerInput(0,0,true));Step(m,10);m.SetInput(0,default);Step(m,2);
                    m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);
                    m.SetInput(0,new PlayerInput(0,-1));Step(m,2);
                    m.SetInput(0,new PlayerInput(0,.4f,B:true));Step(m,2);
                    m.SetInput(0,new PlayerInput(0,-1));Step(m,6);m.SetInput(0,default);Step(m,18);
                    m.SetInput(0,new PlayerInput(0,1));Step(m,24);
                }
                else {Place(m);m.SetInput(0,new PlayerInput(0,1,A:true));Step(m,12);}
                m.SetInput(0,default);Step(m,pumped?2:16);var b=Bomb(m);
                Assert(F(b,"z")>5.5f&&F(b,"z")<=6-BlastBomb.Radius+.01f,$"Corner collision did not settle correctly: pumped={pumped}, bomb={b}.");
                Step(m,100);Assert(Bombs(m).GetArrayLength()==0,"Corner-stopped bomb never auto-exploded.");
            }
        });
        Check("diagonal corner cannot trap or swallow a normal fuse",()=>{
            var m=Match(wall:true,corner:true);m.SetInput(0,new PlayerInput(.7071f,.7071f));Step(m,2);Place(m);
            m.SetInput(0,new PlayerInput(.7071f,.7071f,A:true));Step(m,12);m.SetInput(0,default);Step(m,12);
            var b=Bomb(m);Assert(F(b,"x")>5.5f&&F(b,"z")>5.5f&&F(b,"x")<=5.66f&&F(b,"z")<=5.66f,
                "Diagonal bomb did not stop with its whole circle outside both corner walls.");
            Step(m,100);Assert(Bombs(m).GetArrayLength()==0,"Diagonal corner bomb never auto-exploded.");
        });
        Check("remote corner bomb waits for command and orphaned remotes cannot persist forever",()=>{
            var m=Match(wall:true,remote:true);Place(m);m.SetInput(0,new PlayerInput(0,1,A:true));Step(m,12);m.SetInput(0,default);Step(m,100);
            Assert(Bombs(m).GetArrayLength()==1,"Active remote owner lost deliberate command detonation.");
            m.SetInput(0,new PlayerInput(0,0,R:true));Step(m,2);Assert(Bombs(m).GetArrayLength()==0,"Corner remote ignored its command.");
            var cells=Cells();var f=new AppearanceFixture(customStage:new StageLayout{Id="fixture",Width=15,Height=15,Cells=cells,Spawns=[(3,3),(12,12),(10,12)],SoftBlockDensity=0,InitialItems=[(3,3,"remote")]},humans:3);
            f.Step(64);f.Message(0,"{\"a\":true}","input");f.Step(2);f.Message(0,"{}","input");
            Assert(f.Snapshot.GetProperty("bombs").GetArrayLength()==1,"Remote orphan fixture did not create a bomb.");
            f.Session.OnPlayerLeft(f,f.Players[0],LeaveReason.Left);f.Step(100);
            Assert(f.Snapshot.GetProperty("bombs").GetArrayLength()==0,"Departed owner's remote remained permanently armed.");
        });
        Check("dead remote owner cannot leave a permanently armed bomb",()=>{
            var cells=Cells();for(int z=0;z<15;z++)cells[0,z]=CellDef.Water;
            var f=new AppearanceFixture(customStage:new StageLayout{Id="fixture",Width=15,Height=15,Cells=cells,
                Spawns=[(3,3),(12,12),(10,12)],SoftBlockDensity=0,InitialItems=[(3,3,"remote")]},humans:3);
            f.Step(64);f.Message(0,"{\"a\":true}","input");f.Step(2);f.Message(0,"{\"x\":-1}","input");f.Step(30);
            var owner=f.Snapshot.GetProperty("players")[0];
            Assert(!owner.GetProperty("alive").GetBoolean()||owner.GetProperty("ghost").GetBoolean(),"Remote owner fixture never reached water death.");
            f.Step(100);Assert(f.Snapshot.GetProperty("bombs").GetArrayLength()==0,"Dead owner left a permanent remote bomb.");
            Assert(f.Snapshot.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="remote-fuse"),
                "Orphaned remote did not announce its resumed fuse.");
        });
        if(failures.Count>0)throw new InvalidOperationException(string.Join("; ",failures));
        return count;
    }
    static CellDef[,] Cells(){var c=new CellDef[15,15];for(int x=0;x<15;x++)for(int z=0;z<15;z++)c[x,z]=CellDef.Floor0;return c;}
    static LocalMatch Match(bool victim=false,bool wall=false,bool remote=false,bool chain=false,bool corner=false){var c=Cells();if(corner)for(int z=0;z<15;z++)c[6,z]=CellDef.Pillar;if(wall)for(int x=0;x<15;x++)c[x,6]=CellDef.Pillar;var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=true},new StageLayout{Id="fixture",Width=15,Height=15,Cells=c,Spawns=[(3,3),victim||chain?(3,6):(12,12)],SoftBlockDensity=0,InitialItems=remote?[(3,3,"remote")]:[]});Step(m,64);return m;}
    static void AwaitKick(LocalMatch m){for(int i=0;i<15;i++){m.Tick();if(Bomb(m).GetProperty("state").GetInt32()==1)return;}throw new InvalidOperationException("Contact kick never released after anticipation.");}
    static void Place(LocalMatch m){m.SetInput(0,new PlayerInput(0,0,true));Step(m,2);m.SetInput(0,default);Step(m,2);}
    static void Step(LocalMatch m,int n){for(int i=0;i<n;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
    static JsonElement Bombs(LocalMatch m)=>State(m).GetProperty("bombs");
    static JsonElement Bomb(LocalMatch m)=>Bombs(m)[0];
    static JsonElement Player(LocalMatch m,int i)=>State(m).GetProperty("players")[i];
    static float F(JsonElement e,string key)=>e.GetProperty(key).GetSingle();
    static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}

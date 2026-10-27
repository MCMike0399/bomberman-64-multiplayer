using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

internal static class PowerUpModeChecks
{
    public static int Run()
    {
        int passed=0;var failures=new List<string>();
        void Check(string name,Action test){try{test();Console.WriteLine("PASS "+name);passed++;}catch(Exception e){failures.Add(name+": "+e.Message);Console.WriteLine("FAIL "+failures[^1]);}}
        Check("latest special pickup replaces the previous bomb mode in either order",()=>{
            foreach(var first in new[]{"remote","red"}){
                var p=new BlastPlayer();string second=first=="remote"?"red":"remote";
                PowerUpRules.Apply(p,first);PowerUpRules.Apply(p,second);
                Assert(p.HasRemote==(second=="remote")&&p.HasRed==(second=="red"),"Special modes stacked instead of replacing each other.");
                Assert(p.PickupSerial==2&&p.LastPickup==second,"Replacement lost the accepted pickup cue.");
                PowerUpRules.Apply(p,second);
                Assert(p.PickupSerial==3&&p.HasRemote!=(p.HasRed),"Repeated pickup cleared the selected mode.");
            }
        });
        Check("ordinary upgrades and heart preserve the selected special bomb mode",()=>{
            foreach(var mode in new[]{"remote","red"}){
                var p=new BlastPlayer();PowerUpRules.Apply(p,mode);
                foreach(var item in new[]{"fire","bomb","heart"})PowerUpRules.Apply(p,item);
                Assert(p.FireLevel==2&&p.BombCapacity==2&&p.HasHeart,"Mode interfered with ordinary inventory upgrades.");
                Assert(p.HasRemote==(mode=="remote")&&p.HasRed==(mode=="red"),"Ordinary pickup replaced the bomb mode.");
            }
        });
        Check("remote to red pickup changes future bombs and releases old remote into its remaining fuse",()=>{
            var m=Match("remote","red");string oldId=Place(m).GetProperty("id").GetString()!;
            var old=FindBomb(m,oldId);float fuse=F(old,"fuse");
            Assert(Flag(old,"remote")&&!Flag(old,"red"),"Initial bomb was not an ordinary remote.");
            bool sawFallback=false;
            m.Message+=json=>{using var d=JsonDocument.Parse(json);if(d.RootElement.TryGetProperty("d",out var data)&&data.TryGetProperty("events",out var events))
                sawFallback|=events.EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="remote-fuse");};
            CollectNext(m);
            Assert(Flag(Player(m),"red")&&!Flag(Player(m),"remote"),"Walking over red did not replace remote.");
            old=FindBomb(m,oldId);
            Assert(!Flag(old,"remote")&&!Flag(old,"red")&&F(old,"fuse")<fuse&&F(old,"fuse")>1,"Old remote became red, restarted, or lost its countdown.");
            Assert(sawFallback,"Replacement failed to emit the existing remote-fuse safety cue.");
            var next=Place(m);
            Assert(Flag(next,"red")&&!Flag(next,"remote"),"Future bomb did not select red mode: "+State(m));
            m.SetInput(0,new PlayerInput(1,0));Step(m,100);
            Assert(!Bombs(m).Any(b=>b.GetProperty("id").GetString()==oldId),"Replaced remote remained an immortal obstacle.");
        });
        Check("red to remote pickup leaves old red intact while future remote waits for a command",()=>{
            var m=Match("red","remote");var old=Place(m);string oldId=old.GetProperty("id").GetString()!;float fuse=F(old,"fuse");
            CollectNext(m);
            Assert(Flag(Player(m),"remote")&&!Flag(Player(m),"red"),"Walking over remote did not replace red.");
            old=FindBomb(m,oldId);
            Assert(Flag(old,"red")&&!Flag(old,"remote")&&F(old,"fuse")<fuse,"Previously placed red changed its blast mode or fuse.");
            var next=Place(m);string nextId=next.GetProperty("id").GetString()!;
            Assert(Flag(next,"remote")&&!Flag(next,"red"),"Future bomb did not select remote mode: "+State(m));
            m.SetInput(0,new PlayerInput(1,0));Step(m,100);
            Assert(!Bombs(m).Any(b=>b.GetProperty("id").GetString()==oldId),"Previously placed red stopped its normal countdown.");
            Assert(Flag(FindBomb(m,nextId),"remote"),"Active owner's replacement remote stopped waiting for a command.");
            m.SetInput(0,new PlayerInput(0,0,R:true));Step(m,2);
            Assert(!Bombs(m).Any(b=>b.GetProperty("id").GetString()==nextId),"Replacement remote did not accept its owner's command.");
        });
        if(failures.Count>0)throw new InvalidOperationException(string.Join("; ",failures));return passed;
    }
    static LocalMatch Match(string first,string second){var cells=new CellDef[30,15];for(int x=0;x<30;x++)for(int z=0;z<15;z++)cells[x,z]=CellDef.Floor0;
        var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=true,SuddenDeath=false},
            new StageLayout{Id="bomb-mode",Width=30,Height=15,Cells=cells,Spawns=[(3,3),(27,12)],SoftBlockDensity=0,InitialItems=[(3,3,first),(3,3,"bomb"),(5,3,second)]});Step(m,64);return m;}
    static JsonElement Place(LocalMatch m){int count=Bombs(m).Length;m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);Step(m,2);
        Assert(Bombs(m).Length==count+1,"Fixture failed to place its next bomb.");return Bombs(m).Last();}
    static void CollectNext(LocalMatch m){m.SetInput(0,new PlayerInput(1,0));Step(m,16);m.SetInput(0,new PlayerInput(0,1));Step(m,1);m.SetInput(0,default);Step(m,3);}
    static void Step(LocalMatch m,int n){for(int i=0;i<n;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
    static JsonElement Player(LocalMatch m)=>State(m).GetProperty("players")[0];
    static JsonElement[] Bombs(LocalMatch m)=>State(m).GetProperty("bombs").EnumerateArray().ToArray();
    static JsonElement FindBomb(LocalMatch m,string id)=>Bombs(m).Single(b=>b.GetProperty("id").GetString()==id);
    static bool Flag(JsonElement e,string key)=>e.GetProperty(key).GetBoolean();
    static float F(JsonElement e,string key)=>e.GetProperty(key).GetSingle();
    static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}

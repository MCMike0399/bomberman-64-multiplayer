using System.Numerics;
using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;

internal static class SandboxEquipmentChecks
{
    public static int Run()
    {
        int count=0;
        void Check(string label,Action action){action();count++;Console.WriteLine("PASS "+label);}
        Check("two equipment slots replace independently while capacity and flame stack",()=>{
            var p=new BlastPlayer();
            foreach(var type in new[]{"red","remote","bounce","fast"}){
                string old=p.BombType;Assert(PowerUpRules.Exchange(p,type)==(old=="normal"?null:old),"Wrong displaced bomb type.");
                PowerUpRules.Apply(p,"fire");PowerUpRules.Apply(p,"bomb");
                Assert(p.BombType==type,"Stacking upgrade replaced equipment.");
            }
            PowerUpRules.Exchange(p,"heart");Assert(PowerUpRules.Exchange(p,"focus")=="heart","Ability replacement did not drop heart.");
            Assert(p.HasFocus&&p.BombType=="fast"&&p.FireLevel==3&&p.BombCapacity==7,"Equipment slots interfered.");
            Assert(!PowerUpRules.CanCollect(p,"fast")&&!PowerUpRules.CanCollect(p,"focus")&&!PowerUpRules.CanCollect(p,"fire"),"Duplicate equipment or capped stack consumed.");
        });
        Check("replacement drops old equipment with a pickup lock and finite arena lifetime",()=>{
            var m=Match([(5,5,"remote"),(7,5,"red")]);
            m.SetInput(0,new PlayerInput(1,0));Step(m,16);m.SetInput(0,default);Step(m,4);
            Assert(P(m).GetProperty("bombType").GetString()=="red","Replacement was not collected.");
            var item=S(m).GetProperty("items").EnumerateArray().Single(i=>i.GetProperty("type").GetString()=="remote");
            Assert(item.GetProperty("dropped").GetBoolean()&&item.GetProperty("life").GetSingle()>18,"Swap drop lacks finite life.");
            Step(m,20);Assert(P(m).GetProperty("bombType").GetString()=="red","Owner instantly recollected swapped equipment.");
            m.SetInput(0,new PlayerInput(0,1));Step(m,18);m.SetInput(0,default);Step(m,610);
            Assert(!S(m).GetProperty("items").EnumerateArray().Any(i=>i.GetProperty("type").GetString()=="remote"),"Swap drop never expired.");
        });
        Check("normal charging is deliberate and focus completes each level three times faster",()=>{
            foreach(bool focus in new[]{false,true}){
                var m=Match(focus?[(5,5,"focus")]:[]);Lift(m);
                m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);
                Assert(B(m).GetProperty("pumps").GetInt32()==0&&B(m).GetProperty("charging").GetBoolean(),"Charge completed instantly.");
                m.SetInput(0,default);Step(m,4);
                Assert(B(m).GetProperty("pumps").GetInt32()==(focus?1:0),"Focus timing did not create an advantage.");
                Step(m,10);Assert(B(m).GetProperty("pumps").GetInt32()==1,"Normal charge did not complete.");
                Step(m,40);Assert(B(m).GetProperty("pumps").GetInt32()==1,"A held/released tap auto-repeated.");
            }
        });
        Check("early throw cancels incomplete charging and preserves completed levels",()=>{
            var m=Match([]);Lift(m);m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);
            m.SetInput(0,new PlayerInput(0,0,B:true));Step(m,2);
            Assert(B(m).GetProperty("pumps").GetInt32()==0&&!B(m).GetProperty("charging").GetBoolean(),"Throw granted an unfinished charge.");
        });
        Check("fast throws reach the same selected ground target sooner",()=>{
            foreach(int pumps in new[]{0,3}){
                var origin=new Vector3(5.5f,BombThrowRules.LaunchHeight,5.5f);
                var normal=BombThrowRules.Project(origin,BombThrowRules.AimedVelocity(0,1,6,1,pumps),(x,z)=>0,_=>false,gravityScale:BombThrowRules.Weight(pumps));
                var fast=BombThrowRules.Project(origin,BombThrowRules.AimedVelocity(0,1,6,1.5f,pumps),(x,z)=>0,_=>false,motionRate:1.5f,gravityScale:BombThrowRules.Weight(pumps));
                Assert(Vector3.Distance(normal.Last,fast.Last)<.01f&&fast.Points.Count<normal.Points.Count*.8f,"Fast bomb lost its target or speed advantage.");
            }
        });
        Check("equipped fast bombs use the faster authority clock on real aimed throws",()=>{
            (int Ticks,float Z) Throw(string type){
                var m=Match(type=="fast"?[(5,5,"fast")]:[]);Lift(m);
                m.SetInput(0,new PlayerInput(0,0,Aim:true));Step(m,2);
                m.SetInput(0,new PlayerInput(0,0,B:true));Step(m,2);m.SetInput(0,default);
                int ticks=2;while(B(m).GetProperty("state").GetInt32()!=0&&ticks<70){Step(m,2);ticks+=2;}
                Assert(B(m).GetProperty("state").GetInt32()==0,"Throw failed to land before its fuse.");
                return(ticks,F(B(m),"z"));
            }
            var normal=Throw("normal");var fast=Throw("fast");
            Assert(fast.Ticks<normal.Ticks*.8f&&Math.Abs(fast.Z-normal.Z)<.02f,"Fast authority throw lost speed or aimed destination.");
        });
        Check("another player can collect swapped equipment before the former holder's lock expires",()=>{
            var cells=new CellDef[30,25];for(int x=0;x<30;x++)for(int z=0;z<25;z++)cells[x,z]=CellDef.Floor0;
            var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=true},new StageLayout{Id="swap-sharing",Width=30,Height=25,Cells=cells,Spawns=[(5,5),(7,7)],SoftBlockDensity=0,InitialItems=[(5,5,"remote"),(7,5,"red")]});Step(m,64);
            m.SetInput(0,new PlayerInput(1,0));Step(m,12);m.SetInput(0,new PlayerInput(-1,0));
            m.SetInput(1,new PlayerInput(-.5f,-1));Step(m,12);m.SetInput(0,default);m.SetInput(1,default);Step(m,2);
            Assert(S(m).GetProperty("players")[1].GetProperty("bombType").GetString()=="remote","Swap drop was not available to another player immediately.");
        });
        Check("bounce bombs ricochet at stone and their real fuse still runs",()=>{
            var m=Match([(5,5,"bounce")],wall:true);Lift(m);m.SetInput(0,new PlayerInput(0,0,B:true));Step(m,2);m.SetInput(0,default);
            float furthest=0;bool returned=false;
            for(int i=0;i<90;i++){Step(m,1);if(S(m).GetProperty("bombs").GetArrayLength()==0)break;float z=F(B(m),"z");returned|=z<furthest-.3f;furthest=Math.Max(furthest,z);}
            Assert(returned,"Bouncing bomb stopped instead of reflecting off stone.");
            Step(m,40);Assert(S(m).GetProperty("bombs").GetArrayLength()==0,"Bounce reset or disabled the fuse.");
        });
        Check("bouncing bombs roll normally, ricochet at walls, and obey Stop",()=>{
            var m=Match([(5,5,"bounce")],wall:true);m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);Step(m,2);m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,6);m.SetInput(0,default);
            Assert(B(m).GetProperty("state").GetInt32()==1&&F(B(m),"y")==0,"Bouncing bomb hopped during an ordinary roll.");
            m.SetInput(0,new PlayerInput(-1,0));
            float furthest=F(B(m),"z");bool reflected=false;
            for(int i=0;i<16;i++){Step(m,1);float z=F(B(m),"z");reflected|=z<furthest-.2f;furthest=Math.Max(furthest,z);}
            Assert(reflected&&B(m).GetProperty("state").GetInt32()==1,"Rolling bomb failed to ricochet and keep moving.");
            float fuse=F(B(m),"fuse");m.SetInput(0,new PlayerInput(0,0,Stop:true));Step(m,2);m.SetInput(0,default);float stopped=F(B(m),"z");Step(m,12);
            Assert(B(m).GetProperty("state").GetInt32()==0&&F(B(m),"z")==stopped&&F(B(m),"fuse")<fuse,"Stop failed to stop the ricochet or paused its fuse.");
        });
        Check("ground bounces require a charged throw and agree with the advisory",()=>{
            foreach(int pumps in new[]{0,1}){
                var m=Match([(5,5,"bounce")]);Lift(m);
                if(pumps>0){m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);Step(m,14);}
                var p=P(m);var projection=BombThrowRules.Project(new(F(p,"x"),F(p,"y")+BombThrowRules.LaunchHeight,F(p,"z")),BombThrowRules.Velocity(0,1,0,pumps),(x,z)=>0,_=>false,3.5f,bounce:true,gravityScale:BombThrowRules.Weight(pumps),pumps:pumps);
                m.SetInput(0,new PlayerInput(0,0,B:true));Step(m,2);m.SetInput(0,default);
                for(int i=0;i<44&&B(m).GetProperty("state").GetInt32()!=0;i++)Step(m,2);
                Assert(B(m).GetProperty("bounces").GetInt32()==(pumps>0?3:0),"Charged throw bounce limit or uncharged landing is incorrect.");
                Assert(Math.Abs(F(B(m),"z")-projection.Last.Z)<.02f,$"Bounce landing disagrees with shared guide: pumps={pumps}, expected={projection.Last}, actual={B(m)}.");
            }
        });
        Check("red blasts keep piercing cover with a larger physical radius",()=>{
            var normal=new BlastBomb{FireLevel=3};var red=new BlastBomb{FireLevel=3,Mode="red"};
            Assert(red.BlastRadius>=normal.BlastRadius*1.24f&&!red.IsRemote,"Red mode has no radius advantage.");
        });
        Check("all battle stages offer more space and pyramid has a fifth tier and open rear falls",()=>{
            Assert(BlastStages.Get(BlastStages.RockGarden).Width>17,"Rock Garden did not grow.");
            Assert(BlastStages.Get(BlastStages.UpAndDown).Width>17,"Up & Down did not grow.");
            var stage=BlastStages.Get(BlastStages.SunkenTemple);
            Assert(stage.Height>23&&stage.Width==25&&stage.At(12,1).Tier==5&&stage.At(12,3).Kind==CellKind.Ramp,"Pyramid did not gain a reachable summit.");
            Assert(Enumerable.Range(0,stage.Width).All(x=>stage.At(x,0).IsElimination),"Rear edge still has an invisible wall.");
        });
        Check("maximum charge has a shorter airborne duration and no balloon head rebound",()=>{
            var origin=new Vector3(5.5f,BombThrowRules.LaunchHeight,5.5f);
            var light=BombThrowRules.Project(origin,BombThrowRules.AimedVelocity(0,1,7),(x,z)=>0,_=>false);
            var heavy=BombThrowRules.Project(origin,BombThrowRules.AimedVelocity(0,1,7,pumps:3),(x,z)=>0,_=>false,gravityScale:BombThrowRules.Weight(3));
            Assert(heavy.Points.Count<light.Points.Count*.8f,"Large bomb still floats as long as the small one.");
            var m=Match([],victim:true);Lift(m);
            for(int i=0;i<3;i++){m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);Step(m,14);}
            m.SetInput(0,new PlayerInput(0,0,Aim:true));Step(m,2);
            m.SetInput(0,new PlayerInput(0,0,B:true));Step(m,2);m.SetInput(0,default);
            bool hit=false;float peak=0,after=0;
            for(int i=0;i<60;i++){
                Step(m,1);hit|=S(m).GetProperty("players")[1].GetProperty("stunned").GetBoolean();
                if(S(m).GetProperty("bombs").GetArrayLength()>0){var b=B(m);if(hit){peak=Math.Max(peak,F(b,"y"));after=Math.Max(after,F(b,"z"));}}
            }
            Assert(hit,"Long heavy throw missed test opponent.");
            Assert(peak<2.2f&&after<12,"Head hit relaunched the largest bomb high or far across the arena.");
        });
        Check("focus gently assists nearby visible targets while explicit steering stays in control",()=>{
            foreach(bool focus in new[]{false,true}){
                var m=Match(focus?[(5,5,"focus")]:[],victim:true);Lift(m);
                m.SetInput(0,new PlayerInput(0,0,Aim:true));Step(m,12);
                float range=F(P(m),"aimRange");
                Assert(focus?range<4.35f&&range>4.1f:Math.Abs(range-4.5f)<.01f,"Aim assist snapped or failed to nudge only focus users.");
                m.SetInput(0,new PlayerInput(0,.1f,Aim:true));Step(m,10);
                Assert(F(P(m),"aimRange")>range+.04f,"Aim assist overrode explicit cursor movement.");
            }
        });
        Check("ghosts pass through bombs and cannot carry kick charge stop or detonate them",()=>{
            var cells=new CellDef[30,25];for(int x=0;x<30;x++)for(int z=0;z<25;z++)cells[x,z]=CellDef.Floor0;
            var m=new LocalMatch(new MatchOptions{HumanPlayers=3,BotPlayers=0,SuddenDeath=false},new StageLayout{Id="ghost-bombs",Width=30,Height=25,Cells=cells,Spawns=[(5,5),(5,8),(25,20)],SoftBlockDensity=0,InitialItems=[(5,8,"remote")]});Step(m,64);
            m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);Step(m,95);
            Assert(P(m).GetProperty("ghost").GetBoolean(),"Fixture failed to create a ghost through a real explosion.");
            m.SetInput(1,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(1,new PlayerInput(1,0));Step(m,20);m.SetInput(1,default);
            var before=B(m);float bx=F(before,"x"),bz=F(before,"z");
            m.SetInput(0,new PlayerInput(0,1));Step(m,15);
            m.SetInput(0,new PlayerInput(0,1,A:true,B:true,R:true,Aim:true,Stop:true));Step(m,8);m.SetInput(0,default);Step(m,2);
            Assert(P(m).GetProperty("carrying").ValueKind==JsonValueKind.Null,"Ghost picked up a bomb.");
            Assert(S(m).GetProperty("bombs").GetArrayLength()==1&&B(m).GetProperty("state").GetInt32()==0&&F(B(m),"x")==bx&&F(B(m),"z")==bz,"Ghost moved, stopped, placed or detonated a bomb.");
            Assert(F(P(m),"z")>bz+.2f,"Ghost collided with a bomb instead of passing through.");
        });
        return count;
    }
    static LocalMatch Match((int,int,string)[] items,bool wall=false,bool victim=false)
    {
        var cells=new CellDef[30,25];for(int x=0;x<30;x++)for(int z=0;z<25;z++)cells[x,z]=CellDef.Floor0;
        if(wall)for(int x=0;x<30;x++)cells[x,9]=new CellDef{Kind=CellKind.Pillar,Tier=5};
        var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=true,SuddenDeath=false},new StageLayout{Id="equipment",Width=30,Height=25,Cells=cells,Spawns=[(5,5),victim?(5,9):(25,20)],SoftBlockDensity=0,InitialItems=items});Step(m,64);return m;
    }
    static void Lift(LocalMatch m){m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,10);m.SetInput(0,default);Step(m,2);}
    static void Step(LocalMatch m,int n){for(int i=0;i<n;i++)m.Tick();}
    static JsonElement S(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
    static JsonElement P(LocalMatch m)=>S(m).GetProperty("players")[0];
    static JsonElement B(LocalMatch m)=>S(m).GetProperty("bombs")[0];
    static float F(JsonElement e,string k)=>e.GetProperty(k).GetSingle();
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
}

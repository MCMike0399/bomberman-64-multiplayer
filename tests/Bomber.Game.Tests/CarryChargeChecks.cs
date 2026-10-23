using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;


internal static class CarryChargeChecks
{
    public static int Run()
    {
        var count=0;
        void Check(string name,Action check){check();count++;Console.WriteLine("PASS "+name);}
        Check("neutral and lightly aimed throws clear stationary owner at every pump level",()=>{
            foreach(var pumps in new[]{0,1,2,3})foreach(var strength in new[]{0f,.2f,.4f,1f}){
                var m=Match();Lift(m);Pump(m,pumps);
                m.SetInput(0,new PlayerInput(0,strength,B:true));m.Tick();m.SetInput(0,default);
                for(int tick=0;tick<24;tick++){
                    m.Tick();Assert(!Player(m).GetProperty("stunned").GetBoolean(),$"Throw knocked down owner: pumps={pumps}, strength={strength}, tick={tick+1}.");
                }
            }
        });
        Check("holding and pumping never releases carry and keeps its original fuse",()=>{
            var m=Match();Lift(m);var fuse=Number(Bomb(m),"fuse");Pump(m,3);
            m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,100);
            Assert(Player(m).GetProperty("carrying").GetString()=="bomb","Pumping released carry.");
            Assert(Number(Bomb(m),"fuse")==fuse,"Carried fuse changed.");
            m.SetInput(0,new PlayerInput(0,0,B:true));Step(m,2);
            Assert(Number(Bomb(m),"fuse")<fuse,"Thrown fuse did not resume.");
            Step(m,90);Assert(State(m).GetProperty("bombs").GetArrayLength()==0,"Charged throw never exploded.");
        });
        Check("weighted charged throws still clear their owner with room to retreat",()=>{
            for(int pumps=0;pumps<=3;pumps++){
                var m=Match();Lift(m);Pump(m,pumps);m.SetInput(0,new PlayerInput(0,0,B:true));Step(m,2);m.SetInput(0,default);Step(m,48);
                var distance=Number(Bomb(m),"z")-Number(Player(m),"z");
                Assert(distance>3,"Weighted throw did not clear the carrier.");
                Assert(distance>Bomb(m).GetProperty("effectiveLevel").GetInt32()*.9f+.4f,"Neutral charged blast overlaps throw position.");
            }
        });
        Check("thrown bomb still knocks down an opponent",()=>{
            var m=Match(victim:true);Lift(m);m.SetInput(0,new PlayerInput(0,0,B:true));m.Tick();m.SetInput(0,default);
            bool hit=false;for(int i=0;i<28;i++){m.Tick();hit|=Player(m,1).GetProperty("knockdownReason").GetString()=="falling-bomb";}
            Assert(hit,"Safe launch disabled enemy head hits.");
        });
        Check("owner chasing their own descending bomb can still get hit",()=>{
            var m=Match();Lift(m);m.SetInput(0,new PlayerInput(0,0,B:true));m.Tick();m.SetInput(0,new PlayerInput(0,1));
            bool hit=false;for(int i=0;i<28;i++){m.Tick();hit|=Player(m).GetProperty("knockdownReason").GetString()=="falling-bomb";}
            Assert(hit,"Launch fix granted owner immunity against returning contact.");
        });
        Check("carried metadata and launch share a continuous overhead height",()=>{
            var m=Match();Lift(m);var held=Number(Bomb(m),"y");
            Assert(Math.Abs(held-Number(Player(m),"y")-BombThrowRules.LaunchHeight)<.01f,"Carried metadata drifted from the shared overhead launch height.");
            m.SetInput(0,new PlayerInput(0,0,B:true));m.Tick();
            Assert(Number(Bomb(m),"y")>=held,"Actual launch dropped below its carried height.");
        });
        Check("shared advisory landing matches actual authoritative throw",()=>{
            foreach(int pumps in new[]{0,3}){
                var m=Match();Lift(m);Pump(m,pumps);
                var p=Player(m);
                var projected=BombThrowRules.Project(new(Number(p,"x"),Number(p,"y")+BombThrowRules.LaunchHeight,Number(p,"z")),
                    BombThrowRules.Velocity(Number(p,"fx"),Number(p,"fz"),0,pumps),(x,z)=>0,_=>false,Number(Bomb(m),"fuse"),gravityScale:BombThrowRules.Weight(pumps));
                m.SetInput(0,new PlayerInput(0,0,B:true));Step(m,2);m.SetInput(0,default);Step(m,48);
                Assert(Math.Abs(Number(Bomb(m),"x")-projected.Last.X)<.01f&&Math.Abs(Number(Bomb(m),"z")-projected.Last.Z)<.01f,
                     $"Shared target {projected.Last}, actual {Bomb(m)}, pumps={pumps}.");
                Assert(projected.End==ThrowProjectionEnd.Landing,"Unobstructed advisory failed to reach terrain.");
            }
        });
        Check("advisory arc truncates at cover and respects an already short fuse",()=>{
            var origin=new System.Numerics.Vector3(6.5f,.9f,6.5f);var velocity=BombThrowRules.Velocity(0,1,0,3);
            var blocked=BombThrowRules.Project(origin,velocity,(x,z)=>0,p=>p.Z>8);
            Assert(blocked.End==ThrowProjectionEnd.Obstructed&&blocked.Last.Z<=8,"Advisory drew through cover.");
            var shortFuse=BombThrowRules.Project(origin,velocity,(x,z)=>0,_=>false,.01f);
            Assert(shortFuse.End==ThrowProjectionEnd.Fuse&&shortFuse.Last==origin,"Advisory promised flight beyond remaining fuse.");
        });
        Check("water and void advisories match real removal instead of promising a resting bomb",()=>{
            foreach(var kind in new[]{CellKind.Water,CellKind.Void}){
                var m=Match(elimination:kind);Lift(m);var player=Player(m);
                var projection=BombThrowRules.Project(new(Number(player,"x"),Number(player,"y")+BombThrowRules.LaunchHeight,Number(player,"z")),
                    BombThrowRules.Velocity(0,1,0,0),(x,z)=>0,_=>false,Number(Bomb(m),"fuse"),(x,z)=>z>=8?kind:CellKind.Floor);
                Assert(projection.End==(kind==CellKind.Water?ThrowProjectionEnd.Water:ThrowProjectionEnd.Void),"Elimination endpoint was mislabeled as a resting landing.");
                Assert(projection.Last.Z>=8,"Fixture projection never reached the elimination surface.");
                m.SetInput(0,new PlayerInput(0,0,B:true));Step(m,2);m.SetInput(0,default);Step(m,40);
                Assert(State(m).GetProperty("bombs").GetArrayLength()==0&&Player(m).GetProperty("activeBombs").GetInt32()==0,
                    "Actual eliminated bomb did not disappear and release capacity before its fuse.");
                Assert(!State(m).GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="blast"),
                    "Elimination fixture unexpectedly reached its fuse instead of terrain removal.");
            }
        });
        return count;
    }
    static LocalMatch Match(bool victim=false,CellKind? elimination=null)
    {
        var c=new CellDef[21,21];for(int x=0;x<21;x++)for(int z=0;z<21;z++)c[x,z]=CellDef.Floor0;
        if(elimination is {} kind)for(int x=0;x<21;x++)for(int z=8;z<21;z++)c[x,z]=new CellDef{Kind=kind};
        var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=true},new StageLayout{Id="fixture",Width=21,Height=21,Cells=c,Spawns=[(6,6),victim?(6,9):elimination is not null?(18,6):(18,18)],SoftBlockDensity=0});Step(m,64);return m;
    }
    static void Lift(LocalMatch m){m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,10);Assert(Player(m).GetProperty("carrying").GetString()=="bomb","Fixture did not lift.");m.SetInput(0,default);Step(m,2);}
    static void Pump(LocalMatch m,int pumps){for(int i=0;i<pumps;i++){m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);Step(m,14);}}
    static void Step(LocalMatch m,int ticks){for(int i=0;i<ticks;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
    static JsonElement Player(LocalMatch m,int index=0)=>State(m).GetProperty("players")[index];
    static JsonElement Bomb(LocalMatch m)=>State(m).GetProperty("bombs")[0];
    static float Number(JsonElement element,string property)=>element.GetProperty(property).GetSingle();
    static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}

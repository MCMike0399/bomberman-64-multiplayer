using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;

internal static class BombPlacementChecks
{
    public static int Run()
    {
        int n=0;void Check(string name,Action test){test();n++;Console.WriteLine("PASS "+name);}
        Check("A accepts an empty flat spot at a fractional grid boundary",()=>{
            var m=Match();m.SetInput(0,new PlayerInput(1,0));Step(m,3);m.SetInput(0,default);Step(m,2);
            Tap(m);Assert(Bombs(m).GetArrayLength()==1,"A was rejected with zero active bombs on an empty flat floor.");
        });
        Check("A drops near the feet rather than a distant forward circle",()=>{
            var m=Match();Tap(m);var p=Player(m);var b=Bombs(m)[0];
            Assert(Distance(p,b)<=.3f,"Placement is too far in front of the feet.");
        });
        Check("standing beside stone still permits a supported near-foot bomb",()=>{
            var m=Match(wall:true);Tap(m);
            Assert(Bombs(m).GetArrayLength()==1,"Facing adjacent stone swallowed the A press.");
            Assert(F(Bombs(m)[0],"z")+.35f<=4.001f,"Foot placement penetrated stone.");
        });
        Check("owner can wait then depart any direction without automatically kicking",()=>{
            foreach(var (x,z) in new[]{(0f,1f),(0f,-1f),(1f,0f),(-1f,0f),(.7071f,.7071f)}){
                var m=Match();Tap(m);Step(m,15);var start=Player(m);var bomb=Bombs(m)[0];
                m.SetInput(0,new PlayerInput(x,z));Step(m,10);m.SetInput(0,default);
                Assert(Distance(start,Player(m))>1.3f,"Planted bomb blocked its owner's departure.");
                Assert(Distance(bomb,Bombs(m)[0])<.01f&&!Events(m).Any(e=>Kind(e)=="kick"),"Walking away launched a bomb without a deliberate kick.");
            }
        });
        Check("a fresh second A deliberately kicks a stationary near-foot bomb",()=>{
            var m=Match();Tap(m);Tap(m);
            Assert(Events(m).Any(e=>Kind(e)=="kick-windup"),"Second A did not commit a kick.");
            Step(m,6);Assert(Bombs(m)[0].GetProperty("state").GetInt32()==1,"Second A never released its planted strike.");
            Assert(Bombs(m).GetArrayLength()==1,"Second A created an overlapping duplicate.");
        });
        Check("repeated A taps leave three stationary bombs while walking",()=>{
            var m=Match();for(int i=0;i<3;i++){
                m.SetInput(0,new PlayerInput(1,0,A:true));Step(m,1);m.SetInput(0,new PlayerInput(1,0));Step(m,9);
            }
            m.SetInput(0,default);
            Assert(Bombs(m).GetArrayLength()==3,"Walking taps lost an available placement.");
            Assert(Bombs(m).EnumerateArray().All(b=>b.GetProperty("state").GetInt32()==0),"Walking trail accidentally kicked a bomb.");
            Assert(!Events(m).Any(e=>Kind(e)=="kick-windup"),"Placing a walking trail interrupted movement.");
        });
        Check("holding A while departing never picks the bomb back up from behind",()=>{
            var m=Match();m.SetInput(0,new PlayerInput(1,0,A:true));Step(m,12);
            Assert(Player(m).GetProperty("carrying").ValueKind==JsonValueKind.Null,"Moving A hold retrieved the departed bomb from behind.");
            Assert(Bombs(m).GetArrayLength()==1&&Bombs(m)[0].GetProperty("state").GetInt32()==0,"Moving hold changed the stationary placed bomb.");
        });
        return n;
    }
    static LocalMatch Match(bool wall=false){var cells=new CellDef[15,15];for(int x=0;x<15;x++)for(int z=0;z<15;z++)cells[x,z]=CellDef.Floor0;if(wall)cells[3,4]=CellDef.Pillar;
        var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=true},new StageLayout{Id="foot-placement",Width=15,Height=15,Cells=cells,Spawns=[(3,3),(12,12)],SoftBlockDensity=0});Step(m,64);return m;}
    static void Tap(LocalMatch m){m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);Step(m,2);}
    static void Step(LocalMatch m,int n){for(int i=0;i<n;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
    static JsonElement Bombs(LocalMatch m)=>State(m).GetProperty("bombs");
    static JsonElement Player(LocalMatch m)=>State(m).GetProperty("players")[0];
    static IEnumerable<JsonElement> Events(LocalMatch m)=>State(m).GetProperty("events").EnumerateArray();
    static string? Kind(JsonElement e)=>e.GetProperty("kind").GetString();
    static float F(JsonElement e,string k)=>e.GetProperty(k).GetSingle();
    static float Distance(JsonElement a,JsonElement b)=>MathF.Sqrt(MathF.Pow(F(a,"x")-F(b,"x"),2)+MathF.Pow(F(a,"z")-F(b,"z"),2));
    static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}

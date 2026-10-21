using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;

internal static class KickTimingChecks
{
    public static int Run()
    {
        int n=0;
        void Check(string name,Action test){test();Console.WriteLine("PASS "+name);n++;}
        Check("contact kick plants the player before launching instead of walking through",()=>{
            var m=Match();Place(m);var bomb=Bomb(m);float bx=F(bomb,"x"),bz=F(bomb,"z");
            m.SetInput(0,new PlayerInput(0,1));Step(m,2);
            Assert(Bomb(m).GetProperty("state").GetInt32()==0,"Bomb launched immediately: no planted anticipation.");
            float z=F(Player(m),"z");Step(m,2);
            Assert(Math.Abs(F(Player(m),"z")-z)<.01f,"Kicker walked during the planted wind-up.");
            Assert(Math.Abs(F(Bomb(m),"z")-bz)<.01f&&Math.Abs(F(Bomb(m),"x")-bx)<.01f,"Bomb moved before foot strike.");
            Step(m,3);Assert(Bomb(m).GetProperty("state").GetInt32()==1,"Committed kick failed to release after its short wind-up.");
            Step(m,10);Assert(F(Player(m),"z")>z+.5f,"Kick recovery never returns running control.");
        });
        Check("committed kick retains direction while player turns stick during anticipation",()=>{
            var m=Match();Place(m);m.SetInput(0,new PlayerInput(0,1));Step(m,2);
            float x=F(Bomb(m),"x"),z=F(Bomb(m),"z");m.SetInput(0,new PlayerInput(1,0));Step(m,8);
            Assert(Math.Abs(F(Bomb(m),"x")-x)<.01f&&F(Bomb(m),"z")>z+1,"Stick redirection changed the committed foot strike.");
        });
        Check("kick continues down a long open runway without being pushed",()=>{
            var m=Match();Place(m);m.SetInput(0,new PlayerInput(0,1));Step(m,2);m.SetInput(0,default);
            Step(m,6);float z=F(Bomb(m),"z");Step(m,30);
            Assert(F(Bomb(m),"z")>z+11.5f,"Kick lost momentum across the open runway.");
            Assert(Bomb(m).GetProperty("state").GetInt32()==1,"Unobstructed shot stopped without a collision.");
        });
        Check("bomb fuse expires during kick anticipation without phantom launch or frozen player",()=>{
            var m=Match();Place(m);while(F(Bomb(m),"fuse")>.09f)Step(m,1);m.SetInput(0,new PlayerInput(0,1));Step(m,10);
            Assert(State(m).GetProperty("bombs").GetArrayLength()==0,"Anticipation paused the bomb fuse.");
            var kicks=State(m).GetProperty("events").EnumerateArray().Where(e=>e.GetProperty("kind").GetString()=="kick").Count();
            Assert(kicks==0,"Expired bomb produced a phantom kick.");
        });
        Check("kick impact is a low short stumble rather than a long launch",()=>{
            var m=Match(victim:true);Place(m);float z=F(State(m).GetProperty("players")[1],"z"),peak=0,travel=0;int flight=0;
            m.SetInput(0,new PlayerInput(0,1));Step(m,2);m.SetInput(0,default);
            for(int i=0;i<60;i++){m.Tick();var p=State(m).GetProperty("players")[1];peak=Math.Max(peak,F(p,"y"));travel=Math.Max(travel,F(p,"z")-z);if(p.GetProperty("airborne").GetBoolean())flight++;}
            Assert(peak>.1f&&peak<=.4f,$"Impact height is excessive or unreadable: {peak}.");
            Assert(travel>.5f&&travel<=1.3f,$"Impact should move only a short body-width distance: {travel}.");
            Assert(flight>0&&flight<=20,$"Hit remained airborne too long: {flight} ticks.");
        });
        return n;
    }
    static LocalMatch Match(bool victim=false){var cells=new CellDef[15,40];for(int x=0;x<15;x++)for(int z=0;z<40;z++)cells[x,z]=CellDef.Floor0;
        var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=true},new StageLayout{Id="kick-timing",Width=15,Height=40,Cells=cells,Spawns=[(3,3),victim?(3,7):(12,35)],SoftBlockDensity=0});Step(m,64);return m;}
    static void Place(LocalMatch m){m.SetInput(0,new PlayerInput(0,0,A:true));Step(m,2);m.SetInput(0,default);}
    static void Step(LocalMatch m,int n){for(int i=0;i<n;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var d=JsonDocument.Parse(m.StateJson!);return d.RootElement.GetProperty("d").Clone();}
    static JsonElement Bomb(LocalMatch m)=>State(m).GetProperty("bombs")[0];
    static JsonElement Player(LocalMatch m)=>State(m).GetProperty("players")[0];
    static float F(JsonElement e,string k)=>e.GetProperty(k).GetSingle();
    static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}

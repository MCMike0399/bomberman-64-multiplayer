using System.Text.Json;
using Bomber.Game;
using Bomber.Game.Simulation;
using static GameFixtures;

internal static class TutorialChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool condition,string message){Assert(condition,message);checks++;}
        LocalMatch Start(string id)
        {
            var match=new LocalMatch(new MatchOptions {Stage=BlastStages.CircuitGarden,Practice=true,BotPlayers=0,TutorialLesson=id,BotDifficulty="easy"});
            Step(match,62);
            Check(Data(match.StageJson!).GetProperty("id").GetString()==BlastStages.CircuitGarden,"Lesson uses authored garden");
            Check(!Done(match),id+" does not complete itself");return match;
        }
        foreach(var invalid in new[]{new MatchOptions{Stage=BlastStages.CircuitGarden},new MatchOptions{Practice=true,Stage=BlastStages.CircuitGarden,TutorialLesson="missing",BotPlayers=0},new MatchOptions{TutorialLesson="move"}})
        {
            bool rejected=false;try{_ = new LocalMatch(invalid);}catch(ArgumentException){rejected=true;}
            Check(rejected,"Invalid/competitive tutorial configuration rejected");
        }
        foreach(var lesson in TutorialCourse.Lessons)
        {
            var m=Start(lesson.Id);
            switch(lesson.Id)
            {
                case "move":Go(m,11.5f,12.5f);break;
                case "bomb":Plant(m);Go(m,5.5f,12.5f);Advance(m,100);break;
                case "carry":Carry(m);Input(m,default,4);break;
                case "primary-hold":Input(m,new(0,0,A:true),12);Input(m,default,2);Input(m,new(0,0,B:true),4);break;
                case "pump":Carry(m);Pump(m,3);Input(m,default,4);break;
                case "aim":Carry(m);Input(m,new(0,1,B:true,Aim:true),18);Input(m,default,4);break;
                case "kick":Kick(m);Input(m,new(0,0,Stop:true),4);break;
                case "chain":Plant(m);Go(m,8.5f,13.6f);Plant(m);Go(m,5.5f,13.6f);Advance(m,100);break;
                case "swap":foreach(float x in new[]{9.5f,11.5f,13.5f,15.5f})Go(m,x,12.5f);break;
                case "expiry":Go(m,9.5f,12.5f);Advance(m,330);break;
                case "cover":Go(m,11.2f,12.5f);Plant(m);Go(m,8.5f,12.5f);Advance(m,100);break;
                case "items":Go(m,9.5f,12.5f);Go(m,11.5f,12.5f);break;
                case "remote":Go(m,9.5f,12.5f);Plant(m);Go(m,5.5f,12.5f);Input(m,new(0,0,R:true),2);Advance(m,18);break;
                case "red":Go(m,9.5f,12.5f);Go(m,11.2f,12.5f);Plant(m);Go(m,7.5f,12.5f);Advance(m,100);break;
                case "bounce":Go(m,9.5f,12.5f);Carry(m);Pump(m,1);Input(m,default,2);Advance(m,80);break;
                case "heart":Go(m,9.5f,12.5f);Plant(m);Advance(m,100);break;
                case "focus":Go(m,9.5f,12.5f);Carry(m);Pump(m,1);break;
                case "effects":foreach(float x in new[]{9.5f,11.5f,13.5f,15.5f})Go(m,x,12.5f);break;
                case "terrain":case "flood":
                    Go(m,11.5f,12.5f);Go(m,11.5f,1.5f);
                    if(lesson.Id=="terrain")Go(m,11.5f,9.5f);else Advance(m,420);break;
                case "recovery":case "escape":
                    for(int i=0;i<7&&!Done(m);i++){Input(m,new(0,0,A:true),4);Input(m,default,4);}Advance(m,140);break;
                case "impact":Go(m,10.5f,12.5f);Input(m,new(1,0),1);Kick(m);Advance(m,10);break;
                case "grapple":Input(m,new(1,0),4);Input(m,new(0,0,B:true),4);Input(m,default,4);break;
                case "ghost":Plant(m);Advance(m,90);Go(m,12.5f,12.5f);break;
                case "shake":for(int i=0;i<8&&!Done(m);i++)Input(m,new(i%2==0?1:-1,0),6);break;
                case "duel":
                    Go(m,9.5f,12.5f);Go(m,7.5f,12.5f);
                    Carry(m);Pump(m,3);
                    Input(m,new(1,0,B:true),2);
                    for(int t=0;t<35;t++)
                    {
                        var rival=Player(m,1);var me=Player(m);
                        float dx=rival.GetProperty("x").GetSingle()-me.GetProperty("x").GetSingle()-me.GetProperty("aimX").GetSingle()*me.GetProperty("aimRange").GetSingle();
                        float dz=rival.GetProperty("z").GetSingle()-me.GetProperty("z").GetSingle()-me.GetProperty("aimZ").GetSingle()*me.GetProperty("aimRange").GetSingle();
                        Input(m,new(dx,dz,B:true,Aim:true),1);
                        if(t>0&&Math.Abs(dx)+Math.Abs(dz)<.3f)break;
                    }
                    Input(m,default,2);
                    for(int t=0;t<90&&!Done(m)&&!Failed(m);t++)
                    {
                        var bomb=State(m).GetProperty("bombs").EnumerateArray().FirstOrDefault(b=>b.GetProperty("ownerId").GetString()=="local-0");
                        var rival=Player(m,1);
                        if(bomb.ValueKind!=JsonValueKind.Undefined)
                        {
                            float dx=bomb.GetProperty("x").GetSingle()-rival.GetProperty("x").GetSingle();
                            float dz=bomb.GetProperty("z").GetSingle()-rival.GetProperty("z").GetSingle();
                            float dy=bomb.GetProperty("y").GetSingle()-rival.GetProperty("y").GetSingle();
                            if(dx*dx+dy*dy+dz*dz<3.7f*3.7f){Input(m,new(0,0,R:true),2);Input(m,default,2);Advance(m,20);break;}
                        }
                        Input(m,default,1);
                    }
                    break;
            }
            Check(Done(m),$"{lesson.Id} is achievable with ordinary inputs: {State(m).GetProperty("tutorial")} player={Player(m)} events={string.Join(";",Events(m).Select(e=>e.ToString()))}");
            var pos=(Player(m).GetProperty("x").GetSingle(),Player(m).GetProperty("z").GetSingle());
            Input(m,new(1,0,A:true),40);
            Check(pos==(Player(m).GetProperty("x").GetSingle(),Player(m).GetProperty("z").GetSingle()),"Completed lesson waits safely for explicit continuation");
            Console.WriteLine("PASS tutorial "+lesson.Id);
        }
        var red=Start("red");Go(red,8.5f,10.5f);Go(red,11.4f,10.5f);Go(red,11.4f,12.0f);
        Plant(red);Go(red,8.5f,10.5f);Advance(red,100);
        Go(red,9.5f,12.5f);Plant(red);Go(red,5.5f,12.5f);Advance(red,100);
        Check(!Done(red),"A red blast elsewhere cannot claim cover destroyed earlier by a normal bomb");
        var failure=Start("bomb");Plant(failure);Advance(failure,100);
        Check(Failed(failure)&&!Done(failure),"Self-elimination fails bomb safety lesson");
        var retry=Start("bomb");Check(State(retry).GetProperty("bombs").GetArrayLength()==0&&!Failed(retry),"Retry starts with clean arena and progress");
        var idle=Start("move");Advance(idle,180);Check(!Done(idle),"Elapsed time cannot clear movement objective");
        var garden=BlastStages.Get(BlastStages.CircuitGarden);
        Check(garden.At(11,7).Kind==CellKind.Ramp&&garden.At(11,1).Tier==2&&garden.At(19,12).Kind==CellKind.Water,"Garden has connected elevation and visible canal");
        Console.WriteLine($"Passed {checks} tutorial integration checks.");return checks;
    }
    public static int ReviewRegressions()
    {
        var failures=new List<string>();int passed=0;
        LocalMatch Start(string id){var m=new LocalMatch(new MatchOptions{Stage=BlastStages.CircuitGarden,Practice=true,BotPlayers=0,TutorialLesson=id,BotDifficulty="easy"});Step(m,62);return m;}
        void Run(string name,Action check){try{check();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failures.Add(name+": "+e.Message);Console.WriteLine("FAIL "+name+": "+e.Message);}}
        Run("planting must survive its own bomb",()=>{var m=Start("bomb");Carry(m);Input(m,default,2);Go(m,5.5f,12.5f);Advance(m,120);Plant(m);Assert(!Done(m),"an earlier thrown blast cleared a newly planted live bomb");});
        Run("Focus requires a charge completed with Focus",()=>{var m=Start("focus");Carry(m);Pump(m,1);Input(m,new(1,0,B:true),4);Assert(!Done(m),"merely collecting Focus credited a previous normal charge");Pump(m,1);Assert(Done(m),"a real Focus charge did not complete");});
        Run("ghost stays on dry land and can still finish",()=>{var m=Start("ghost");Plant(m);Advance(m,90);Assert(Player(m).GetProperty("ghost").GetBoolean(),"first death did not create a ghost");Input(m,new(-1,0),110);Assert(Player(m).GetProperty("alive").GetBoolean()&&!Failed(m),"ghost bypassed its land boundary");Go(m,12.5f,12.5f);Assert(Done(m),"ghost could not return from the edge and latch");});
        Run("graduation rival attacks normally",()=>{var m=Start("duel");bool attacked=false;m.Message+=json=>{var d=Data(json);if(d.TryGetProperty("bombs",out var bombs)&&bombs.EnumerateArray().Any(b=>b.GetProperty("ownerId").GetString()=="tutorial-partner"))attacked=true;};Advance(m,300);Assert(attacked,"the Easy CPU never placed a bomb");});
        if(failures.Count>0)throw new InvalidOperationException(string.Join("\n",failures));
        Console.WriteLine($"Passed {passed} tutorial review regressions.");return passed;
    }
    private static bool Done(LocalMatch m)=>State(m).GetProperty("tutorial").GetProperty("complete").GetBoolean();
    private static bool Failed(LocalMatch m)=>State(m).GetProperty("tutorial").GetProperty("failed").GetBoolean();
    private static void Advance(LocalMatch m,int ticks){for(int i=0;i<ticks&&!Done(m)&&!Failed(m);i++)m.Tick();Step(m,2);}
    private static void Input(LocalMatch m,PlayerInput input,int ticks){m.SetInput(0,input);Advance(m,ticks);}
    private static void Plant(LocalMatch m){Input(m,new(0,0,A:true),2);Input(m,default,2);}
    private static void Carry(LocalMatch m){Input(m,new(0,0,B:true),2);Input(m,new(0,0,A:true,B:true),2);Input(m,new(0,0,B:true),2);}
    private static void Pump(LocalMatch m,int count){for(int i=0;i<count;i++){Input(m,new(0,0,A:true,B:true),2);Input(m,new(0,0,B:true),18);}}
    private static void Kick(LocalMatch m){Plant(m);Input(m,new(0,0,A:true),10);Input(m,default,2);}
    private static void Go(LocalMatch m,float x,float z)
    {
        for(int tick=0;tick<400&&!Done(m)&&!Failed(m);tick++)
        {
            var p=Player(m);float dx=x-p.GetProperty("x").GetSingle(),dz=z-p.GetProperty("z").GetSingle();
            if(Math.Abs(dx)<.13f&&Math.Abs(dz)<.13f){Input(m,default,2);return;}
            Input(m,new(Math.Clamp(dx/.3f,-1,1),Math.Clamp(dz/.3f,-1,1)),1);
        }
        Input(m,default,2);
    }
}

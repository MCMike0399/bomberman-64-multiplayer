using Bomber.Client;
using Bomber.Game;
using System.Text.Json;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task SandboxEquipment()
    {
        _root.StartLocal(new SessionSettings(Bots:0){Advanced=new AdvancedRules{ItemSeconds=60,DisabledItems="bomb,heart,remote,tiny,big,slow"}},1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"sandbox practice reaches fight",150);
        Check(State.GetProperty("items").EnumerateArray().All(i=>i.GetProperty("type").GetString()!="fast"),"practice cache contains no Fast bombs");
        await Capture("expanded-pyramid-and-equipment");
        await MoveTo(8.5f,12.5f,100);await MoveTo(8.5f,13.5f,40);
        Check(Player.GetProperty("ability").GetString()=="focus","ordinary movement collects throwing ability");
        Input(0,new PlayerInput(0,0,A:true));await Frames(12);Input(0,default);await Frames(2);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);
        Check(State.GetProperty("bombs")[0].GetProperty("charging").GetBoolean(),"focus charge publishes visible progress before completion");
        await Capture("focus-charge-building",0);
        Input(0,default);await Frames(5);
        Check(State.GetProperty("bombs")[0].GetProperty("pumps").GetInt32()==1,"focus completes one deliberate charge quickly");
        Input(0,new PlayerInput(0,0,B:true));await Frames(2);Input(0,default);await Frames(85);
        await MoveTo(12.5f,12.5f,100);await MoveTo(16.5f,12.5f,100);
        Check(Player.GetProperty("bombType").GetString()=="bounce","ordinary movement collects bouncing bombs");
        Input(0,new PlayerInput(0,1));await Frames(1);Input(0,new PlayerInput(0,0,A:true));await Frames(12);Input(0,default);await Frames(3);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(14);
        Check(State.GetProperty("bombs")[0].GetProperty("pumps").GetInt32()>0,"bouncing throw completes a real charge before release");
        await Capture("bouncing-bomb-held");
        Input(0,new PlayerInput(0,0,B:true));await Frames(2);Input(0,default);
        await WaitState(s=>s.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("bounces").GetInt32()>0),"bouncing throw rebounds from courtyard",90);
        await Capture("bouncing-bomb-courtyard");await Frames(110);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
        Input(0,new PlayerInput(0,0,A:true));await Frames(6);Input(0,default);
        await WaitState(s=>s.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==1),"kicked bounce bomb rolls normally",25);
        await Capture("bouncing-kick-normal-roll",0);
        Input(0,new PlayerInput(0,0,Stop:true));await Frames(2);Input(0,default);
        float stoppedX=State.GetProperty("bombs")[0].GetProperty("x").GetSingle(),stoppedZ=State.GetProperty("bombs")[0].GetProperty("z").GetSingle();
        await Frames(28);
        Check(State.GetProperty("bombs")[0].GetProperty("state").GetInt32()==0&&Math.Abs(State.GetProperty("bombs")[0].GetProperty("x").GetSingle()-stoppedX)<.01f&&Math.Abs(State.GetProperty("bombs")[0].GetProperty("z").GetSingle()-stoppedZ)<.01f,"Stop cancels the rolling bouncing bomb");
        await Capture("bouncing-kick-stopped");Input(0,new PlayerInput(-1,0));await Frames(26);Input(0,default);await Frames(80);
        await MoveTo(14.5f,12.5f,80);await MoveTo(14.5f,13.5f,40);
        Check(Player.GetProperty("bombType").GetString()=="red","red pickup replaces bounce slot");
        Check(State.GetProperty("items").EnumerateArray().Any(i=>i.GetProperty("type").GetString()=="bounce"&&i.GetProperty("dropped").GetBoolean()),"old bouncing equipment becomes visible arena loot");
        await Capture("red-bombs-and-swap-drop");
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,new PlayerInput(1,0));await Frames(36);Input(0,default);
        await WaitState(s=>s.GetProperty("hazards").EnumerateArray().Any(h=>h.GetProperty("red").GetBoolean()),"red bomb emits actual amplified blast",100);
        await Capture("red-bomb-blue-flame",0);await Frames(50);
        await MoveTo(18.5f,16.5f,100);await MoveTo(10.5f,16.5f,100);
        await WaitState(_=>Player.GetProperty("statusEffect").GetString()=="haste","courtyard route collects Haste",40);
        Check(Player.GetProperty("statusEffect").GetString()=="haste"&&Player.GetProperty("bombType").GetString()=="red","Haste speeds the bomber while retaining red equipment");
        float before=Player.GetProperty("x").GetSingle();Input(0,new PlayerInput(1,0));await Frames(10);Input(0,default);
        Check(Player.GetProperty("x").GetSingle()-before>2,"Haste increases real player movement");
        await Capture("haste-player-with-red-bombs");
        await WaitState(_=>Player.GetProperty("statusEffect").GetString()=="none","Haste expires like other temporary effects",330);
        Check(Player.GetProperty("bombType").GetString()=="red","Haste expiry retains the bomb slot");
        await MoveTo(8.5f,16.5f,60);await MoveTo(8.5f,12.5f,80);
        await MoveTo(12.5f,12.5f,100);await MoveTo(12.5f,2.5f,220);
        Check(Player.GetProperty("y").GetSingle()==5,"four real stair flights reach expanded summit");
        await Capture("fifth-tier-summit");
        Input(0,new PlayerInput(0,-1));
        await WaitState(_=>Player.GetProperty("falling").GetBoolean(),"rear summit edge allows a fall instead of an invisible wall",65);
        Input(0,default);await Capture("pyramid-rear-pitfall",0);
        await WaitState(s=>s.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="death"&&e.GetProperty("reason").GetString()=="water"),"rear fall reaches water and respawns in practice",100);
        _root.Back();await Frames(3);
    }
}

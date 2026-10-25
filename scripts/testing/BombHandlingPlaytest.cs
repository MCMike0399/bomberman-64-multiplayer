using Godot;
using Bomber.Client;
using Bomber.Game;
using System.Text.Json;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private JsonElement HandlingBomb=>State.GetProperty("bombs")[0];
    private async Task HandlingPractice()
    {
        if(_root.ActiveSession!=null){Input(0,default);_root.Back();await Frames(3);}
        Input(0,default);_root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0){Advanced=new AdvancedRules{DisabledItems="red,bounce,heart,focus,tiny,big,haste,slow"}},1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"bomb handling reaches stock temple training",150);
    }
    private async Task BombHandling()
    {
        Event("bomb-handling-policy",new {stage="sunken-temple",source="real authority movement and Nintendo A/B actions",fixtures=false,stateMutation=false});
        await HandlingPractice();
        float px=Player.GetProperty("x").GetSingle(),pz=Player.GetProperty("z").GetSingle();
        int lifts=Cue("lift"),throws=Cue("throw"),pumps=Cue("pump");
        Input(0,new PlayerInput(0,0,A:true));await Frames(3);
        Check(State.GetProperty("bombs").GetArrayLength()==1,"Nintendo A places exactly one normal bomb");
        var bomb=HandlingBomb;
        Check(Math.Abs(bomb.GetProperty("x").GetSingle()-px)<.05f&&Math.Abs(bomb.GetProperty("z").GetSingle()-pz)<=.3f,"normal bomb is placed near the feet with only a small forward bias");
        Check(!bomb.GetProperty("remote").GetBoolean()&&bomb.GetProperty("state").GetInt32()==0,"short A starts a resting normal fused bomb");
        float fuse=bomb.GetProperty("fuse").GetSingle();
        await Capture("bomb-a-foot-placement",0);
        await Frames(10);
        Check(Player.GetProperty("carrying").GetString()=="bomb"&&HandlingBomb.GetProperty("state").GetInt32()==2,"holding Nintendo A alone lifts its fresh near-foot bomb");
        Check(Cue("lift")==lifts+1,"A-only lift produces one accepted lift cue");
        Check(HandlingBomb.GetProperty("fuse").GetSingle()<=fuse,"normal bomb retains its ordinary fuse metadata after lift");
        Input(0,default);await Frames(4);
        Check(Player.GetProperty("carrying").GetString()=="bomb","releasing Nintendo A leaves the bomb held overhead");
        Check(HandlingBomb.GetProperty("pumps").GetInt32()==0,"initial A hold lifts without silently pumping");
        await Capture("bomb-a-held-release",0);
        for(int charge=1;charge<=3;charge++)
        {
            Input(0,new PlayerInput(0,0,A:true));await Frames(16);
            Check(HandlingBomb.GetProperty("pumps").GetInt32()==charge,$"fresh Nintendo A press pumps held bomb to stage {charge}");
            await Frames(8);
            Check(HandlingBomb.GetProperty("pumps").GetInt32()==charge,"holding A does not repeat a pump");
            Input(0,default);await Frames(3);
        }
        Check(Cue("pump")==pumps+3,"three fresh A presses produce exactly three pump cues");
        await Capture("bomb-a-pumped-large",0);
        Input(0,new PlayerInput(0,0,B:true));await Frames(3);
        Check(Player.GetProperty("carrying").ValueKind==JsonValueKind.Null&&HandlingBomb.GetProperty("state").GetInt32()==3,"Nintendo B press throws the A-latched bomb without requiring B release");
        Check(Cue("throw")==throws+1,"B press produces exactly one accepted throw cue");
        await Capture("bomb-b-throw-flight",0);Input(0,default);
        await WaitState(s=>s.GetProperty("hazards").GetArrayLength()>0,"thrown normal bomb detonates through its own fuse",130);
        Check(State.GetProperty("bombs").GetArrayLength()==0,"normal detonation consumes the bomb");await Capture("bomb-normal-fuse-detonation",0);

        await HandlingPractice();
        Input(0,new PlayerInput(0,0,A:true));await Frames(3);Input(0,default);await Frames(3);
        Input(0,new PlayerInput(0,0,B:true));await Frames(3);
        Check(Player.GetProperty("carrying").GetString()=="bomb","legacy Nintendo B hold still picks up a ground bomb");
        Input(0,default);await Frames(3);
        Check(Player.GetProperty("carrying").ValueKind==JsonValueKind.Null&&HandlingBomb.GetProperty("state").GetInt32()==3,"legacy Nintendo B release still throws its picked-up bomb");

        await HandlingPractice();await GrappleWalk(0,10,13);
        Check(Player.GetProperty("remote").GetBoolean(),"remote behavior is enabled by collecting the real temple cache pickup");
        // Turn toward the broad terrace; holding A lifts the bomb through the same ordinary input path.
        Input(0,new PlayerInput(1,0));await Frames(1);Input(0,new PlayerInput(0,0,A:true));await Frames(13);Input(0,default);await Frames(3);
        Check(Player.GetProperty("carrying").GetString()=="bomb"&&HandlingBomb.GetProperty("remote").GetBoolean(),"A-only holding also carries an explicitly marked remote bomb");
        await Capture("bomb-remote-held",0);
        Input(0,new PlayerInput(0,0,B:true));await Frames(3);Input(0,default);
        await WaitState(_=>State.GetProperty("bombs").GetArrayLength()==1&&HandlingBomb.GetProperty("state").GetInt32()==0,"remote bomb completes real thrown flight",100);
        await Frames(90);
        Check(State.GetProperty("bombs").GetArrayLength()==1&&HandlingBomb.GetProperty("remote").GetBoolean(),"remote bomb remains present beyond the normal fuse duration");
        Check(State.GetProperty("hazards").GetArrayLength()==0,"remote bomb never silently converts to a timed explosion");await Capture("bomb-remote-awaiting-command",0);
        Input(0,new PlayerInput(0,0,R:true));await Frames(3);Input(0,default);
        Check(State.GetProperty("bombs").GetArrayLength()==0&&State.GetProperty("hazards").GetArrayLength()>0,"remote command explicitly detonates the waiting remote bomb");await Capture("bomb-remote-command-detonation",0);
        Input(0,default);_root.Back();await Frames(3);
    }
}

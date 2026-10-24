using Godot;
using Bomber.Client;
using Bomber.Game;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private float BodyDistance()
    {
        float dx=GrappleValue(GrapplePlayer(0),"x")-GrappleValue(GrapplePlayer(1),"x");
        float dz=GrappleValue(GrapplePlayer(0),"z")-GrappleValue(GrapplePlayer(1),"z");
        return MathF.Sqrt(dx*dx+dz*dz);
    }
    private PlayerInput BodyAim(int from,int to)
    {
        float dx=GrappleValue(GrapplePlayer(to),"x")-GrappleValue(GrapplePlayer(from),"x");
        float dz=GrappleValue(GrapplePlayer(to),"z")-GrappleValue(GrapplePlayer(from),"z");
        float length=MathF.Sqrt(dx*dx+dz*dz);
        return new PlayerInput(dx/length,dz/length);
    }
    private void CheckBodyModels(string reason)
    {
        var a=TraversalModel(0).Position;var b=TraversalModel(1).Position;
        float distance=new Vector2(a.X-b.X,a.Z-b.Z).Length();
        Event("body-render-clearance",new {reason,distance,authority=BodyDistance()});
        Check(distance>=.7f,reason+" keeps visible character meshes separated");
    }
    private async Task BodyContacts()
    {
        Event("body-input-policy",new {stage="sunken-temple",source="ordinary three-human practice inputs",fixtures=false,stateMutation=false});
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        foreach(int seat in new[]{0,1,2})Input(seat,default);
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),3,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"body contact practice reaches fight",150);
        await GrappleWalk(2,17,12);
        await GrappleWalk(0,10,13);await GrappleWalk(1,14,13);

        for(int sample=0;sample<16;sample++)
        {
            Input(0,BodyAim(0,1));Input(1,BodyAim(1,0));await Frames(2);
            Check(GrappleValue(GrapplePlayer(0),"x")<GrappleValue(GrapplePlayer(1),"x"),"opposed ordinary stick inputs cannot swap player sides");
            Check(BodyDistance()>=.785f,"head-on authority retains circle clearance within wire rounding");
        }
        Check(BodyDistance()<.83f,"opposed players reach actual body contact");
        CheckBodyModels("head-on pressure");await Capture("body-contact");

        float idleStart=GrappleValue(GrapplePlayer(1),"x");
        Input(1,default);
        for(int sample=0;sample<12;sample++){Input(0,BodyAim(0,1));await Frames(2);Check(BodyDistance()>=.785f,"sustained pressure cannot pass through idle player");}
        float yielded=GrappleValue(GrapplePlayer(1),"x")-idleStart;
        Event("body-yield",new {yielded,seconds=.8f});
        Check(yielded>.35f&&yielded<1.15f,"idle opponent yields slowly under sustained pressure");
        CheckBodyModels("stationary yielding");await Capture("body-push");

        float slideStart=GrappleValue(GrapplePlayer(1),"z");
        Input(0,new PlayerInput(1,0));Input(1,new PlayerInput(0,-1));
        for(int sample=0;sample<9;sample++){await Frames(2);Check(BodyDistance()>=.785f,"lateral escape retains body clearance while attacker presses");}
        Check(GrappleValue(GrapplePlayer(1),"z")<slideStart-1.6f,"pressed opponent can slide sideways using their own movement");
        Input(0,default);Input(1,default);await Frames(8);
        CheckBodyModels("settled lateral escape");await Capture("body-slide");
        Check(GrapplePlayer(0).GetProperty("alive").GetBoolean()&&GrapplePlayer(1).GetProperty("alive").GetBoolean(),"body contact preserves independent living players");
        _root.Back();await Frames(3);
    }
}

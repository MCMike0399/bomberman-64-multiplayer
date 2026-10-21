using Godot;
using Bomber.Client;
using Bomber.Game;
using Bomber.Game.Simulation;
using System.Text.Json;
namespace Bomber.Testing;

public partial class PlaytestDriver
{
    private JsonElement KickBomb=>State.GetProperty("bombs")[0];
    private async Task StartKickPractice()
    {
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        foreach(int seat in new[]{0,1,2})Input(seat,default);
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),3,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"kick physics reaches three-human stock temple training",150);
    }
    private async Task KickPhysics()
    {
        Event("kick-physics-policy",new {stage="sunken-temple",source="ordinary three-human practice movement and actions",fixtures=false,stateMutation=false});
        await StartKickPractice();
        await GrappleWalk(0,8,13);
        Check(GrapplePlayer(0).GetProperty("remote").GetBoolean(),"kick witness collects actual remote training pickup");
        await GrappleWalk(0,7,13);await GrappleWalk(1,12,13);await GrappleWalk(2,15,12);
        Camera3D? inspection=null;var arenaCamera=_root.GetViewport().GetCamera3D()!;
        if(_scenario=="kick-physics")
        {
            inspection=new Camera3D {Fov=38,Near=.05f,Far=100};_root.AddChild(inspection);
            var target=(TraversalModel(0).GlobalPosition+TraversalModel(1).GlobalPosition)*.5f+Vector3.Up*.65f;
            inspection.Position=target+new Vector3(0,5,10);inspection.LookAt(target);inspection.MakeCurrent();
            await Frames(8);Event("inspection-camera",new {purpose="actual arena kick and impact motion; unchanged simulation",frame=Engine.GetProcessFrames()});
        }
        float victimStartX=GrappleValue(GrapplePlayer(1),"x"),victimGround=GrappleValue(GrapplePlayer(1),"y");
        Input(0,new PlayerInput(1,0));await Frames(1);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
        Check(KickBomb.GetProperty("state").GetInt32()==0,"precontact bomb is planted near the feet in the real corridor");
        float startX=GrappleValue(KickBomb,"x"),ground=GrappleValue(KickBomb,"y");
        await Capture("kick-precontact");
        int kickCueBefore=Cue("kick");
        Input(0,new PlayerInput(0,0,A:true));
        await WaitState(_=>GrapplePlayer(0).GetProperty("kickWindup").GetSingle()>0,"a fresh primary press plants the kicker in an authoritative wind-up",20);
        Check(KickBomb.GetProperty("state").GetInt32()==0&&Cue("kick")==kickCueBefore,"anticipation holds the bomb and defers the impact sound until launch");
        Check(TraversalModel(0).KickPose<0,"actual character leg anticipates the accepted kick");
        await Capture("kick-planted-windup",0);
        await WaitState(_=>State.GetProperty("bombs").GetArrayLength()>0&&KickBomb.GetProperty("state").GetInt32()==1,"deliberate primary kick launches the planted bomb",20);
        Input(0,default);await Frames(2);
        Check(GrappleValue(KickBomb,"y")>ground+.04f,"kicked bomb rises through authoritative short hop");
        Check(GrappleValue(KickBomb,"x")>startX+.45f,"kick carries bomb decisively ahead without continued pushing");
        Check(Cue("kick")>kickCueBefore&&TraversalModel(0).KickPose>0,"strike releases bomb with visible follow-through and impact sound");
        await Capture("kick-launch-hop",0);
        await WaitState(_=>GrapplePlayer(1).GetProperty("airborne").GetBoolean(),"shot contact launches victim into real authority flight",30);
        float impactX=GrappleValue(GrapplePlayer(1),"x"),impactY=GrappleValue(GrapplePlayer(1),"y");
        await Frames(4);
        Check(GrappleValue(GrapplePlayer(1),"x")>impactX+.1f&&GrappleValue(GrapplePlayer(1),"y")>impactY+.05f,"victim makes a small upward and backward stumble from bomb impact");
        Check(GrapplePlayer(1).GetProperty("knockdownReason").GetString()=="rolling-bomb","victim reaction identifies physical bomb contact");
        Check(!GrapplePlayer(0).GetProperty("stunned").GetBoolean(),"outgoing shot never stuns its following kicker");
        await Capture("kick-victim-flight");
        float peak=GrappleValue(GrapplePlayer(1),"y");int flightFrames=0;
        while(GrapplePlayer(1).GetProperty("airborne").GetBoolean()&&flightFrames<30)
        {await Frames(1);flightFrames++;peak=Math.Max(peak,GrappleValue(GrapplePlayer(1),"y"));}
        float knockback=GrappleValue(GrapplePlayer(1),"x")-victimStartX;
        Check(!GrapplePlayer(1).GetProperty("airborne").GetBoolean(),"kicked victim completes short gravity stumble");
        Check(peak-victimGround>.1f&&peak-victimGround<=.4f&&knockback>.5f&&knockback<=1.3f,"actual temple impact stays low and within a short body-width displacement");
        Event("kick-impact-measurements",new {height=peak-victimGround,knockback,flightFrames,frame=Engine.GetProcessFrames()});
        Check(GrapplePlayer(1).GetProperty("stunned").GetBoolean(),"impact landing has a readable finite recovery state");
        await Capture("kick-victim-landing");
        await Frames(90);
        Check(State.GetProperty("bombs").GetArrayLength()==1&&KickBomb.GetProperty("remote").GetBoolean(),"active-owner remote remains deliberately armed beyond ordinary fuse");
        await Capture("kick-remote-waits-command");
        Input(0,new PlayerInput(0,0,R:true));await Frames(2);Input(0,default);
        Check(State.GetProperty("bombs").GetArrayLength()==0&&State.GetProperty("hazards").GetArrayLength()>0,"remote command detonates the kicked waiting bomb");
        await Capture("kick-remote-command");
        if(inspection!=null){arenaCamera.MakeCurrent();inspection.QueueFree();await Frames(3);}

        await StartKickPractice();await GrappleWalk(0,3,12);
        Check(!GrapplePlayer(0).GetProperty("remote").GetBoolean(),"corner witness uses a normal bomb without remote pickup");
        // The outside corner of the raised rear terrace is solid at (5,11),
        // approached from the guaranteed-clear tier-one row twelve.
        Input(0,new PlayerInput(.7071f,-.7071f));await Frames(1);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
        string cornerId=KickBomb.GetProperty("id").GetString()!;
        Input(0,new PlayerInput(0,0,A:true));
        await WaitState(_=>State.GetProperty("bombs").GetArrayLength()>0&&KickBomb.GetProperty("state").GetInt32()==1,"normal diagonal kick reaches the actual temple corner",20);
        Input(0,default);
        await WaitState(_=>State.GetProperty("bombs").GetArrayLength()>0&&KickBomb.GetProperty("state").GetInt32()==0,"normal bomb stops against solid raised corner",20);
        float x=GrappleValue(KickBomb,"x"),z=GrappleValue(KickBomb,"z"),fuse=GrappleValue(KickBomb,"fuse");
        Check(x>4&&x<5&&z>11&&z<12,"corner stop remains outside the upper terrace footprint");
        Check(fuse>0&&fuse<BlastBomb.FuseSeconds,"corner collision retains a running normal fuse");
        await Capture("kick-normal-corner-fuse");
        // Walk away using normal controls while the actual bomb keeps ticking.
        Input(0,new PlayerInput(-.6f,.8f));await Frames(18);Input(0,default);
        await WaitState(_=>!State.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("id").GetString()==cornerId),"corner-stopped normal bomb autoexplodes without a remote command",100);
        Check(State.GetProperty("hazards").GetArrayLength()>0,"corner fuse ends in a real rendered explosion");
        Check(GrappleValue(GrapplePlayer(0),"x")<3,"corner witness retreats beyond the collected fire-up blast instead of silently respawning");
        await Capture("kick-normal-corner-exploded");
        await StartKickPractice();
        var layout=BlastStages.Get("sunken-temple");var cover=Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!);
        bool ClearLane(int column)=>Enumerable.Range(15,7).All(row=>(cover[(row*layout.Width+column)/8]&(1<<((row*layout.Width+column)%8)))==0);
        int lane=Enumerable.Range(9,3).First(ClearLane);
        await GrappleWalk(0,lane,13);Input(0,new PlayerInput(0,1));await Frames(1);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
        Check(State.GetProperty("bombs").GetArrayLength()==1,"terrace witness places a real bomb on the bridge before the courtyard drop");
        float terraceStart=GrappleValue(KickBomb,"z");Input(0,new PlayerInput(0,0,A:true));
        await WaitState(_=>KickBomb.GetProperty("state").GetInt32()==1,"ordinary bridge contact releases committed kick",20);Input(0,default);
        await WaitState(_=>KickBomb.GetProperty("state").GetInt32()==3,"kicked bomb leaves real bridge edge through gravity",20);
        Check(GrappleValue(KickBomb,"y")<1&&GrappleValue(KickBomb,"y")>0,"courtyard shot falls between actual terrain levels");
        await Capture("kick-courtyard-drop",0);
        await WaitState(_=>KickBomb.GetProperty("state").GetInt32()==1,"courtyard landing resumes the same shot",30);
        Check(GrappleValue(KickBomb,"z")>terraceStart+5&&Math.Abs(GrappleValue(KickBomb,"y"))<.01f,"shot crosses more than five cells and retains motion on lower courtyard");
        await Capture("kick-courtyard-continues",0);
        foreach(int seat in new[]{0,1,2})Input(seat,default);
        _root.Back();await Frames(3);
    }
}

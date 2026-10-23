using Godot;
using Bomber.Client;
using Bomber.Game;
using Bomber.Game.Simulation;
using System.Text.Json;
namespace Bomber.Testing;

public partial class PlaytestDriver
{
    private JsonElement GrapplePlayer(int seat)=>State.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("slot").GetInt32()==seat);
    private static float GrappleValue(JsonElement player,string key)=>player.GetProperty(key).GetSingle();
    private int GrappleEffect(string kind)=>Presentation.GetProperty("effects").TryGetProperty(kind,out var value)?value.GetInt32():0;
    private async Task GrappleActions()
    {
        Event("grapple-input-policy",new {source="ordinary authoritative three-human training sessions; real movement/action inputs",fixtures=false,stateMutation=false});
        await PrepareGrapple(detonateAtImpact:true);
        await PrepareGrapple();
        int recoverBefore=GrappleEffect("recover");
        var passive=GrappleValue(GrapplePlayer(1),"recoveryRemaining");
        await Capture("grapple-knocked-down");
        int recoveryFrames=0;
        while(GrapplePlayer(1).GetProperty("stunned").GetBoolean()&&recoveryFrames<35)
        {
            Input(1,new PlayerInput(recoveryFrames%4<2?1:-1,0,A:recoveryFrames%4<2));await Frames(2);recoveryFrames+=2;
        }
        Input(1,default);await Frames(2);
        Check(!GrapplePlayer(1).GetProperty("stunned").GetBoolean()&&recoveryFrames/30f<passive,"victim-owned alternating stick/button input accelerates finite recovery");
        Check(State.GetProperty("bombs").GetArrayLength()==1,"recovery mashing produces no buffered bomb");
        Check(GrappleEffect("recover")>recoverBefore,"recover presentation reaches actual arena renderer");await Capture("grapple-recovered");

        await PrepareGrapple();await GrabGrappleVictim();
        var start=GrappleValue(GrapplePlayer(2),"x");
        Input(2,new PlayerInput(1,0,B:true));await Frames(6);
        var victim=GrapplePlayer(1);var carrier=GrapplePlayer(2);
        Check(GrappleValue(carrier,"x")>start+.2f,"carrier really walks while holding player");
        Check(Math.Abs(GrappleValue(victim,"x")-GrappleValue(carrier,"x"))<.1f&&Math.Abs(GrappleValue(victim,"z")-GrappleValue(carrier,"z"))<.1f,"held player follows carrier authority position");
        Check(GrappleValue(victim,"y")>GrappleValue(carrier,"y")+1,"held player stays visibly above carrier");
        Input(2,new PlayerInput(0,0,B:true));await Frames(2);await Capture("grapple-held-follow");
        int releaseBefore=GrappleEffect("player-release");
        for(int i=0;i<44&&GrapplePlayer(1).GetProperty("carriedBy").ValueKind!=JsonValueKind.Null;i+=2)
        {Input(1,new PlayerInput(i%4<2?1:-1,0,A:i%4<2));await Frames(2);}
        Input(1,default);Input(2,default);await Frames(2);
        Check(GrapplePlayer(1).GetProperty("carriedBy").ValueKind==JsonValueKind.Null,"held victim escapes through their own inputs");
        Check(GrapplePlayer(2).GetProperty("carrying").ValueKind==JsonValueKind.Null,"escape clears both ends of hold");
        Check(GrappleEffect("player-release")>releaseBefore,"escape cue reaches live arena presentation");await Capture("grapple-escaped");

        await PrepareGrapple();await GrabGrappleVictim();
        float x=GrappleValue(GrapplePlayer(1),"x"),z=GrappleValue(GrapplePlayer(1),"z");
        int throwBefore=GrappleEffect("player-throw"),throwCueBefore=Cue("throw");
        // Face along the guaranteed-clear training corridor, then release the actual lift button.
        Input(2,new PlayerInput(-1,0,B:true));await Frames(2);Input(2,new PlayerInput(-1,0));await Frames(2);
        victim=GrapplePlayer(1);
        Check(victim.GetProperty("airborne").GetBoolean(),"releasing lift throws victim through authority flight state");
        Check(Math.Abs(GrappleValue(victim,"x")-x)+Math.Abs(GrappleValue(victim,"z")-z)<1,"player throw begins with continuous short displacement");
        Check(GrappleValue(victim,"y")>1.4f,"throw rises above held height rather than teleporting to floor");
        Check(GrapplePlayer(2).GetProperty("carrying").ValueKind==JsonValueKind.Null,"throw clears carrier hold immediately");
        Check(GrappleEffect("player-throw")>throwBefore&&Cue("throw")>throwCueBefore,"player throw produces actual renderer event and audio cue");
        Input(2,default);await Capture("grapple-player-flight");
        await WaitState(_=>!GrapplePlayer(1).GetProperty("airborne").GetBoolean(),"thrown player returns to an authority surface",85);
        Check(GrappleValue(GrapplePlayer(1),"y")<=1.01f,"player flight lands on actual temple walkway");await Capture("grapple-player-landed");
        _root.Back();await Frames(3);
    }
    private int _grappleTrial;
    private async Task PrepareGrapple(bool detonateAtImpact=false)
    {
        if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        foreach(int seat in new[]{0,1,2})Input(seat,default);
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),3,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"three-human grapple training reaches fight",150);
        await GrappleWalk(0,8,13);Check(GrapplePlayer(0).GetProperty("remote").GetBoolean(),"kicker collects the real training remote pickup");
        await GrappleWalk(0,7,13);await GrappleWalk(1,11,13);await GrappleWalk(2,13,12);
        int knockBefore=GrappleEffect("knockdown"),kickCueBefore=Cue("kick");
        // Face along the victim's real corridor before near-foot placement; a second primary press deliberately strikes.
        Input(0,new PlayerInput(1,0));await Frames(1);Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(7);Input(0,new PlayerInput(0,0,A:true));
        await WaitState(s=>s.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("state").GetInt32()==1),"contact input starts real bomb rolling",25);
        Input(0,default);
        await WaitState(_=>GrapplePlayer(1).GetProperty("stunned").GetBoolean(),"real kicked bomb knocks down opponent",65);
        await Frames(1);
        Check(GrapplePlayer(1).GetProperty("knockdownReason").GetString()=="rolling-bomb","snapshot identifies physical rolling-bomb cause");
        Check(!GrapplePlayer(0).GetProperty("stunned").GetBoolean(),"kicker avoids outgoing bomb self-hit");
        Check(GrappleEffect("knockdown")>knockBefore&&Cue("kick")>kickCueBefore,"knockdown event reaches actual renderer and cue path");
        Check(GrapplePlayer(1).GetProperty("airborne").GetBoolean(),"bomb impact launches victim through real authority flight");
        if(detonateAtImpact)
        {
            Check(!GrapplePlayer(1).GetProperty("heart").GetBoolean(),"remote-snipe victim has no heart to absorb the explosion");
            string victimId=GrapplePlayer(1).GetProperty("id").GetString()!;
            Input(0,new PlayerInput(0,0,R:true));
            await WaitState(s=>s.GetProperty("events").EnumerateArray().Any(e=>
                e.GetProperty("kind").GetString()=="death"&&e.GetProperty("playerId").GetString()==victimId&&
                e.GetProperty("reason").GetString()=="explosion"),"remote detonation eliminates the freshly knocked down victim",12);
            Check(State.GetProperty("bombs").GetArrayLength()==0,"the actual sniping remote explodes on command during knockdown");
            await Capture("grapple-remote-snipe-explosion");Input(0,default);return;
        }
        await Capture($"grapple-impact-flight-{++_grappleTrial}");
        await WaitState(_=>!GrapplePlayer(1).GetProperty("airborne").GetBoolean(),"bomb-impact victim lands before grounded recovery or grab",65);
        Check(GrapplePlayer(1).GetProperty("stunned").GetBoolean()&&!GrapplePlayer(1).GetProperty("ghost").GetBoolean(),"bomb-impact landing preserves a finite grounded recovery window");
    }
    private async Task GrabGrappleVictim()
    {
        int grabBefore=GrappleEffect("player-grab"),liftCueBefore=Cue("lift");
        // Stop in grab range outside body clearance; input never walks through the victim.
        bool inRange=false;
        for(int frame=0;frame<16;frame++)
        {
            var victim=GrapplePlayer(1);var carrier=GrapplePlayer(2);
            float dx=GrappleValue(victim,"x")-GrappleValue(carrier,"x"),dz=GrappleValue(victim,"z")-GrappleValue(carrier,"z");
            if(dx*dx+dz*dz<=1.02f*1.02f){inRange=true;break;}
            Input(2,new PlayerInput(Math.Clamp(dx*4,-1,1),Math.Clamp(dz*4,-1,1)));await Frames(1);
        }
        Check(inRange,"carrier reaches landed victim through ordinary movement within body clearance");
        Input(2,new PlayerInput(0,0,B:true));await Frames(2);
        Check(GrapplePlayer(2).GetProperty("carrying").GetString()=="player","real lift button picks up knocked-down victim");
        Check(GrapplePlayer(1).GetProperty("carriedBy").GetString()==GrapplePlayer(2).GetProperty("id").GetString(),"hold identifies authoritative carrier");
        Check(GrappleEffect("player-grab")>grabBefore&&Cue("lift")>liftCueBefore,"player grab produces actual renderer event and lift audio cue");
    }
    private async Task GrappleWalk(int seat,int targetX,int targetZ)
    {
        var layout=BlastStages.Get("sunken-temple");var soft=Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!);
        int start=(int)GrappleValue(GrapplePlayer(seat),"z")*layout.Width+(int)GrappleValue(GrapplePlayer(seat),"x"),goal=targetZ*layout.Width+targetX;
        var queue=new Queue<int>();queue.Enqueue(start);var previous=new Dictionary<int,int>{{start,-1}};
        while(queue.Count>0&&!previous.ContainsKey(goal))
        {
            int cell=queue.Dequeue(),x=cell%layout.Width,z=cell/layout.Width;
            foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
            {
                int nx=x+dx,nz=z+dz,next=nz*layout.Width+nx;
                if(nx<0||nz<0||nx>=layout.Width||nz>=layout.Height||previous.ContainsKey(next)||!layout.CanCross(x,z,nx,nz)||!layout.At(nx,nz).IsPassable)continue;
                if(next/8<soft.Length&&(soft[next/8]&(1<<(next%8)))!=0)continue;
                previous[next]=cell;queue.Enqueue(next);
            }
        }
        Check(previous.ContainsKey(goal),$"ordinary walkable route exists for seat {seat} to {targetX},{targetZ}");
        var path=new List<int>();for(int cell=goal;cell!=start;cell=previous[cell])path.Add(cell);path.Reverse();
        foreach(int cell in path)
        {
            float x=cell%layout.Width+.5f,z=cell/layout.Width+.5f;bool arrived=false;
            for(int frame=0;frame<40;frame++)
            {
                var player=GrapplePlayer(seat);float dx=x-GrappleValue(player,"x"),dz=z-GrappleValue(player,"z");
                if(Math.Abs(dx)<.12f&&Math.Abs(dz)<.12f){arrived=true;break;}
                Input(seat,new PlayerInput(Math.Clamp(dx*5,-1,1),Math.Clamp(dz*5,-1,1)));await Frames(1);
            }
            Check(arrived,$"seat {seat} walks real authority path cell {cell%layout.Width},{cell/layout.Width}");
        }
        Input(seat,default);await Frames(2);
    }
}

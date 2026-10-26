using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Rules;
using System.Text.Json;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task BlastHitbox()
    {
        Event("blast-hitbox-policy",new{stage="sunken-temple",ordinaryInput=true,stateMutation=false});
        foreach(int pumps in new[]{3,0})
        {
            if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
            for(int seat=0;seat<3;seat++)Input(seat,default);
            _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0){Advanced=new AdvancedRules{
                DisabledItems="fire,bomb,red,bounce,heart,focus,tiny,big,haste,slow"}},3,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"overhead blast training reaches fight",150);
            await GrappleWalk(0,10,13);
            Check(GrapplePlayer(0).GetProperty("remote").GetBoolean(),"thrower collects the stock remote pickup");
            await GrappleWalk(0,12,6);await GrappleWalk(1,12,12);
            Input(0,new PlayerInput(0,1));await Frames(1);
            Input(0,new PlayerInput(0,0,A:true,B:true));await Frames(2);
            Input(0,new PlayerInput(0,1,B:true,Aim:true));
            await WaitState(_=>GrappleValue(GrapplePlayer(0),"aimRange")>=6,"thrower aims down the temple toward the victim",30);
            Input(0,new PlayerInput(0,0,B:true,Aim:true));await Frames(2);
            for(int pump=0;pump<pumps;pump++)
            {
                Input(0,new PlayerInput(0,0,A:true,B:true,Aim:true));await Frames(2);
                Input(0,new PlayerInput(0,0,B:true,Aim:true));await Frames(15);
            }
            Check(HandlingBomb.GetProperty("pumps").GetInt32()==pumps,"real pump presses set the requested bomb size");
            await Capture($"blast-hitbox-{pumps}-held");
            Input(0,default);
            bool overlap=false;
            for(int frame=0;frame<110;frame++)
            {
                await Frames(1);
                var bomb=HandlingBomb;var victim=GrapplePlayer(1);
                float x=AimNumber(bomb,"x"),y=AimNumber(bomb,"y"),z=AimNumber(bomb,"z");
                float px=AimNumber(victim,"x"),py=AimNumber(victim,"y"),pz=AimNumber(victim,"z");
                float radius=bomb.GetProperty("effectiveLevel").GetInt32()*.9f+.4f,dx=x-px,dz=z-pz;
                float bodyY=Math.Clamp(y,py,py+BlastGeometry.PlayerHeight);
                if(dx*dx+dz*dz+(y-py)*(y-py)<=radius*radius ||
                    dx*dx+dz*dz+(y-bodyY)*(y-bodyY)>radius*radius)continue;
                Event("overhead-body-overlap",new{pumps,bomb,victim});
                await Capture($"blast-hitbox-{pumps}-overhead",0);
                string id=victim.GetProperty("id").GetString()!;
                Input(0,new PlayerInput(0,0,R:true));
                await WaitState(s=>s.GetProperty("events").EnumerateArray().Any(e=>
                    e.GetProperty("kind").GetString()=="death"&&e.GetProperty("playerId").GetString()==id&&
                    e.GetProperty("reason").GetString()=="explosion"),"overhead explosion kills the body-overlapped victim",12);
                Check(State.GetProperty("bombs").GetArrayLength()==0,"remote command detonates the actual thrown bomb");
                var blast=State.GetProperty("events").EnumerateArray().Single(e=>e.GetProperty("kind").GetString()=="blast");
                dx=AimNumber(blast,"x")-px;dz=AimNumber(blast,"z")-pz;y=AimNumber(blast,"y");
                bodyY=Math.Clamp(y,py,py+BlastGeometry.PlayerHeight);
                Check(dx*dx+dz*dz+(y-py)*(y-py)>radius*radius&&
                    dx*dx+dz*dz+(y-bodyY)*(y-bodyY)<=radius*radius,
                    "actual detonation reaches the body while the feet remain outside its sphere");
                await Capture($"blast-hitbox-{pumps}-explosion");
                Input(0,default);overlap=true;break;
            }
            Check(overlap,"ordinary thrown bomb reaches the victim's body while its feet remain outside the blast sphere");
        }
        _root.Back();await Frames(3);
    }
}

using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Stages;

namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task UpAndDownTour()
    {
        Input(0,default);Input(1,default);Input(2,default);
        _root.StartLocal(new SessionSettings(Stage:BlastStages.UpAndDown,Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"Up & Down practice starts",150);
        var terrain=_root.GetChildren().OfType<ArenaView>().Single().FindChildren("*","",true,false).OfType<TempleStageView>().Single();
        foreach(var (x,z,tier) in new[]{(5.5f,10.5f,2),(11.5f,2.5f,4),(10.5f,16.5f,0)})
        {
            float top=tier*TempleStageView.HeightScale;
            var faces=AuditDescendants(terrain).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null).Count(m=>
            {
                var bounds=(terrain.GlobalTransform.AffineInverse()*m.GlobalTransform)*m.Mesh.GetAabb();
                return Math.Abs(bounds.End.Y-top)<.0001f&&x>bounds.Position.X&&x<bounds.End.X&&z>bounds.Position.Z&&z<bounds.End.Z;
            });
            Check(faces==1,$"Up & Down {x},{z} has one visible paving top instead of coplanar slab faces");
        }
        await Capture("up-and-down-battle-camera");
        if(!_headless)
        {
            var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
            camera.Position=new(37,31,43);camera.LookAt(new(12,1,13));
            await Capture("up-and-down-oblique-inspection");
            camera.Position=new(29,12,34);camera.LookAt(new(12,2,16));
            await Capture("up-and-down-underpasses-inspection");
            camera.Transform=pose;
        }
        async Task Go(int seat,int x,int z,float y)
        {
            bool arrived=false;
            for(int frame=0;frame<400;frame++)
            {
                var player=GrapplePlayer(seat);
                float dx=x+.5f-GrappleValue(player,"x"),dz=z+.5f-GrappleValue(player,"z");
                if(Math.Abs(dx)<.12f&&Math.Abs(dz)<.12f){arrived=true;break;}
                Input(seat,new PlayerInput(Math.Clamp(dx*5,-1,1),Math.Clamp(dz*5,-1,1)));await Frames(1);
            }
            Input(seat,default);await Frames(3);
            Check(arrived,$"P{seat+1} walks to {x},{z} through ordinary movement");
            Check(Math.Abs(GrappleValue(GrapplePlayer(seat),"y")-y)<.04f,$"Up & Down P{seat+1} reaches {x},{z} at elevation {y}");
            Check(!GrapplePlayer(seat).GetProperty("airborne").GetBoolean(),"connected stage route keeps ordinary foot support");
        }
        foreach(int side in new[]{-1,1})
        {
            int rail=side<0?2:21;
            if(side>0)await Go(0,rail,2,4);
            await Go(0,rail,12,4);
            Input(0,new PlayerInput(-side,0));
            await WaitState(_=>GrapplePlayer(0).GetProperty("falling").GetBoolean(),"inward side-rail movement starts an ordinary drop",30);
            await Capture($"up-and-down-side-drop-{side}-falling",0);
            await WaitState(_=>!GrapplePlayer(0).GetProperty("airborne").GetBoolean(),"side drop reaches the middle landing edge",45);
            Input(0,default);
            Check(GrapplePlayer(0).GetProperty("alive").GetBoolean()&&Math.Abs(GrappleValue(GrapplePlayer(0),"y")-2)<.01f,
                "inward side drop lands alive on the middle pier");
            await Capture($"up-and-down-side-drop-{side}-landed");
            await Go(0,side<0?5:18,12,2);await Go(0,side<0?5:18,7,2);
            await Go(0,11,7,4);await Go(0,11,2,4);
        }
        await Go(0,2,2,4);
        await Go(0,11,2,4);await Go(0,11,7,4);await Capture("up-and-down-green-crown");
        await Go(0,5,7,2);await Go(0,5,14,2);await Go(0,11,14,0);
        await Capture("up-and-down-low-deck");
        await Go(0,18,14,2);await Go(0,18,22,2);
        await Capture("up-and-down-pier-under-ring");
        await Go(0,18,21,2);await Go(0,11,21,0);await Go(0,11,23,0);
        await Go(0,11,14,0);await Go(0,5,14,2);await Go(0,5,21,2);
        await Go(0,11,21,0);await Go(0,11,14,0);await Go(0,18,14,2);
        await Go(0,18,21,2);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
        Check(Math.Abs(State.GetProperty("bombs")[0].GetProperty("y").GetSingle()-2)<.01f,"bomb planted under the ring stays on the pier");
        await Go(0,18,23,2);
        Input(0,new PlayerInput(0,1));
        await WaitState(_=>GrapplePlayer(0).GetProperty("falling").GetBoolean(),"exposed pier edge starts a real fall",30);
        Input(0,default);await Capture("up-and-down-falling-to-water",0);
        await WaitState(s=>s.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="death"&&e.GetProperty("reason").GetString()=="water"),"leaving the pier eliminates at the real water plane",100);
        _root.Back();await Frames(3);

        _root.StartLocal(new SessionSettings(Stage:BlastStages.UpAndDown,Bots:0,Wins:3,RoundSeconds:90,Ghosts:false),3);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"normal three-human Up & Down battle starts",150);
        Check(Convert.FromBase64String(State.GetProperty("softBlocks").GetString()!).Sum(b=>System.Numerics.BitOperations.PopCount((uint)b))==36,"normal stage has thirty-six lower/middle cover blocks");
        await Capture("up-and-down-normal-cover");
        Check(AuditDescendants(_root).OfType<Node3D>().Count(n=>n.Name.ToString().StartsWith("UpAndDownBlock",StringComparison.Ordinal))==36,
            "all normal Up & Down destructible blocks use their own gray stone model");
        var block=AuditDescendants(_root).OfType<Node3D>().First(n=>n.Name.ToString().StartsWith("UpAndDownBlock",StringComparison.Ordinal));
        float blockTop=BombThrowRules.CoverHeight*TempleStageView.HeightScale;
        Check(block.GetChildren().OfType<MeshInstance3D>().Count(m=>Math.Abs((m.Transform*m.Mesh.GetAabb()).End.Y-blockTop)<.0001f)==1,
            "stone block rim owns its top without a coplanar body face");
        if(!_headless)
        {
            var camera=_root.GetViewport().GetCamera3D()!;var pose=camera.Transform;
            // Look from inside the ring; an exterior near-side camera lets the
            // high rail occlude precisely the lower block we need to inspect.
            camera.Position=new(7.5f,6.5f,20.5f);camera.LookAt(new Vector3(4.5f,3.1f,18.5f));
            await Capture("up-and-down-stone-blocks-inspection");camera.Transform=pose;
        }
        // Clear center lanes and flights stay available in ordinary cover-filled play.
        await Go(0,11,2,4);await Go(0,11,7,4);await Go(0,5,7,2);await Go(0,5,14,2);
        await Go(0,5,18,2);
        Input(0,new PlayerInput(0,0,A:true));await Frames(2);Input(0,default);await Frames(2);
        await Go(0,5,15,2);
        await WaitState(s=>(Convert.FromBase64String(s.GetProperty("softBlocks").GetString()!)[(18*24+4)/8]&(1<<((18*24+4)%8)))==0,
            "ordinary bomb destroys the stage-specific pier stone block",120);
        await Capture("up-and-down-stone-block-destroyed");
        await Go(0,5,14,2);await Go(0,11,14,0);
        await Go(2,11,2,4);await Go(2,11,7,4);await Go(2,18,7,2);await Go(2,18,17,2);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==2,"Up & Down reaches real timed sudden death",1300);
        Check(State.GetProperty("hurryMode").GetString()=="water"&&State.GetProperty("closedLeft").GetInt32()==0,"Up & Down floods instead of closing columns");
        await WaitState(_=>!GrapplePlayer(0).GetProperty("alive").GetBoolean(),"rising water actually eliminates the lowest-deck player",500);
        Check(GrapplePlayer(2).GetProperty("alive").GetBoolean()&&GrapplePlayer(1).GetProperty("alive").GetBoolean(),"middle and upper players outlive the lower deck");
        await Capture("up-and-down-lower-deck-flooded");
        await WaitState(_=>!GrapplePlayer(2).GetProperty("alive").GetBoolean(),"rising water reaches and eliminates the middle-pier player",1000);
        Check(GrapplePlayer(1).GetProperty("alive").GetBoolean(),"upper ring survives the middle-pier flood");
        await Capture("up-and-down-middle-piers-flooded");
        _root.Back();await Frames(3);
    }
}

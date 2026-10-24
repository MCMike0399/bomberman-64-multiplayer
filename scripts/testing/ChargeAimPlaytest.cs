using Godot;
using Bomber.Game;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Effects;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task ChargeAim()
    {
        Event("charge-aim-policy",new{stage="sunken-temple",ordinaryInput=true,stateMutation=false,preview="controlled carrier advisory arc, static cover truncation"});
        float previousRange=0;
        foreach(int pumps in new[]{0,3})
        {
            await HandlingPractice();
            // The stock terrace's cleared east-west lane keeps this comparison away from cover.
            Input(0,new PlayerInput(1,0));await Frames(3);Input(0,default);await Frames(3);
            Input(0,new PlayerInput(0,0,A:true));await Frames(14);Input(0,default);await Frames(4);
            Check(Player.GetProperty("carrying").GetString()=="bomb","standing A hold lifts normally before aim preview");
            for(int charge=0;charge<pumps;charge++)
            {Input(0,new PlayerInput(0,0,A:true));await Frames(3);Input(0,default);await Frames(13);}
            await Frames(12);
            Check(_root.ThrowPreviews.Count==1,"only the locally controlled carrier gets a throw guide");
            ThrowPreviewState preview=_root.ThrowPreviews.Single();
            Event("charge-preview",preview);
            Check(preview.Pumps==pumps&&preview.End=="Landing"&&preview.Points>8,"renderer exposes a clear landing advisory from shared throw rules");
            var camera=GetViewport().GetCamera3D();
            var target=new Vector3(preview.X,preview.Y*TempleStageView.HeightScale+.1f,preview.Z);
            Check(camera!=null&&!camera.IsPositionBehind(target)&&GetViewport().GetVisibleRect().HasPoint(camera.UnprojectPosition(target)),
                "landing guide sits inside the real gameplay camera frame");
            Check(Visual.GetProperty("largestBombScale").GetSingle()<=2.301f,"charged model stays within the approved 2.3 scale cap");
            float px=Player.GetProperty("x").GetSingle(),pz=Player.GetProperty("z").GetSingle();
            float range=MathF.Sqrt((preview.X-px)*(preview.X-px)+(preview.Z-pz)*(preview.Z-pz));
            Check(range>previousRange+.5f,"charging projects the landing farther along the visible guide");previousRange=range;
            await Capture($"charge-aim-{pumps}-held",0);
            await Frames(36);
            Check(Player.GetProperty("carrying").GetString()=="bomb"&&!Player.GetProperty("stunned").GetBoolean(),"released A keeps the charged bomb held without automatic drop or self hit");
            if(!_headless)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var heldPose=_root.BombRenderPoses.Single();
            Check(heldPose.SocketY is {} socketY&&Math.Abs(heldPose.Y-socketY)<.035f,"release baseline uses a settled bomb at the actual hand socket");
            Input(0,new PlayerInput(0,0,B:true));
            var releasePoses=new List<BombRenderPose>();
            for(int frame=0;frame<16&&releasePoses.Count<3;frame++)
            {
                await Frames(1);
                if(!_headless)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var pose=_root.BombRenderPoses.Single();
                if(pose.CarrierId==null)releasePoses.Add(pose);
            }
            Event("charge-render-release",new{pumps,held=heldPose,frames=releasePoses});
            Check(releasePoses.Count==3,"release records the actual first three rendered flight frames");
            Check(releasePoses.All(pose=>pose.Y>=heldPose.Y-.02f),"bomb rises from the hand socket without dipping on release");
            Input(0,default);
            Check(Player.GetProperty("carrying").ValueKind==System.Text.Json.JsonValueKind.Null,"neutral B deliberately releases the bomb");
            await Capture($"charge-aim-{pumps}-flight",0);
            bool landed=false;
            for(int frame=0;frame<100;frame++)
            {
                Check(!Player.GetProperty("stunned").GetBoolean(),"neutral outgoing throw clears the stationary carrier");
                if(State.GetProperty("bombs").GetArrayLength()>0&&HandlingBomb.GetProperty("state").GetInt32()==0){landed=true;break;}
                await Frames(1);
            }
            Check(landed,"ordinary neutral throw reaches a resting landing");
            float dx=HandlingBomb.GetProperty("x").GetSingle()-preview.X,dz=HandlingBomb.GetProperty("z").GetSingle()-preview.Z;
            Check(MathF.Sqrt(dx*dx+dz*dz)<.12f,"real neutral throw lands at the renderer's shared-rule advisory target");
            Check(_root.ThrowPreviews.Count==0,"throw guide disappears after carrying ends");
            Event("charge-landing",new{pumps,predicted=preview,actual=HandlingBomb});
            await Capture($"charge-aim-{pumps}-landing",0);
        }
        // The shared arc reaches the visible water plane (-1). Approach the
        // shore through ordinary movement so this throw lands in the water row,
        // rather than on dry ground or past the outer map boundary.
        await HandlingPractice();await GrappleWalk(0,8,21);
        Input(0,new PlayerInput(0,1));await Frames(3);Input(0,default);await Frames(3);
        Check(Player.GetProperty("fz").GetSingle()>.9f,"shore witness faces south after its real courtyard walk");
        Input(0,new PlayerInput(0,0,A:true));await Frames(14);Input(0,default);await Frames(16);
        var waterPreview=_root.ThrowPreviews.Single();Event("charge-water-preview",waterPreview);
        Check(waterPreview.End=="Water"&&!waterPreview.LandingMarkerVisible&&waterPreview.EndpointLabel.Contains("sinks"),
            "real stock-temple water endpoint retains water semantics without a safe landing ring");
        await Capture("charge-aim-water-endpoint",0);
        Input(0,new PlayerInput(0,0,B:true));await Frames(3);Input(0,default);
        await WaitState(s=>s.GetProperty("bombs").GetArrayLength()==0,"real throw disappears on the predicted water cell",100);
        Check(!State.GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="blast"),
            "shore throw is removed by water before any timed blast");
        Input(0,default);_root.Back();await Frames(3);
    }
}

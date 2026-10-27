using Godot;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using BlastCircuit.Arena.Effects;
using BlastCircuit.Arena.Models;
using BlastCircuit.Rendering;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    /// <summary>
    /// Knockdown, held-struggle and blast-death presentation: real kicked-bomb knockdowns, real
    /// grabs and a real remote-bomb elimination, plus a fixed-pose model sheet for side-by-side review.
    /// </summary>
    private async Task MishapShowcase()
    {
        Event("mishap-showcase-policy",new {source="ordinary three-human training sessions with real inputs, plus a deterministic pose sheet",stateMutation=false,reference="Bomberman 64 battle footage: burning flail then smoke puff; dizzy sparkles on stunned players"});
        var battleCamera=_root.GetViewport().GetCamera3D()!;
        var inspection=new Camera3D {Fov=34,Near=.05f,Far=100};_root.AddChild(inspection);
        void Look(Vector3 target,Vector3 offset)=>MishapLook(inspection,target,offset);
        try
        {
            // Dazed: the actual kicked-bomb victim flops onto its back with spinning eyes and circling stars.
            await PrepareGrapple();
            var victim=TraversalModel(1);
            await Frames(8);
            float floor=GrappleValue(GrapplePlayer(1),"y")*Arena.Stages.TempleStageView.HeightScale;
            Check(victim.DizzyEyes&&victim.StarsVisible&&!victim.SquintEyes,"grounded knockdown shows dizzy swirl eyes and stars");
            var stars=victim.StarsWorldCenter-victim.HeadWorldCenter;
            Check(new Vector2(stars.X,stars.Z).Length()<.25f&&stars.Y>.3f&&stars.Y<.8f,"dizzy stars circle above the downed head, not the empty standing height");
            float rest=victim.BodyLowestPoint-floor;
            Check(rest>-.12f&&rest<.12f,$"dazed body rests on the walkway (lowest point {rest:0.000} from floor)");
            Check(Mathf.Abs(victim.TumbleSpin)<.001f,"landing finishes the backflip and unwinds whole turns before recovery");
            Event("mishap-dazed",new {rest,stars=stars.ToString(),knockdown=victim.KnockdownPose});
            Look(victim.HeadWorldCenter,new Vector3(0,2.6f,2.4f));inspection.MakeCurrent();
            await Capture("mishap-dazed-closeup");
            battleCamera.MakeCurrent();await Capture("mishap-dazed-battle");

            // Held: the real carrier lifts a fresh victim, who lies across the raised gloves and thrashes.
            await PrepareGrapple();await GrabGrappleVictim();victim=TraversalModel(1);
            await Frames(10);
            var carrier=TraversalModel(2);
            Check(victim.SquintEyes&&!victim.StarsVisible,"held victim squeezes its eyes shut instead of dizzy stars");
            Check(Mathf.Abs(Mathf.AngleDifference(victim.Rotation.Y,carrier.Rotation.Y))<.15f,"held victim turns with the carrier so it lies across the shoulders");
            var offset=victim.GlobalPosition-carrier.GlobalPosition;
            Check(new Vector2(offset.X,offset.Z).Length()<.01f,"held victim rides exactly above the carrier's rendered position");
            float gloves=carrier.GlobalPosition.Y+1.47f;
            Check(victim.BodyLowestPoint>gloves-.25f&&victim.BodyLowestPoint<gloves+.3f,$"held body rests on the carrier's raised gloves ({victim.BodyLowestPoint-gloves:0.000} from glove top)");
            var before=victim.WalkHeadRotation;await Frames(3);
            Check(victim.WalkHeadRotation.DistanceTo(before)>.02f,"held victim keeps thrashing while carried");
            Look(carrier.GlobalPosition+new Vector3(0,1.6f,0),carrier.Basis.Z.Normalized()*4.2f+new Vector3(0,1.4f,0));inspection.MakeCurrent();
            await Capture("mishap-held-closeup");
            battleCamera.MakeCurrent();await Capture("mishap-held-battle");

            // Blast death: the real remote snipe leaves a charred, flailing stand-in that vanishes in smoke.
            await PrepareGrapple(detonateAtImpact:true);
            var burnout=_root.FindChildren("*","",true,false).OfType<BurnoutEffect>().LastOrDefault();
            Check(burnout!=null,"blast elimination spawns the charred burnout gag in the live arena");
            if(burnout!=null)
            {
                async Task At(float age,string label)
                {
                    for(int i=0;i<90&&GodotObject.IsInstanceValid(burnout)&&burnout.Age<age;i++)await Frames(1);
                    if(!GodotObject.IsInstanceValid(burnout))return;
                    Look(burnout.GlobalPosition+new Vector3(0,.8f,0),new Vector3(0,2.2f,3.6f));inspection.MakeCurrent();
                    await Capture(label+"-closeup",0);battleCamera.MakeCurrent();
                }
                Check(burnout.Puppet.Charred||burnout.Age<.09f,"burnout stand-in turns sooty after the white zap");
                await At(.95f,"mishap-burnout-flail");
                Check(GodotObject.IsInstanceValid(burnout)&&burnout.Puppet.Charred,"charred stand-in is still flailing about a second after the blast");
                await At(BomberModel.BurnoutFlailEnd+.02f,"mishap-burnout-stiff");
                await At(BomberModel.BurnoutFlailEnd+.3f,"mishap-burnout-poof");
                for(int i=0;i<90&&GodotObject.IsInstanceValid(burnout);i++)await Frames(1);
                Check(!GodotObject.IsInstanceValid(burnout),"burnout gag removes itself after its smoke puff");
            }
            Check(!TraversalModel(1).Charred,"the respawned training player is the ordinary living model");
            Input(0,default);_root.Back();await Frames(3);

            await MishapSheet(inspection,battleCamera);
        }
        finally
        {
            foreach(int seat in new[]{0,1,2})Input(seat,default);
            battleCamera.MakeCurrent();inspection.QueueFree();
            if(_root.ActiveSession!=null){_root.Back();await Frames(3);}
        }
    }
    private static void MishapLook(Camera3D camera,Vector3 target,Vector3 offset){camera.Position=target+offset;camera.LookAt(target);}
    /// <summary>Fixed poses side by side on the real courtyard light, for reviewing silhouettes rather than timing.</summary>
    private async Task MishapSheet(Camera3D inspection,Camera3D battleCamera)
    {
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"pose sheet reaches the actual temple courtyard",150);
        await GrappleWalk(0,8,18);
        float floor=TraversalModel(0).GlobalPosition.Y;
        void Look(Vector3 target,Vector3 offset)=>MishapLook(inspection,target,offset);
        var art=new ArtMaterials();var sheet=new Node3D {Name="MishapSheet"};_root.AddChild(sheet);
        try
        {
            var suit=ArtMaterials.PlayerColors[3];
            BomberModel Actor(float x,Action<BomberModel> pose)
            {
                var model=new BomberModel(art,suit,"");model.ShowName(false);sheet.AddChild(model);
                model.Position=new(x,floor,18.5f);pose(model);return model;
            }
            var standing=Actor(9.6f,m=>m.Pose(false,false,false));
            var dazed=Actor(11.2f,m=>{m.Pose(false,true,false);m.RecoveryPose(false,false);});
            var tumbling=Actor(13.4f,m=>{m.Pose(false,true,false);m.RecoveryPose(false,true);});
            var carrier=Actor(15.2f,m=>m.Pose(true,false,false,liftingPlayer:true));
            var held=Actor(15.2f,m=>{m.Pose(false,true,false);m.RecoveryPose(true,false);});
            held.Position+=new Vector3(0,1.4f*Arena.Stages.TempleStageView.HeightScale,0);
            var actors=new[]{standing,dazed,tumbling,carrier,held};
            for(int frame=0;frame<24;frame++){foreach(var actor in actors)actor.Animate(1/30f,0);await Frames(1);}
            Check(dazed.DizzyEyes&&tumbling.SquintEyes&&held.SquintEyes&&!standing.DizzyEyes,"pose sheet shows dizzy, squeezed and ordinary eyes on the matching actors");
            Look(new Vector3(12.4f,floor+1.2f,18.5f),new Vector3(0,2.6f,8.8f));inspection.MakeCurrent();
            await Capture("mishap-sheet-poses",4);
            Look(dazed.HeadWorldCenter,new Vector3(0,1.5f,.35f));
            await Capture("mishap-sheet-dizzy-face",2);
            foreach(var actor in actors)actor.QueueFree();
            // The death gag as a strip: zap, airborne flail, grounded flail, stiff, smoke puff.
            float[] ages=[.05f,.3f,1.0f,BomberModel.BurnoutFlailEnd+.02f,BomberModel.BurnoutFlailEnd+.3f];
            var strip=new List<BurnoutEffect>();
            for(int i=0;i<ages.Length;i++)
            {
                var effect=new BurnoutEffect(art,suit,0) {Position=new(9.4f+i*1.8f,floor,18.5f),ProcessMode=ProcessModeEnum.Disabled};
                sheet.AddChild(effect);effect._Process(ages[i]);strip.Add(effect);
            }
            await Frames(2);
            Check(!strip[0].Puppet.Charred&&strip[2].Puppet.Charred,"burnout begins with a white zap and turns sooty for the flail");
            Look(new Vector3(13,floor+.9f,18.5f),new Vector3(0,2.6f,8.2f));
            await Capture("mishap-sheet-burnout",2);
            Event("mishap-sheet",new {floor,ages});
        }
        finally {sheet.QueueFree();battleCamera.MakeCurrent();}
    }
}

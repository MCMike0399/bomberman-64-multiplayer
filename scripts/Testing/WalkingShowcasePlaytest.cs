using Godot;
using Bomber.Client;
using Bomber.Game;
using BlastCircuit.Rendering;
namespace Bomber.Testing;

public partial class PlaytestDriver
{
    private async Task WalkingShowcase()
    {
        Event("walking-showcase-policy",new {stage="sunken-temple",ordinaryInput=true,stateMutation=false,camera="side inspection following the actual match actor",playbackSpeed=1});
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"walking showcase reaches actual temple practice",150);
        await GrappleWalk(0,6,18);
        var model=TraversalModel(0);var battleCamera=_root.GetViewport().GetCamera3D()!;
        var inspection=new Camera3D {Fov=38,Near=.05f,Far=100};_root.AddChild(inspection);
        void Follow()
        {
            var target=model.GlobalPosition+new Vector3(0,.78f,0);
            inspection.Position=target+new Vector3(0,.65f,4.8f);inspection.LookAt(target);
        }
        void Sample(string segment)=>Event("walking-showcase-sample",new {segment,movieFrame=Engine.GetProcessFrames(),authorityX=Player.GetProperty("x").GetSingle(),authorityZ=Player.GetProperty("z").GetSingle(),renderX=model.GlobalPosition.X,renderZ=model.GlobalPosition.Z,pose=model.WalkPose,leftLift=model.LeftFootLift,rightLift=model.RightFootLift,facing=model.Rotation.Y});
        try
        {
            Follow();inspection.MakeCurrent();Input(0,default);
            for(int i=0;i<15;i++){Follow();await Frames(1);}
            Check(Player.GetProperty("alive").GetBoolean()&&!Player.GetProperty("falling").GetBoolean(),"walking witness arrives alive on the stock clear courtyard");
            await Capture("walk-feel-side-ready",0);
            Event("walking-showcase-begin",new {movieFrame=Engine.GetProcessFrames(),start=model.GlobalPosition.ToString()});
            async Task WalkLeg(float direction,float target,string label)
            {
                float startX=Player.GetProperty("x").GetSingle(),startRender=model.GlobalPosition.X,startZ=model.GlobalPosition.Z,previous=startRender,maxLift=0,minPose=1,maxPose=-1;
                var displacements=new List<float>();int frames=0,support=0;
                Input(0,new PlayerInput(direction,0));
                while((target-Player.GetProperty("x").GetSingle())*direction>.08f&&frames<75)
                {
                    Follow();await Frames(1);frames++;
                    float x=model.GlobalPosition.X;
                    if(frames>8)displacements.Add((x-previous)*direction);
                    previous=x;minPose=Mathf.Min(minPose,model.WalkPose);maxPose=Mathf.Max(maxPose,model.WalkPose);
                    maxLift=Mathf.Max(maxLift,Mathf.Max(model.LeftFootLift,model.RightFootLift));
                    if(model.LeftFootLift<.001f||model.RightFootLift<.001f)support++;
                    Sample(label);
                }
                float travel=(Player.GetProperty("x").GetSingle()-startX)*direction;
                Check(frames<75&&travel>7.5f,"ordinary input traverses the clear courtyard toward "+label);
                Check(displacements.Count>20&&displacements.Min()>.03f,"actual walking keeps continuous displacement through supporting-foot contacts: "+label);
                Check(minPose<-.3f&&maxPose>.3f&&maxLift>.03f&&maxLift<.05f&&support==frames,"actual scene alternates readable low supporting and returning feet: "+label);
                Check(Mathf.Abs(model.GlobalPosition.Z-startZ)<.1f&&Player.GetProperty("fx").GetSingle()*direction>.99f,"direction reversal keeps the real actor on the courtyard path: "+label);
                Event("walking-showcase-leg",new {label,frames,seconds=frames/30f,authorityTravel=travel,renderTravel=(model.GlobalPosition.X-startRender)*direction,minStep=displacements.Min(),maxStep=displacements.Max(),minPose,maxPose,maxLift,support});
            }
            await WalkLeg(1,14.5f,"right");
            await Capture("walk-feel-turn",0);
            await WalkLeg(-1,6.5f,"left");
            Input(0,default);Event("walking-showcase-release",new {movieFrame=Engine.GetProcessFrames()});
            float settledX=0;
            for(int i=0;i<30;i++)
            {
                Follow();await Frames(1);Sample("settle");
                if(i==15)settledX=model.GlobalPosition.X;
            }
            Check(Mathf.Abs(model.GlobalPosition.X-settledX)<.005f&&Mathf.Abs(model.WalkPose)<.002f&&model.LeftFootLift<.001f&&model.RightFootLift<.001f,"actual walking settles to planted rest without root drift after release");
            Check(Player.GetProperty("alive").GetBoolean()&&!Player.GetProperty("stunned").GetBoolean(),"recorded walking path completes without respawn or action interruption");
            await Capture("walk-feel-side-settled",0);
            Event("walking-showcase-end",new {movieFrame=Engine.GetProcessFrames(),end=model.GlobalPosition.ToString()});
        }
        finally
        {
            Input(0,default);battleCamera.MakeCurrent();inspection.QueueFree();
            _root.Back();await Frames(3);
        }
    }
}

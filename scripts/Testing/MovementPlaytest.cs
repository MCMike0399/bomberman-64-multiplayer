using Godot;
using Bomber.Client;
using Bomber.Game;
using BlastCircuit.Rendering;
namespace Bomber.Testing;

public partial class PlaytestDriver
{
    private async Task MovementQuality()
    {
        StrideQuality();
        foreach(int fps in new[]{30,60,120})
        {
            var motion=new SnapshotMotion(Vector3.Zero,0);int next=2;float last=0;var deltas=new List<float>();
            for(int frame=0;frame<fps*4;frame++)
            {
                double now=(frame+1d)/fps;
                while(next/30d+((next/2)%2==0?.012:-.012)<=now)
                {motion.Push(new Vector3(next/30f*4.5f,0,0),next);next+=2;}
                float x=motion.Advance(1f/fps).X;
                if(frame>fps)deltas.Add(x-last);
                last=x;
            }
            float min=deltas.Min(),max=deltas.Max();
            Event("snapshot-jitter",new {fps,min,max,ratio=max/Mathf.Max(min,.00001f)});
            Check(min>0&&max/min<1.3f,"jittered snapshot delivery preserves smooth motion at "+fps+" FPS");
            float held=0;for(int i=0;i<fps;i++)held=motion.Advance(1f/fps).X;
            Check(Math.Abs(held-(next-2)/30f*4.5f)<.001f,"packet gap holds last authority position without extrapolation at "+fps);
            motion.Push(new Vector3(-20,0,0),next-4);
            Check(Math.Abs(motion.Advance(1f/fps).X-held)<.001f,"stale motion snapshot cannot move actor backwards at "+fps);
            motion.Push(new Vector3(40,0,0),next+30);
            Check(Math.Abs(motion.Advance(1f/fps).X-40)<.001f,"discontinuous respawn snaps rather than sweeping through the arena at "+fps);
        }
        _root.StartLocal(new SessionSettings(Stage:"rock-garden",Bots:0),1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"movement quality reaches actual fight",150);
        var model=_root.FindChildren("*","",true,false).OfType<BomberModel>().Single(m=>m.IsVisibleInTree()&&m.GetViewport()==_root.GetViewport());
        Input(0,new PlayerInput(1,0));await Frames(10);
        var steps=new List<float>();float previous=model.Position.X;
        for(int i=0;i<20;i++){await Frames(1);steps.Add(model.Position.X-previous);previous=model.Position.X;}
        float low=steps.Min(),high=steps.Max();
        Event("render-motion-cadence",new {low,high,ratio=high/Mathf.Max(low,.00001f),steps});
        Check(low>.03f,"held input moves the rendered character every frame");
        Check(high/low<1.3f,"rendered movement does not pulse between authority snapshots");
        await Capture("movement-stride");
        Input(0,default);await Frames(12);
        float stopped=model.Position.X;await Frames(8);
        Check(Math.Abs(model.Position.X-stopped)<.005f,"rendered character settles after release without drift");
        _root.Back();await Frames(3);
    }
    private void StrideQuality()
    {
        var endPoses=new List<float>();
        foreach(int fps in new[]{30,60,120})
        {
            var model=new BomberModel(new ArtMaterials(),Colors.White,"stride check");
            try
            {
                float dt=1f/fps,previous=0,maxLift=0;int contacts=0,leftSupport=0,doubleSupport=0;
                for(int frame=0;frame<fps*4;frame++)
                {
                    model.Animate(dt,dt*4.5f);
                    float pose=model.WalkPose;
                    if(previous<0&&pose>=0)contacts++;
                    previous=pose;
                    maxLift=Mathf.Max(maxLift,Mathf.Max(model.LeftFootLift,model.RightFootLift));
                    if(model.LeftFootLift<.001f)leftSupport++;
                    if(model.LeftFootLift<.001f&&model.RightFootLift<.001f)doubleSupport++;
                }
                endPoses.Add(model.WalkPose);
                Event("walk-stride",new {fps,distance=18,contacts,maxLift,supportFraction=leftSupport/(fps*4f),doubleSupportFraction=doubleSupport/(fps*4f)});
                Check(contacts is >=8 and <=10,"normal travel uses deliberate strides rather than tiny rapid steps at "+fps+" FPS");
                Check(leftSupport/(fps*4f)>.58f&&doubleSupport/(fps*4f)>.20f,"walking includes sustained support and both-feet-down contact at "+fps+" FPS");
                Check(maxLift>.03f&&maxLift<.05f,"returning foot clears the floor with restrained lift at "+fps+" FPS");
                float beforeStop=model.WalkPose;bool noExtraStep=true;
                for(int frame=0;frame<fps;frame++)
                {
                    model.Animate(dt,0);
                    noExtraStep&=model.WalkPose*beforeStop>=0;
                }
                Check(noExtraStep,"stopping settles the planted stride without taking extra steps at "+fps+" FPS");
                Check(Mathf.Abs(model.WalkPose)<.001f&&model.LeftFootLift<.001f&&model.RightFootLift<.001f,"feet settle to rest after movement release at "+fps+" FPS");
            }
            finally {model.Free();}
        }
        Check(endPoses.Max()-endPoses.Min()<.002f,"equal displacement reaches the same stride pose at 30/60/120 FPS");
    }
}

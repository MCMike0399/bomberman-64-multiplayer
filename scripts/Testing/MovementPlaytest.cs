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
        CompactLegs();
        WalkPhrasing();
        BootContinuity();
        var endPoses=new List<float>();
        foreach(int fps in new[]{30,60,120})
        {
            var model=new BomberModel(new ArtMaterials(),Colors.White,"stride check");
            try
            {
                float dt=1f/fps,previous=0,maxLift=0,minLift=0,minBody=1,maxBody=-1,maxCompression=0,maxFootSeparation=0;int contacts=0,leftSupport=0,flight=0;
                for(int frame=0;frame<fps*4;frame++)
                {
                    model.Animate(dt,dt*4.5f);
                    float pose=model.WalkPose;
                    if(previous<0&&pose>=0)contacts++;
                    previous=pose;
                    maxLift=Mathf.Max(maxLift,Mathf.Max(model.LeftFootLift,model.RightFootLift));
                    minLift=Mathf.Min(minLift,Mathf.Min(model.LeftFootLift,model.RightFootLift));
                    maxFootSeparation=Mathf.Max(maxFootSeparation,Mathf.Abs(model.LeftFootLift-model.RightFootLift));
                    minBody=Mathf.Min(minBody,model.WalkBodyHeight);maxBody=Mathf.Max(maxBody,model.WalkBodyHeight);maxCompression=Mathf.Max(maxCompression,model.WalkCompression);
                    if(Mathf.Abs(model.LeftFootLift)<.01f)leftSupport++;
                    if(model.LeftFootLift>.01f&&model.RightFootLift>.01f)flight++;
                }
                endPoses.Add(model.WalkPose);
                Event("walk-stride",new {fps,distance=18,contacts,maxLift,minLift,minBody,maxBody,maxCompression,maxFootSeparation,supportFraction=leftSupport/(fps*4f),flightFraction=flight/(fps*4f)});
                Check(contacts is >=8 and <=10,"normal travel uses deliberate strides rather than tiny rapid steps at "+fps+" FPS");
                Check(maxFootSeparation>.03f&&maxFootSeparation<.13f,"short rigid legs retain a readable alternating boot silhouette at "+fps+" FPS");
                Check(maxLift>.025f&&maxLift<.12f&&minLift>-.02f,"compact recovery boots clear the floor without sinking or a large jump at "+fps+" FPS");
                Check(maxBody-minBody>.015f&&maxBody-minBody<.065f&&maxCompression>.005f&&maxCompression<.03f,"body springs gently within the short rigid leg range at "+fps+" FPS");
                float beforeStop=model.WalkPose;bool noExtraStep=true;
                for(int frame=0;frame<fps;frame++)
                {
                    model.Animate(dt,0);
                    noExtraStep&=model.WalkPose*beforeStop>=0;
                }
                Check(noExtraStep,"stopping settles the planted stride without taking extra steps at "+fps+" FPS");
                Check(Mathf.Abs(model.WalkPose)<.001f&&Mathf.Abs(model.LeftFootLift)<.02f&&Mathf.Abs(model.RightFootLift)<.02f,"feet settle to rest after movement release at "+fps+" FPS");
            }
            finally {model.Free();}
        }
        Check(endPoses.Max()-endPoses.Min()<.002f,"equal displacement reaches the same stride pose at 30/60/120 FPS");
    }
    private void WalkPhrasing()
    {
        var model=new BomberModel(new ArtMaterials(),Colors.White,"walk phrasing");
        try
        {
            for(int i=0;i<60;i++)model.Animate(1f/30,4.5f/30);
            float previous=model.WalkBodyHeight,maxStep=0,maxReach=0;
            for(int i=0;i<180;i++)
            {
                model.Animate(1f/30,4.5f/30);
                maxStep=Mathf.Max(maxStep,Mathf.Abs(model.WalkBodyHeight-previous));
                maxReach=Mathf.Max(maxReach,Mathf.Abs(model.WalkPose));previous=model.WalkBodyHeight;
            }
            Event("walk-phrasing",new {maxStep,maxReach});
            Check(maxReach<.53f,"short legs stay within their intended swing without recovery overshoot");
            Check(maxStep<.015f,"body does not drop almost its entire bounce in one 30 FPS frame");
        }
        finally {model.Free();}
    }
    private void CompactLegs()
    {
        var model=new BomberModel(new ArtMaterials(),Colors.White,"compact legs");
        try
        {
            float maxStretch=0,maxHipGap=0;
            for(int frame=0;frame<240;frame++)
            {
                model.Animate(1f/60,4.5f/60);
                maxStretch=Mathf.Max(maxStretch,model.WalkLegStretch);maxHipGap=Mathf.Max(maxHipGap,model.WalkHipSeparation);
            }
            Event("compact-legs",new {maxStretch,maxHipGap});
            Check(Mathf.Abs(maxStretch-1)<.001f,"walking white legs retain their short fixed length throughout the stride");
            Check(maxHipGap<.001f,"walking legs stay attached to their actual hips throughout the stride");
        }
        finally {model.Free();}
    }
    private void BootContinuity()
    {
        // Sample real composed boot transforms, including the leg and body. The
        // fine pass exposes a boundary snap that coarse movie frames can hide.
        foreach(int fps in new[]{30,60,120,1200})
        {
            var model=new BomberModel(new ArtMaterials(),Colors.White,"boot continuity");
            try
            {
                float dt=1f/fps,maxAngularSpeed=0,maxAcceleration=0;
                for(int frame=0;frame<fps;frame++)model.Animate(dt,dt*4.5f);
                var left=model.LeftBootOrientation;var right=model.RightBootOrientation;
                var leftCenter=model.LeftBootCenter;var rightCenter=model.RightBootCenter;
                var leftVelocity=Vector3.Zero;var rightVelocity=Vector3.Zero;
                for(int frame=0;frame<fps*2;frame++)
                {
                    model.Animate(dt,dt*4.5f);
                    maxAngularSpeed=Mathf.Max(maxAngularSpeed,Mathf.Max(left.AngleTo(model.LeftBootOrientation),right.AngleTo(model.RightBootOrientation))/dt);
                    var nextLeftVelocity=(model.LeftBootCenter-leftCenter)/dt;var nextRightVelocity=(model.RightBootCenter-rightCenter)/dt;
                    if(frame>0)maxAcceleration=Mathf.Max(maxAcceleration,Mathf.Max((nextLeftVelocity-leftVelocity).Length(),(nextRightVelocity-rightVelocity).Length())/dt);
                    leftVelocity=nextLeftVelocity;rightVelocity=nextRightVelocity;leftCenter=model.LeftBootCenter;rightCenter=model.RightBootCenter;
                    left=model.LeftBootOrientation;right=model.RightBootOrientation;
                }
                Event("boot-continuity",new {fps,maxAngularSpeed,maxAcceleration});
                Check(maxAngularSpeed<16,"actual boot orientation stays continuous across toe-off and touchdown at "+fps+" FPS");
                Check(maxAcceleration<400,"actual boot recovery avoids a velocity corner at "+fps+" FPS");
            }
            finally {model.Free();}
        }
    }
}

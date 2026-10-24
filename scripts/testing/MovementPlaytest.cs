using Godot;
using Bomber.Client;
using Bomber.Game;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Models;
using BlastCircuit.Rendering;
namespace BlastCircuit.Testing;

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
        WholeBodyWalk();
        BootContinuity();
        var endPoses=new List<float>();
        foreach(int fps in new[]{30,60,120})
        {
            var model=new BomberModel(new ArtMaterials(),Colors.White,"stride check");
            try
            {
                float dt=1f/fps,previous=0,maxLift=0,minLift=0,minBody=1,maxBody=-1,maxCompression=0,maxFootSeparation=0;int contacts=0,leftSupport=0,flight=0;
                // World-space boot travel while a boot is down. The root advances
                // along local +Z, so a gripping sole cancels that travel.
                float travel=0,slip=0,supportTravel=0,leftWorld=0,rightWorld=0;bool leftDown=false,rightDown=false;
                for(int frame=0;frame<fps*4;frame++)
                {
                    model.Animate(dt,dt*4.5f);travel+=dt*4.5f;
                    float leftNow=travel+model.LeftBootCenter.Z*model.Scale.Z,rightNow=travel+model.RightBootCenter.Z*model.Scale.Z;
                    bool leftContact=model.LeftFootLift<.02f,rightContact=model.RightFootLift<.02f;
                    if(frame>fps)
                    {
                        if(leftDown&&leftContact){slip+=Mathf.Abs(leftNow-leftWorld);supportTravel+=dt*4.5f;}
                        if(rightDown&&rightContact){slip+=Mathf.Abs(rightNow-rightWorld);supportTravel+=dt*4.5f;}
                    }
                    leftWorld=leftNow;rightWorld=rightNow;leftDown=leftContact;rightDown=rightContact;
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
                float skating=supportTravel>0?slip/supportTravel:1;
                Event("walk-stride",new {fps,distance=18,contacts,maxLift,minLift,minBody,maxBody,maxCompression,maxFootSeparation,supportFraction=leftSupport/(fps*4f),flightFraction=flight/(fps*4f),skating});
                Check(skating<.3f,"planted boots grip the floor instead of skating with the body at "+fps+" FPS");
                // 1.5-unit strides: quick enough for short legs to grip the floor.
                Check(contacts is >=11 and <=13,"normal travel uses a short-legged scamper matched to ground speed at "+fps+" FPS");
                Check(maxFootSeparation>.03f&&maxFootSeparation<.13f,"short rigid legs retain a readable alternating boot silhouette at "+fps+" FPS");
                Check(maxLift>.025f&&maxLift<.135f&&minLift>-.02f,"compact recovery boots clear the floor without sinking or a large jump at "+fps+" FPS");
                Check(maxBody-minBody>.015f&&maxBody-minBody<.065f&&maxCompression>.02f&&maxCompression<.07f,"body squashes into contacts within the short rigid leg range at "+fps+" FPS");
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
    private void WholeBodyWalk()
    {
        foreach(int fps in new[]{30,60,120})
        {
            var model=new BomberModel(new ArtMaterials(),Colors.White,"whole body walk");
            try
            {
                float dt=1f/fps,minX=1,maxX=-1,minRoll=1,maxRoll=-1,minHead=1,maxHead=-1,minShoulder=1,maxShoulder=-1,minAntenna=1,maxAntenna=-1;
                for(int i=0;i<fps;i++)model.Animate(dt,4.5f*dt);
                for(int i=0;i<fps*3;i++)
                {
                    model.Animate(dt,4.5f*dt);
                    minX=Mathf.Min(minX,model.WalkBodyOffset.X);maxX=Mathf.Max(maxX,model.WalkBodyOffset.X);
                    minRoll=Mathf.Min(minRoll,model.WalkBodyRotation.Z);maxRoll=Mathf.Max(maxRoll,model.WalkBodyRotation.Z);
                    minHead=Mathf.Min(minHead,model.WalkHeadRotation.Z);maxHead=Mathf.Max(maxHead,model.WalkHeadRotation.Z);
                    minShoulder=Mathf.Min(minShoulder,model.WalkShoulderRotation.Y);maxShoulder=Mathf.Max(maxShoulder,model.WalkShoulderRotation.Y);
                    minAntenna=Mathf.Min(minAntenna,model.AntennaRotation.X);maxAntenna=Mathf.Max(maxAntenna,model.AntennaRotation.X);
                }
                // Release: the antenna keeps swinging from momentum, then settles upright.
                float stopSwing=0;
                for(int i=0;i<fps/2;i++){model.Animate(dt,0);stopSwing=Mathf.Max(stopSwing,Mathf.Abs(model.AntennaRotation.X));}
                for(int i=0;i<fps*2;i++)model.Animate(dt,0);
                float settledAntenna=model.AntennaRotation.Length();
                Event("whole-body-walk",new{fps,lateralTravel=maxX-minX,weightRoll=maxRoll-minRoll,headCounterRoll=maxHead-minHead,shoulderTurn=maxShoulder-minShoulder,antennaSwing=maxAntenna-minAntenna,stopSwing,settledAntenna});
                // Art-direction bounds: readable weight transfer at the battle camera,
                // while keeping the head balanced and the short-leg silhouette intact.
                Check(maxX-minX>.035f&&maxX-minX<.07f,"walking transfers visible body weight between supporting sides at "+fps);
                Check(maxRoll-minRoll>.085f&&maxRoll-minRoll<.18f,"hips visibly lean through the stride without a toy-like rigid trunk at "+fps);
                Check(maxShoulder-minShoulder>.12f&&maxShoulder-minShoulder<.24f,"shoulders follow the stride independently from the hips at "+fps);
                Check(maxHead-minHead>.045f&&maxHead-minHead<.15f,"head balances the body with readable counter-motion at "+fps);
                Check(maxAntenna-minAntenna>.08f&&maxAntenna-minAntenna<.9f,"antenna trails the helmet with springy follow-through at "+fps);
                Check(stopSwing>.05f&&settledAntenna<.01f,"antenna sways from momentum on stopping, then settles upright at "+fps);
            }
            finally{model.Free();}
        }
    }
    private void WalkPhrasing()
    {
        var model=new BomberModel(new ArtMaterials(),Colors.White,"walk phrasing");
        try
        {
            for(int i=0;i<60;i++)model.Animate(1f/30,4.5f/30);
            float previous=model.WalkBodyHeight,maxStep=0,maxReach=0,low=1,high=-1;
            for(int i=0;i<180;i++)
            {
                model.Animate(1f/30,4.5f/30);
                maxStep=Mathf.Max(maxStep,Mathf.Abs(model.WalkBodyHeight-previous));
                low=Mathf.Min(low,model.WalkBodyHeight);high=Mathf.Max(high,model.WalkBodyHeight);
                maxReach=Mathf.Max(maxReach,Mathf.Abs(model.WalkPose));previous=model.WalkBodyHeight;
            }
            Event("walk-phrasing",new {maxStep,maxReach,bounce=high-low});
            // .62 rad swing, 7% swing follow-through, plus .11 rad that keeps the boots under the run lean.
            Check(maxReach<.79f,"short legs stay within their intended swing without recovery overshoot");
            // About five 30 FPS frames per step: even a pure sine bob moves 59% of its
            // range in one frame. Guard the rejected pop of nearly the whole bounce.
            Check(maxStep<.75f*(high-low),"body does not drop almost its entire bounce in one 30 FPS frame");
        }
        finally {model.Free();}
    }
    private void CompactLegs()
    {
        var model=new BomberModel(new ArtMaterials(),Colors.White,"compact legs");
        try
        {
            float maxStretch=0,maxHipGap=0,maxSlide=0,maxTuck=0;
            for(int frame=0;frame<240;frame++)
            {
                model.Animate(1f/60,4.5f/60);
                maxStretch=Mathf.Max(maxStretch,model.WalkLegStretch);maxHipGap=Mathf.Max(maxHipGap,model.WalkHipSeparation);
                maxSlide=Mathf.Max(maxSlide,model.WalkHipSlide);maxTuck=Mathf.Max(maxTuck,model.WalkLegTuck);
            }
            Event("compact-legs",new {maxStretch,maxHipGap,maxSlide,maxTuck});
            // The belt spans y .36..45 and z -.155..+.185 around the hips: slide and tuck stay inside the suit.
            Check(maxSlide<.075f&&maxTuck<.06f,"hip slide and passing tuck stay hidden under the belt");
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

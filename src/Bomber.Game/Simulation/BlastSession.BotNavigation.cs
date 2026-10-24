namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private sealed class BotMotion
    {
        public float X,Z,Elapsed,IdleX,IdleZ,IdleElapsed,RecoveryX,RecoveryZ;
        public int Waypoint=-1,Avoid=-1,AvoidUntil,Roam=-1,RoamUntil,RoamSequence,RecoveryUntil;
    }
    private readonly Dictionary<string,BotMotion> _botMotion=new(StringComparer.Ordinal);
    private void BotResetNavigation()=>_botMotion.Clear();
    private BotMotion BotMotionFor(BlastPlayer bot)
    {
        if(!_botMotion.TryGetValue(bot.Id,out var motion))
            _botMotion[bot.Id]=motion=new BotMotion {X=bot.X,Z=bot.Z,IdleX=bot.X,IdleZ=bot.Z};
        return motion;
    }
    private void BotTrackWaypoint(BlastPlayer bot,int waypoint)=>BotMotionFor(bot).Waypoint=waypoint;
    private bool BotAvoidsCell(BlastPlayer bot,int cell)
    {
        var motion=BotMotionFor(bot);
        return motion.Avoid==cell&&_tickCount<motion.AvoidUntil;
    }
    private void BotObserveProgress(BlastPlayer bot,float dt)
    {
        var motion=BotMotionFor(bot);
        // A kick plant, carried state or stun is an intentional movement pause.
        // Start a fresh observation after it instead of banning its valid route.
        if(bot.StunTimer>0||bot.CarriedById is not null||bot.Airborne||bot.KickWindupRemaining>0||bot.KickRecoveryRemaining>0)
        {motion.X=bot.X;motion.Z=bot.Z;motion.Elapsed=0;motion.IdleElapsed=0;motion.IdleX=bot.X;motion.IdleZ=bot.Z;motion.Roam=-1;return;}
        if(Dist2(bot.X,bot.Z,motion.IdleX,motion.IdleZ)>.12f*.12f)
        {motion.IdleElapsed=0;motion.IdleX=bot.X;motion.IdleZ=bot.Z;}
        else motion.IdleElapsed+=dt;
        if(bot.Input.Length<.001f){motion.X=bot.X;motion.Z=bot.Z;motion.Elapsed=0;return;}
        motion.Elapsed+=dt;
        if(motion.Elapsed<.6f)return;
        if(Dist2(bot.X,bot.Z,motion.X,motion.Z)<.12f*.12f&&motion.Waypoint>=0&&motion.Waypoint!=_aiGraph.Cell(bot.X,bot.Z))
        {
            // Forget only the failed next waypoint, for a finite interval. The usual
            // timed-danger search chooses an alternate legal edge or waits safely.
            motion.Avoid=motion.Waypoint;motion.AvoidUntil=_tickCount+45;
            bot.BotThinkTimer=0;
            bot.BotPlacingBomb=false;
        }
        motion.X=bot.X;motion.Z=bot.Z;motion.Elapsed=0;
    }
    private int BotIdleWaypoint(BlastPlayer bot,int start,int waypoint)
    {
        var motion=BotMotionFor(bot);
        if(waypoint!=start&&waypoint>=0)return waypoint;
        if(!BotSafe(_aiDanger,start,0,.5f))return waypoint;
        if(motion.Roam>=0&&_tickCount<motion.RoamUntil&&BotOpen(motion.Roam,start,bot,_aiDanger)&&BotSafe(_aiDanger,motion.Roam,0,_aiProfile.SafetyHorizonSeconds))return motion.Roam;
        if(motion.IdleElapsed<.9f)return waypoint;
        // Local tactical optima can produce a permanent neutral input even with
        // open terrain. Take a bounded safe step, then reconsider the visible goals.
        for(int d=0;d<4;d++)
        {
            int next=_aiGraph.Edges[start*4+((d+bot.Slot+motion.RoamSequence)%4)];
            if(next<0||!BotOpen(next,start,bot,_aiDanger)||!BotSafe(_aiDanger,next,0,_aiProfile.SafetyHorizonSeconds))continue;
            motion.Roam=next;motion.RoamUntil=_tickCount+18;motion.RoamSequence++;
            motion.IdleElapsed=0;
            return next;
        }
        return waypoint;
    }

    private bool BotDirectionMoves(BlastPlayer bot,int start,float dx,float dz)
    {
        // Probe the same axis-separated collision checks as ordinary authority
        // movement. A graph edge can be open while an off-center body cannot turn.
        float step=(PlayerSpeed*bot.StatusSpeed)/30,x=bot.X,z=bot.Z;
        if(!CollidesWithSolid(x+dx*step,z,bot.Tier,bot))x+=dx*step;
        if(!CollidesWithSolid(x,z+dz*step,bot.Tier,bot))z+=dz*step;
        float minimumProgress=step*MathF.Sqrt(dx*dx+dz*dz)*.1f;
        if(Dist2(x,z,bot.X,bot.Z)<minimumProgress*minimumProgress)return false;
        foreach(var other in _players.Values)
        {
            if(other.Id==bot.Id||!HasBody(other)||Math.Abs(other.Y-bot.Y)>=HeadHeight-.05f)continue;
            float before=Dist2(bot.X,bot.Z,other.X,other.Z),after=Dist2(x,z,other.X,other.Z);
            if(after<(PlayerRadius*2+.01f)*(PlayerRadius*2+.01f)&&after<before-.00001f)return false;
        }
        int next=_aiGraph.Cell(x,z);
        if(next<0)return false;
        // An escape crosses threatened cells before the fuse; requiring a safe
        // destination for the entire horizon here contradicts the timed route.
        float horizon=BotSafe(_aiDanger,start,0,_aiProfile.SafetyHorizonSeconds)?_aiProfile.SafetyHorizonSeconds:.4f;
        if(next!=start&&(!BotOpen(next,start,bot,_aiDanger)||!BotSafe(_aiDanger,next,0,horizon)))return false;
        return true;
    }
    private InputState BotUnblockedInput(BlastPlayer bot,int start,InputState desired)
    {
        var motion=BotMotionFor(bot);
        if(_tickCount<motion.RecoveryUntil&&BotDirectionMoves(bot,start,motion.RecoveryX,motion.RecoveryZ))
            return new InputState(motion.RecoveryX,motion.RecoveryZ,false,false,false);
        if(desired.Length<.001f||BotDirectionMoves(bot,start,desired.X,desired.Z))return desired;
        float best=float.NegativeInfinity,bx=0,bz=0;
        for(int d=0;d<8;d++)
        {
            float angle=(d+bot.Slot)*MathF.PI/4,x=MathF.Cos(angle),z=MathF.Sin(angle);
            if(!BotDirectionMoves(bot,start,x,z))continue;
            float score=x*desired.X+z*desired.Z;
            if(score>best){best=score;bx=x;bz=z;}
        }
        if(!float.IsFinite(best))return default;
        motion.RecoveryX=bx;motion.RecoveryZ=bz;motion.RecoveryUntil=_tickCount+12;
        return new InputState(bx,bz,false,false,false);
    }

}

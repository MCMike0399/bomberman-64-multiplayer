using Bomber.Game.Simulation.State;

namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private sealed class BotMotion
    {
        public float X,Z,Elapsed,IdleX,IdleZ,IdleElapsed,RecoveryX,RecoveryZ;
        public int Waypoint=-1,Avoid=-1,AvoidUntil,Roam=-1,RoamUntil,RoamSequence,RecoveryUntil;
        public int StepFrom=-1,StepTo=-1,StepUntil;
        public int LastCell=-1,PreviousCell=-1,Reversals,BoredA=-1,BoredB=-1,BoredUntil;
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
        if(Dist2(bot.X,bot.Z,motion.X,motion.Z)<.12f*.12f&&motion.Waypoint>=0&&motion.Waypoint!=_aiGraph.Cell(bot.X,bot.Z,bot.Y))
        {
            // Forget only the failed next waypoint, for a finite interval. The usual
            // timed-danger search chooses an alternate legal edge or waits safely.
            motion.Avoid=motion.Waypoint;motion.AvoidUntil=_tickCount+45;
            bot.BotThinkTimer=0;
            bot.BotPlacingBomb=false;
        }
        motion.X=bot.X;motion.Z=bot.Z;motion.Elapsed=0;
    }
    /// <summary>Finish a started one-cell step while it stays open and safe. Danger
    /// still preempts it through the urgent escape branch, which bypasses this.</summary>
    private int BotFinishStep(BlastPlayer bot,int start,int waypoint)
    {
        var motion=BotMotionFor(bot);
        if(motion.StepFrom==start&&motion.StepTo>=0&&motion.StepTo!=waypoint&&_tickCount<motion.StepUntil&&
            BotOpen(motion.StepTo,start,bot,_aiDanger)&&BotSafe(_aiDanger,motion.StepTo,0,_aiProfile.SafetyHorizonSeconds))
            return motion.StepTo;
        if(waypoint>=0&&waypoint!=start&&(motion.StepFrom!=start||motion.StepTo!=waypoint))
        {motion.StepFrom=start;motion.StepTo=waypoint;motion.StepUntil=_tickCount+StepCommitTicks;}
        return waypoint;
    }
    // About two cell traversals at ordinary speed, including the turn alignment.
    private const int StepCommitTicks=15,ReversalLimit=3,BoredTicks=60;
    /// <summary>Count returns to the cell left one move ago. Two CPUs keeping their
    /// distance can mirror each other across one boundary indefinitely; after a few
    /// reversals without a bomb, both cells lose their appeal as goals for a while.</summary>
    private void BotNoteCell(BlastPlayer bot,int cell)
    {
        var motion=BotMotionFor(bot);
        if(cell==motion.LastCell)return;
        motion.Reversals=cell==motion.PreviousCell?motion.Reversals+1:0;
        motion.PreviousCell=motion.LastCell;motion.LastCell=cell;
        if(motion.Reversals<ReversalLimit)return;
        motion.BoredA=cell;motion.BoredB=motion.PreviousCell;motion.BoredUntil=_tickCount+BoredTicks;motion.Reversals=0;
        // Choose afresh, then hold that choice (see BotSearch) for the whole interval.
        _aiCommittedGoals.Remove(bot.Id);
    }
    private bool BotBored(BlastPlayer bot)=>_tickCount<BotMotionFor(bot).BoredUntil;
    /// <summary>A body pressed against an obstacle on a cell boundary flickers between
    /// two cells, and every decision then plans from the other side. Keep the previous
    /// cell until the body is clearly past its edge.</summary>
    private int BotStableCell(BlastPlayer bot,int cell)
    {
        int last=BotMotionFor(bot).LastCell;
        if(last<0||last==cell||Math.Abs(_aiGraph.Height[last]-_aiGraph.Height[cell])>=.5f)return cell;
        float x=_aiGraph.X(last),z=_aiGraph.Z(last);
        return bot.X>x-CellHysteresis&&bot.X<x+1+CellHysteresis&&bot.Z>z-CellHysteresis&&bot.Z<z+1+CellHysteresis?last:cell;
    }
    private const float CellHysteresis=.12f;
    private void BotNoteBomb(BlastPlayer bot)=>BotMotionFor(bot).Reversals=0;
    private bool BotBoredWith(BlastPlayer bot,int cell)
    {
        var motion=BotMotionFor(bot);
        return _tickCount<motion.BoredUntil&&(cell==motion.BoredA||cell==motion.BoredB);
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
        // This input persists until the next steering reaction. A one-tick
        // probe let recovery sidesteps walk off narrow stairs before replanning.
        float step=(PlayerSpeed*bot.StatusSpeed)*_aiSteeringInterval,x=bot.X,z=bot.Z;
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
        int next=_aiGraph.Cell(x,z,bot.Y);
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
        // A held sidestep must not override a reversed plan: a stale recovery
        // walked an escaping bot back into a kicked bomb's lane.
        if(_tickCount<motion.RecoveryUntil&&motion.RecoveryX*desired.X+motion.RecoveryZ*desired.Z>=0&&
            BotDirectionMoves(bot,start,motion.RecoveryX,motion.RecoveryZ))
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

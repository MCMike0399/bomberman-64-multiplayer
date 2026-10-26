using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

namespace Bomber.Game.Simulation;

/// <summary>Finite knockdowns, victim-owned struggle input, paired holds and thrown-player physics.</summary>
public sealed partial class BlastSession
{
    private const float HoldSeconds=3.2f,EffortSeconds=.24f,EffortCooldown=.10f,PlayerCarryHeight=1.4f;
    private const float ImpactProtectionSeconds=.35f;
    private static bool CanReceiveBombImpact(BlastPlayer p)=>p.Alive&&!p.Ghost&&
        p.InvincibleTimer<=0&&p.ImpactProtectionTimer<=0&&p.CarriedById is null;
    private static void ClearRecovery(BlastPlayer p)
    {
        p.StunTimer=0;p.CarriedById=null;p.HoldRemaining=0;p.RecoveryTotal=0;
        p.StruggleCooldown=0;p.RecoveryX=p.RecoveryZ=0;p.KnockdownReason=null;
        p.Airborne=false;p.Falling=false;p.AirVX=p.AirVY=p.AirVZ=0;
        p.ImpactProtectionTimer=0;
    }
    private bool ProcessRecovery(BlastPlayer p,InputState input,float dt)
    {
        if(p.StunTimer<=0&&p.CarriedById is null&&!p.Airborne)return false;
        float magnitude=input.X*input.X+input.Z*input.Z;
        float previous=p.RecoveryX*p.RecoveryX+p.RecoveryZ*p.RecoveryZ;
        bool direction=magnitude>.20f&&(previous<.04f||input.X*p.RecoveryX+input.Z*p.RecoveryZ<.2f);
        bool press=(input.A&&!p.PrevA)||(input.B&&!p.PrevB)||(input.R&&!p.PrevR);
        p.RecoveryX=input.X;p.RecoveryZ=input.Z;
        p.PrevA=input.A;p.PrevB=input.B;p.PrevR=input.R;p.PrevStop=input.Stop;
        p.StruggleCooldown=Math.Max(0,p.StruggleCooldown-dt);
        float effort=0;
        if(!p.Airborne&&(direction||press)&&p.StruggleCooldown<=0)
        {effort=EffortSeconds;p.StruggleCooldown=EffortCooldown;if(_tutorialStarted&&p.Slot==0)_tutorialEfforts++;}
        if(p.CarriedById is not null)
        {
            p.HoldRemaining=Math.Max(0,p.HoldRemaining-dt-effort);
            if(p.HoldRemaining<=0)ReleaseHeld(p,"escaped");
        }
        else if(!p.Airborne)
        {
            p.StunTimer=Math.Max(0,p.StunTimer-dt-effort);
            if(p.StunTimer<=0)
            {
                p.KnockdownReason=null;
                Present("recover",p.Id,p.Id,p.X,p.Y,p.Z);
            }
        }
        // Consume recovery edges even on the get-up tick; no buffered bomb/throw surprises.
        return true;
    }
    private void KnockDown(BlastPlayer p,float duration,string reason,string? entity,string? causedBy)
    {
        if(!CanReceiveBombImpact(p))return;
        DropCarriedBombs(p);
        p.StunTimer=duration;p.RecoveryTotal=duration;p.KnockdownReason=reason;
        p.StruggleCooldown=0;p.RecoveryX=p.Input.X;p.RecoveryZ=p.Input.Z;
        p.ImpactProtectionTimer=ImpactProtectionSeconds;
        Present("knockdown",entity,p.Id,p.X,p.Y,p.Z,duration,reason:reason,causedBy:causedBy);
    }
    private void BeginHold(BlastPlayer carrier,BlastPlayer victim)
    {
        if(_stage.Ceiling(carrier.X,carrier.Z,PlayerRadius,carrier.Y,carrier.Y+PlayerCarryHeight,HeadHeight)<carrier.Y+PlayerCarryHeight-.001f)return;
        DropCarriedBombs(victim);
        carrier.CarryingPlayerId=victim.Id;victim.CarriedById=carrier.Id;
        victim.HoldRemaining=HoldSeconds;victim.RecoveryTotal=HoldSeconds;victim.StunTimer=.5f;
        victim.KnockdownReason="held";victim.StruggleCooldown=0;
        Present("player-grab",victim.Id,carrier.Id,victim.X,victim.Y,victim.Z,reason:"player");
    }
    private void ReleaseHeld(BlastPlayer victim,string reason)
    {
        string? carrierId=victim.CarriedById;
        if(carrierId!=null&&_players.TryGetValue(carrierId,out var carrier)&&carrier.CarryingPlayerId==victim.Id)
        {
            carrier.CarryingPlayerId=null;
            victim.X=carrier.X;victim.Z=carrier.Z;
        }
        victim.CarriedById=null;victim.HoldRemaining=0;victim.StunTimer=0;victim.KnockdownReason=null;
        victim.SurfaceY=_stage.SurfaceAt(victim.X,victim.Z,victim.Y);victim.Tier=(int)MathF.Round(victim.Y);
        victim.ImpactProtectionTimer=Math.Max(victim.ImpactProtectionTimer,.6f);
        Present("player-release",victim.Id,victim.Id,victim.X,victim.Y,victim.Z,reason:reason,causedBy:carrierId);
    }
    private bool AirborneTerrainBlocks(float x,float z,BlastPlayer p)
    {
        if(p.Ghost&&_stage.AtHeight((int)MathF.Floor(x),(int)MathF.Floor(z),p.Y).IsElimination)return true;
        for(var cx=(int)MathF.Floor(x-PlayerRadius);cx<=(int)MathF.Floor(x+PlayerRadius);cx++)
            for(var cz=(int)MathF.Floor(z-PlayerRadius);cz<=(int)MathF.Floor(z+PlayerRadius);cz++)
            {
                if(!CircleOverlapsCell(x,z,cx,cz))continue;
                if(EscapingCell(x,z,p,cx,cz))continue;
                if(_stage.DeckOverlaps(cx,cz,p.Y,HeadHeight))return true;
                var cell=_stage.AtHeight(cx,cz,p.Y);
                if(cell.Kind==CellKind.Pillar||PlayerOverlapsSoftBlock(cx,cz,p.Y))return true;
                if(cell.IsPassable&&_stage.SurfaceAt(Math.Clamp(x,cx+.001f,cx+.999f),Math.Clamp(z,cz+.001f,cz+.999f),p.Y)>p.Y+.02f)return true;
            }
        return false;
    }
    private void TickPlayerCarrying(float dt)
    {
        foreach(var p in _players.Values)
        {
            if(!p.Alive)continue;
            if(p.CarriedById is {} carrierId)
            {
                if(!_players.TryGetValue(carrierId,out var carrier)||!carrier.Alive||carrier.Ghost||carrier.CarryingPlayerId!=p.Id)
                {ReleaseHeld(p,"carrier-lost");continue;}
                p.X=carrier.X;p.Z=carrier.Z;p.SurfaceY=carrier.Y+PlayerCarryHeight;p.Tier=carrier.Tier;
            }
            else if(p.Airborne)
            {
                var steps=Math.Max(1,(int)MathF.Ceiling(MathF.Sqrt(p.AirVX*p.AirVX+p.AirVZ*p.AirVZ)*dt/.1f));
                var stepDt=dt/steps;
                for(var step=0;step<steps&&p.Airborne;step++)
                {
                    var wind=BlizzardWind*BlizzardRules.PlayerWindScale;
                    float nx=p.X+(p.AirVX+wind.X)*stepDt,nz=p.Z+(p.AirVZ+wind.Y)*stepDt;
                    if(!AirborneTerrainBlocks(nx,p.Z,p))p.X=nx;
                    if(!AirborneTerrainBlocks(p.X,nz,p))p.Z=nz;
                    float previousY=p.Y;
                    p.AirVY-=(p.Falling?LedgeFallGravity:9.8f)*stepDt;p.SurfaceY+=p.AirVY*stepDt;
                    float ceiling=_stage.Ceiling(p.X,p.Z,PlayerRadius,previousY,p.Y,HeadHeight);
                    if(ceiling<p.Y){p.SurfaceY=ceiling;p.AirVY=Math.Min(0,p.AirVY);}
                    var cell=_stage.AtHeight((int)MathF.Floor(p.X),(int)MathF.Floor(p.Z),previousY);
                    float ground=PlayerLandingHeight(p.X,p.Z,previousY);
                    if(p.Y<=ground)
                    {
                        bool falling=p.Falling;p.Airborne=false;p.Falling=false;p.SurfaceY=ground;p.Tier=(int)MathF.Round(ground);
                        if(cell.IsElimination||p.X<0||p.Z<0||p.X>=_stage.Width||p.Z>=_stage.Height)
                        {Eliminate(null,p,cell.Kind==CellKind.Water?"water":"fall");break;}
                        if(!falling){p.StunTimer=.65f;p.RecoveryTotal=.65f;p.KnockdownReason="landing";}
                        Present("player-land",p.Id,p.Id,p.X,p.Y,p.Z,reason:falling?"fall":"throw");
                    }
                }
            }
        }
    }
}

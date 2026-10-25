// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Burbuja.Realtime.Abstractions;
namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private void ProcessBombActions(Burbuja.Realtime.Abstractions.ISessionContext context,BlastPlayer p,InputState input,
        bool aPress,bool bPress,bool bRelease,float dt)
    {
        if(bPress && p.CarryingBombId is not null && p.CarryFromPrimary)
        {
            ThrowBomb(context,p,input);
            p.PrimaryBombCandidateId=null;p.PrimaryHoldTimer=0;
            return;
        }
        if(aPress)
        {
            p.PrimaryHoldTimer=0;p.PrimaryBombCandidateId=null;
            if(p.CarryingBombId is not null) PumpBomb(p);
            else if(p.CarryingPlayerId is null)
            {
                if(!input.B&&TryPrimaryKick(p))return;
                var count=_bombs.Count;
                TryPlaceBomb(context,p,input.B);
                if(_bombs.Count>count)
                {
                    p.CarryFromPrimary=false;
                    if(!input.B)p.PrimaryBombCandidateId=_bombs[^1].Id;
                }
            }
        }
        if(!input.A||input.Length>.1f)
        {
            p.PrimaryHoldTimer=0;p.PrimaryBombCandidateId=null;
        }
        else if(p.PrimaryBombCandidateId is { } candidateId)
        {
            p.PrimaryHoldTimer+=dt;
            if(p.PrimaryHoldTimer>=.25f)
            {
                var bomb=_bombs.FirstOrDefault(b=>b.Id==candidateId&&!b.Exploded&&b.OwnerId==p.Id&&b.State==BombState.Resting);
                if(bomb is not null&&p.CarryingBombId is null&&p.CarryingPlayerId is null&&Math.Abs(BombY(bomb)-p.Y)<.6f&&
                    Dist2(bomb.X,bomb.Z,p.X,p.Z)<=1.05f*1.05f)
                {
                    bomb.State=BombState.Carried;p.CarryingBombId=bomb.Id;p.CarryFromPrimary=true;
                    UpdateCarriedBombPosition(bomb);PresentBomb("lift",bomb,p.Id);
                }
                p.PrimaryBombCandidateId=null;
            }
        }
        if(bPress)TryPickUp(context,p);
        else if(bRelease&&!p.CarryFromPrimary&&(p.CarryingBombId is not null||p.CarryingPlayerId is not null))
            ThrowCarried(context,p,input);
    }

    private static void LaunchKickedBomb(BlastBomb bomb,float vx,float vz,string? touchedBy)
    {
        bomb.LandingY=null;
        bomb.State=BombState.Rolling;bomb.LastTouchedById=touchedBy;bomb.ResumeRollingAfterFall=false;bomb.OwnerMayLeave=false;
        bomb.VX=vx;bomb.VZ=vz;bomb.KickHopRemaining=bomb.IsBouncy?0:BlastBomb.KickHopSeconds;
    }

    // A small front bias reads as a foot-level drop. If that bias touches
    // stone, the supported position at the feet remains a valid placement.
    private bool TryBombPlacement(BlastPlayer p,float aimX,float aimZ,float advance,out float x,out float z)
    {
        x=p.X+aimX*(advance+.18f);z=p.Z+aimZ*(advance+.18f);
        if(CanPlaceBombAt(p,x,z))return true;
        x=p.X+aimX*advance;z=p.Z+aimZ*advance;
        return CanPlaceBombAt(p,x,z);
    }

    private bool TryPrimaryKick(BlastPlayer p)
    {
        var bomb=_bombs.Where(b=>!b.Exploded&&b.State==BombState.Resting&&Math.Abs(BombY(b)-p.Y)<.6f&&
            Dist2(b.X,b.Z,p.X,p.Z)<=.9f*.9f&&
            (b.X-p.X)*p.FacingX+(b.Z-p.Z)*p.FacingZ>=-.05f)
            .OrderBy(b=>Dist2(b.X,b.Z,p.X,p.Z)).FirstOrDefault();
        if(bomb is null)return false;
        // A fresh button edge is a deliberate new strike, including a wall stop.
        _presentedKickContacts.Remove((p.Id,bomb.Id));
        BeginKick(p,bomb,p.FacingX,p.FacingZ);return true;
    }

    private bool CanPlaceBombAt(BlastPlayer player,float x,float z)
    {
        var cell=_stage.AtHeight((int)MathF.Floor(x),(int)MathF.Floor(z),player.Y);
        if (!cell.IsPassable || !BombTerrainClear(x,z,(int)MathF.Floor(player.X),(int)MathF.Floor(player.Z),allowDescent:true,referenceY:player.Y)) return false;
        var y=_stage.SurfaceAt(x,z,player.Y);
        if (Math.Abs(y-player.Y)>.6f) return false;
        foreach(var other in _players.Values)
            if(other.Id!=player.Id&&other.Alive&&!other.Ghost&&other.CarriedById is null&&!other.Airborne&&Math.Abs(other.Y-y)<.6f&&
                Dist2(x,z,other.X,other.Z)<(PlayerRadius+BlastBomb.Radius)*(PlayerRadius+BlastBomb.Radius)) return false;
        foreach(var bomb in _bombs)
            if(!bomb.Exploded&&bomb.State!=BombState.Carried&&Math.Abs(BombY(bomb)-y)<.6f&&
                Dist2(x,z,bomb.X,bomb.Z)<4*BlastBomb.Radius*BlastBomb.Radius) return false;
        return true;
    }

    private bool BombTerrainClear(float x,float z,int originX,int originZ,bool allowDescent=false,BlastBomb? movingBomb=null,float referenceY=float.PositiveInfinity)
    {
        if(movingBomb is not null)referenceY=BombY(movingBomb);
        if(x<BlastBomb.Radius||z<BlastBomb.Radius||x>_stage.Width-BlastBomb.Radius||z>_stage.Height-BlastBomb.Radius) return false;
        for(var cx=(int)MathF.Floor(x-BlastBomb.Radius);cx<=(int)MathF.Floor(x+BlastBomb.Radius);cx++)
            for(var cz=(int)MathF.Floor(z-BlastBomb.Radius);cz<=(int)MathF.Floor(z+BlastBomb.Radius);cz++)
            {
                var dx=x-Math.Clamp(x,cx,cx+1f);var dz=z-Math.Clamp(z,cz,cz+1f);
                if(dx*dx+dz*dz>=BlastBomb.Radius*BlastBomb.Radius) continue;
                var cell=_stage.AtHeight(cx,cz,referenceY);
                if(cell.Kind==CellKind.Pillar||_softBlocks.IsAlive(cx,cz)||
                    (cell.IsPassable&&!CellsConnected(originX,originZ,cx,cz,referenceY)&&
                     !(allowDescent&&BombCellsDescend(originX,originZ,cx,cz,referenceY))&&
                     !(movingBomb is not null&&BombEscapesCell(movingBomb,x,z,cx,cz)))) return false;
            }
        return true;
    }

    // ─── bomb placement ───────────────────────────────────────────────────────

    private void TryPlaceBomb(ISessionContext context, BlastPlayer p, bool hold = false)
    {
        if (p.ActiveBombs >= p.BombCapacity)
        {Present("bomb-blocked",playerId:p.Id,x:p.X,y:p.Y,z:p.Z,reason:"capacity");return;}

        float x=p.X,z=p.Z;
        if(!hold&&!TryBombPlacement(p,p.FacingX,p.FacingZ,0,out x,out z))
        {Present("bomb-blocked",playerId:p.Id,x:p.X,y:p.Y,z:p.Z,reason:"space");return;}
        var cx = (int)MathF.Floor(x);
        var cz = (int)MathF.Floor(z);

        var bomb = new BlastBomb
        {
            Id = NextBombId(),
            OwnerId = p.Id,
            OwnerMayLeave = !hold,
            X = x, Z = z,
            CellX = cx, CellZ = cz, Tier = (int)MathF.Round(_stage.SurfaceAt(x, z,p.Y)), LandingY = _stage.SurfaceAt(x,z,p.Y),
            FireLevel = p.FireLevel,
            Mode = p.BombType,
            FuseTimer = p.BombType=="bounce"?3.5f:BlastBomb.FuseSeconds,
        };
        _bombs.Add(bomb);
        if(p.IsBot&&BotEnemyInRange(bomb.X,BombY(bomb),bomb.Z,bomb.BlastRadius,p.Id,bomb.IsRed))_aiLastThreat[p.Id]=_roundTimer;
        p.ActiveBombs++;
        if (hold) { bomb.State = BombState.Carried; p.CarryingBombId = bomb.Id; UpdateCarriedBombPosition(bomb); }
        PresentBomb(hold ? "bomb-carry" : "bomb-drop", bomb, p.Id);
    }

    // ─── pick up / throw ──────────────────────────────────────────────────────

    private void TryPickUp(ISessionContext context, BlastPlayer p)
    {
        if (p.CarryingBombId is not null || p.CarryingPlayerId is not null) return;

        // Try a bomb first.
        var bomb = _bombs.Where(b => b.State == BombState.Resting && Math.Abs(BombY(b) - p.Y) < .6f)
            .OrderBy(b => Dist2(b.X, b.Z, p.X, p.Z))
            .FirstOrDefault(b => Dist2(b.X, b.Z, p.X, p.Z) <= (PlayerRadius + BlastBomb.Radius + 0.3f) * (PlayerRadius + BlastBomb.Radius + 0.3f));
        if (bomb is not null)
        {
            bomb.State = BombState.Carried;
            p.CarryingBombId = bomb.Id;
            PresentBomb("lift", bomb, p.Id);
            return;
        }

        // Try a stunned player (ghosts cannot pick up players).
        if (!p.Ghost)
        {
            var victim = _players.Values
                .Where(v => v.Id != p.Id && v.Alive && !v.Ghost && v.StunTimer > 0 && v.CarriedById is null && !v.Airborne && Math.Abs(v.Y - p.Y) < .6f)
                .OrderBy(v => Dist2(v.X, v.Z, p.X, p.Z))
                .FirstOrDefault(v => Dist2(v.X, v.Z, p.X, p.Z) <= (PlayerRadius * 2 + 0.3f) * (PlayerRadius * 2 + 0.3f));
            if (victim is not null)
            {
                BeginHold(p,victim);
            }
        }
    }

    private void TryPickUpBombOnly(BlastPlayer p)
    {
        if (p.Ghost || p.CarryingBombId is not null) return;
        var bomb = _bombs.Where(b => b.State == BombState.Resting && Math.Abs(BombY(b) - p.Y) < .6f)
            .OrderBy(b => Dist2(b.X, b.Z, p.X, p.Z))
            .FirstOrDefault(b => Dist2(b.X, b.Z, p.X, p.Z) <= (PlayerRadius + BlastBomb.Radius + 0.3f) * (PlayerRadius + BlastBomb.Radius + 0.3f));
        if (bomb is not null)
        {
            bomb.State = BombState.Carried;
            p.CarryingBombId = bomb.Id;
            PresentBomb("lift", bomb, p.Id);
        }
    }

    private void ThrowCarried(ISessionContext context, BlastPlayer p, InputState input)
    {
        if (p.CarryingBombId is not null)
        {
            ThrowBomb(context, p, input);
        }
        else if (p.CarryingPlayerId is not null)
        {
            ThrowPlayer(context, p, input);
        }
    }

    private void ThrowBomb(ISessionContext context, BlastPlayer p, InputState input)
    {
        var bomb = _bombs.FirstOrDefault(b => b.Id == p.CarryingBombId);
        if (bomb is null) { p.CarryingBombId = null; ClearThrowAim(p); return; }

        // A released stick still throws decisively clear of the carrier. Pumps
        // project the larger blast farther away, rewarding the setup with space.
        var velocity = p.AimSet ? BombThrowRules.AimedVelocity(p.AimX,p.AimZ,p.AimRange,bomb.Pumps) :
            BombThrowRules.Velocity(p.FacingX,p.FacingZ,input.Length,bomb.Pumps);
        var speed = MathF.Sqrt(velocity.X*velocity.X+velocity.Z*velocity.Z);

        bomb.State = BombState.Flying;
        bomb.RequestedPumps=0; bomb.ChargeRemaining=0; bomb.Bounces=0;
        bomb.LandingY=null;
        bomb.LastTouchedById=p.Id;
        bomb.X = p.X;
        bomb.Z = p.Z;
        bomb.FlyVX = velocity.X;
        bomb.FlyVZ = velocity.Z;
        bomb.FlyVY = velocity.Y;
        bomb.FlyY = p.Y + BombThrowRules.LaunchHeight; // launch slightly above tier
        bomb.Tier = p.Tier;

        p.CarryingBombId = null;
        ClearThrowAim(p);
        p.CarryFromPrimary = false;
        PresentBomb("throw", bomb, p.Id, speed);
    }

    private void ThrowPlayer(ISessionContext context, BlastPlayer p, InputState input)
    {
        if (!_players.TryGetValue(p.CarryingPlayerId!, out var victim)) { p.CarryingPlayerId = null; return; }

        var strength=Math.Clamp(input.Length,0.2f,1f);
        victim.CarriedById=null;victim.HoldRemaining=0;victim.Airborne=true;
        victim.X=p.X;victim.Z=p.Z;victim.SurfaceY=p.Y+PlayerCarryHeight;
        victim.AirVX=p.FacingX*(2+strength*3);victim.AirVZ=p.FacingZ*(2+strength*3);victim.AirVY=3.8f;
        victim.StunTimer=.65f;victim.RecoveryTotal=.65f;victim.KnockdownReason="thrown";
        victim.ImpactProtectionTimer=ImpactProtectionSeconds;p.CarryingPlayerId=null;
        Present("player-throw",victim.Id,p.Id,p.X,p.Y+PlayerCarryHeight,p.Z,strength,reason:"player");
    }

    // ─── pumping ──────────────────────────────────────────────────────────────

    private void PumpBomb(BlastPlayer p)
    {
        var bomb = _bombs.FirstOrDefault(b => b.Id == p.CarryingBombId);
        if (bomb is null) return;
        if (bomb.Pumps + bomb.RequestedPumps >= 3) return;
        bomb.RequestedPumps++;
        if(bomb.ChargeRemaining<=0)
            bomb.ChargeRemaining=bomb.ChargeDuration=p.HasFocus?PowerUpRules.FocusPumpSeconds:PowerUpRules.NormalPumpSeconds;
    }

    // ─── remote detonation ────────────────────────────────────────────────────

    private void RemoteDetonate(ISessionContext context, BlastPlayer p)
    {
        var mine = _bombs.Where(b => b.OwnerId == p.Id && b.IsRemote && !b.Exploded && b.State != BombState.Carried).ToList();
        if (mine.Count == 0) return;
        if(_tutorialStarted && p.Slot==0 && mine.Count>0)_tutorialSignals.Add("remote-command");
        // Detonate the oldest remote bomb.
        ExplodeBomb(context, mine[0]);
    }
}

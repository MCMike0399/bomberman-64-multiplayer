// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Burbuja.Realtime.Abstractions;

namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    // Placement keeps strict connected surfaces. Only an already moving kick may
    // leave a higher edge, including diagonal routes whose two edges both descend.
    private bool BombCellsDescend(int ax,int az,int bx,int bz)
    {
        if(ax==bx||az==bz)return _stage.CanWalk(ax,az,bx,bz);
        return (_stage.CanWalk(ax,az,bx,az)&&_stage.CanWalk(bx,az,bx,bz))||
            (_stage.CanWalk(ax,az,ax,bz)&&_stage.CanWalk(ax,bz,bx,bz));
    }

    // A shallow diagonal ramp entry may already lower the center before its
    // trailing circle clears the upper floor. Permit only an overlap that existed
    // at the previous position and is shrinking; new uphill contact still blocks.
    private static bool BombEscapesCell(BlastBomb bomb,float x,float z,int cx,int cz)
    {
        static float Distance(float px,float pz,int ix,int iz)
        {float dx=px-Math.Clamp(px,ix,ix+1f),dz=pz-Math.Clamp(pz,iz,iz+1f);return dx*dx+dz*dz;}
        float previous=Distance(bomb.X,bomb.Z,cx,cz);
        return previous<BlastBomb.Radius*BlastBomb.Radius&&Distance(x,z,cx,cz)>previous+.000001f;
    }

    private float BombLandingHeight(float x,float z) =>
        _stage.At((int)MathF.Floor(x),(int)MathF.Floor(z)).Kind switch
        {
            CellKind.Water => _waterLevel,
            CellKind.Void => -1,
            _ => _stage.SurfaceAt(x,z),
        };

    private void TickFallingKickedBomb(ISessionContext context,BlastBomb b,float dt)
    {
        var speed=MathF.Sqrt(b.FlyVX*b.FlyVX+b.FlyVZ*b.FlyVZ);
        int steps=Math.Max(1,(int)MathF.Ceiling(speed*dt/.1f));
        float stepTime=dt/steps;
        for(int step=0;step<steps&&!b.Exploded;step++)
        {
            // Landing can occur partway through a tick; spend the remaining
            // substeps rolling rather than losing a tick of horizontal momentum.
            if(b.State==BombState.Rolling){StepRollingBomb(context,b,stepTime);continue;}
            if(b.State!=BombState.Flying)return;
            float nx=b.X+b.FlyVX*stepTime,nz=b.Z+b.FlyVZ*stepTime;
            if(!FallingKickTerrainClear(b,nx,nz))
            {
                // A wall stops horizontal travel, but an unsupported bomb still
                // falls to the surface below instead of hanging on the wall.
                if(b.IsBouncy)
                {
                    bool hitX=!FallingKickTerrainClear(b,nx,b.Z),hitZ=!FallingKickTerrainClear(b,b.X,nz);
                    if(!hitX&&!hitZ)hitX=hitZ=true;
                    if(hitX)b.FlyVX=-b.FlyVX;if(hitZ)b.FlyVZ=-b.FlyVZ;
                    b.Bounces++;PresentBomb("bounce",b,b.LastTouchedById);
                }
                else b.FlyVX=b.FlyVZ=0;
                nx=b.X;nz=b.Z;
            }
            foreach(var p in _players.Values)
            {
                if(!CanReceiveBombImpact(p)||
                    p.Airborne||Math.Abs(p.Y-b.FlyY)>.6f)continue;
                if((p.X-b.X)*b.FlyVX+(p.Z-b.Z)*b.FlyVZ<=0)continue;
                if(Dist2(nx,nz,p.X,p.Z)>=(PlayerRadius+BlastBomb.Radius)*(PlayerRadius+BlastBomb.Radius))continue;
                KnockDown(p,1.5f,"rolling-bomb",b.Id,b.LastTouchedById??b.OwnerId);
                var impactSpeed=MathF.Sqrt(b.FlyVX*b.FlyVX+b.FlyVZ*b.FlyVZ);
                ApplyPlayerImpulse(p,b.FlyVX/impactSpeed*BlastBomb.ImpactHorizontalSpeed,
                    b.FlyVZ/impactSpeed*BlastBomb.ImpactHorizontalSpeed,BlastBomb.ImpactUpSpeed);
                b.FlyVX=b.FlyVZ=0;break;
            }
            foreach(var other in _bombs)
            {
                if(other==b||other.Exploded||other.State!=BombState.Resting||Math.Abs(BombY(other)-b.FlyY)>.6f)continue;
                if(Dist2(nx,nz,other.X,other.Z)>=(BlastBomb.Radius*2+.05f)*(BlastBomb.Radius*2+.05f))continue;
                if(b.FlyVX*b.FlyVX+b.FlyVZ*b.FlyVZ>0)LaunchKickedBomb(other,b.FlyVX,b.FlyVZ,b.LastTouchedById);
                nx=b.X;nz=b.Z;b.FlyVX=b.FlyVZ=0;break;
            }
            b.X=nx;b.Z=nz;b.CellX=(int)MathF.Floor(nx);b.CellZ=(int)MathF.Floor(nz);
            b.FlyVY-=BlastBomb.Gravity*b.GravityScale*stepTime;b.FlyY+=b.FlyVY*stepTime;
            var landingY=BombLandingHeight(nx,nz);
            if(b.FlyY>landingY)continue;
            b.FlyY=landingY;
            if(_stage.At(b.CellX,b.CellZ).IsElimination)
            {
                b.Exploded=true;DecrementOwnerBombs(b.OwnerId);return;
            }
            b.VX=b.FlyVX;b.VZ=b.FlyVZ;b.Tier=(int)MathF.Round(landingY);
            b.FlyVX=b.FlyVZ=b.FlyVY=0;
            b.ResumeRollingAfterFall=false;
            b.State=b.VX*b.VX+b.VZ*b.VZ>0?BombState.Rolling:BombState.Resting;
        }
    }

    private bool FallingKickTerrainClear(BlastBomb b,float x,float z)
    {
        if(x<BlastBomb.Radius||z<BlastBomb.Radius||x>_stage.Width-BlastBomb.Radius||z>_stage.Height-BlastBomb.Radius)return false;
        static float Distance(float x,float z,int cx,int cz)
        {float dx=x-Math.Clamp(x,cx,cx+1f),dz=z-Math.Clamp(z,cz,cz+1f);return dx*dx+dz*dz;}
        for(int cx=(int)MathF.Floor(x-BlastBomb.Radius);cx<=(int)MathF.Floor(x+BlastBomb.Radius);cx++)
            for(int cz=(int)MathF.Floor(z-BlastBomb.Radius);cz<=(int)MathF.Floor(z+BlastBomb.Radius);cz++)
            {
                float distance=Distance(x,z,cx,cz);
                if(distance>=BlastBomb.Radius*BlastBomb.Radius)continue;
                var cell=_stage.At(cx,cz);
                if(cell.Kind==CellKind.Pillar||_softBlocks.IsAlive(cx,cz))return false;
                if(!cell.IsPassable||_stage.SurfaceAt(Math.Clamp(x,cx+.001f,cx+.999f),Math.Clamp(z,cz+.001f,cz+.999f))<=b.FlyY+.01f)continue;
                // The trailing circle can still overlap the ledge it just left.
                // Permit that existing overlap only while moving out of it.
                float previous=Distance(b.X,b.Z,cx,cz);
                if(previous>=BlastBomb.Radius*BlastBomb.Radius||distance<previous-.000001f)return false;
            }
        return true;
    }

    // ─── bomb simulation ──────────────────────────────────────────────────────

    private void TickBombs(ISessionContext context, float dt)
    {
        for (var i = _bombs.Count - 1; i >= 0; i--)
        {
            var b = _bombs[i];
            if (b.Exploded) { _bombs.RemoveAt(i); continue; }
            if(b.State!=BombState.Carried){b.RequestedPumps=0;b.ChargeRemaining=0;}

            if(b.OwnerMayLeave&&(b.State!=BombState.Resting||b.OwnerId is null||
                !_players.TryGetValue(b.OwnerId,out var placingOwner)||!placingOwner.Alive||placingOwner.Ghost||
                Math.Abs(placingOwner.Y-BombY(b))>.6f||Dist2(b.X,b.Z,placingOwner.X,placingOwner.Z)>.8f*.8f))
                b.OwnerMayLeave=false;

            // A remote cannot wait for a command its owner can no longer send. Keep
            // active owners' deliberate remote semantics, but resume a real fuse for
            // abandoned bombs rather than leaving an immortal obstacle in a corner.
            if(b.IsRemote && (b.OwnerId is null || !_players.TryGetValue(b.OwnerId,out var remoteOwner) ||
                !remoteOwner.Alive || remoteOwner.Ghost || !remoteOwner.HasRemote || !context.IsConnected(remoteOwner.Id)))
            {
                b.IsRemote=false;
                PresentBomb("remote-fuse",b,b.OwnerId,b.FuseTimer);
            }

            // Fuse countdown (not while carried or commandable remote).
            if (b.State != BombState.Carried && !b.IsRemote)
            {
                b.FuseTimer -= dt;
                if (b.FuseTimer <= 0)
                {
                    ExplodeBomb(context, b);
                    continue;
                }
            }

            switch (b.State)
            {
                case BombState.Resting:
                    if(b.LandingY is { } support&&support>StaticBombLandingHeight(b.X,b.Z)+.001f)
                    {
                        b.State=BombState.Flying;b.FlyY=support;b.FlyVX=0;b.FlyVZ=0;b.FlyVY=0;b.LandingY=null;
                        TickFlyingBomb(context,b,dt);
                    }
                    break;
                case BombState.Rolling: TickRollingBomb(context, b, dt*b.MotionRate); break;
                case BombState.Flying: TickFlyingBomb(context, b, dt*b.MotionRate); break;
                case BombState.Carried:
                    UpdateCarriedBombPosition(b);
                    if(b.RequestedPumps>0)
                    {
                        b.ChargeRemaining-=dt;
                        if(b.ChargeRemaining<=.00001f)
                        {
                            b.Pumps++;b.RequestedPumps--;
                            var carrier=_players.Values.FirstOrDefault(p=>p.CarryingBombId==b.Id);
                            if(carrier!=null){carrier.PumpSerial++;PresentBomb("pump",b,carrier.Id,b.Pumps);}
                            b.ChargeRemaining=b.RequestedPumps>0 ? b.ChargeDuration=carrier?.HasFocus==true?PowerUpRules.FocusPumpSeconds:PowerUpRules.NormalPumpSeconds : 0;
                        }
                    }
                    break;
            }
        }
    }

    private float BombY(BlastBomb bomb) => bomb.State == BombState.Flying ? bomb.FlyY :
        bomb.State == BombState.Carried ? (_players.Values.FirstOrDefault(p => p.CarryingBombId == bomb.Id)?.Y ?? bomb.Tier) + BombThrowRules.LaunchHeight :
        (bomb.State==BombState.Resting?bomb.LandingY??_stage.SurfaceAt(bomb.X,bomb.Z):_stage.SurfaceAt(bomb.X,bomb.Z)) + (bomb.State==BombState.Rolling && bomb.KickHopRemaining>0
            ? BlastBomb.KickHopHeight*MathF.Sin(MathF.PI*bomb.KickHopRemaining/BlastBomb.KickHopSeconds) : 0);

    private void TickRollingBomb(ISessionContext context, BlastBomb b, float dt)
    {
        b.KickHopRemaining=Math.Max(0,b.KickHopRemaining-dt);
        // Sweep the whole circle in short steps so faster kicks cannot tunnel through
        // terrain, players, or other bombs between authoritative ticks.
        var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(b.VX*b.VX+b.VZ*b.VZ)*dt/.1f));
        for (var step=0;step<steps && !b.Exploded;step++)
        {
            if(b.State==BombState.Rolling)StepRollingBomb(context,b,dt/steps);
            else if(b.State==BombState.Flying&&b.ResumeRollingAfterFall)TickFallingKickedBomb(context,b,dt/steps);
            else break;
        }
    }

    private void StepRollingBomb(ISessionContext context, BlastBomb b, float dt)
    {
        var nx = b.X + b.VX * dt;
        var nz = b.Z + b.VZ * dt;
        var cx = (int)nx;
        var cz = (int)nz;

        var cell = _stage.At(cx, cz);
        if (!BombTerrainClear(nx, nz, (int)MathF.Floor(b.X), (int)MathF.Floor(b.Z),allowDescent:true,movingBomb:b))
        {
            if(b.IsBouncy)
            {
                bool xClear=BombTerrainClear(nx,b.Z,(int)MathF.Floor(b.X),(int)MathF.Floor(b.Z),true,b);
                bool zClear=BombTerrainClear(b.X,nz,(int)MathF.Floor(b.X),(int)MathF.Floor(b.Z),true,b);
                if(!xClear)b.VX=-b.VX;
                if(!zClear)b.VZ=-b.VZ;
                if(xClear&&zClear){b.VX=-b.VX;b.VZ=-b.VZ;}
                b.Bounces++;PresentBomb("bounce",b,b.LastTouchedById);
            }
            else {b.State=BombState.Resting;b.VX=b.VZ=0;}
            return;
        }

        var previousY=_stage.SurfaceAt(b.X,b.Z);
        if(BombLandingHeight(nx,nz)<previousY-.05f &&
            !CellsConnected((int)MathF.Floor(b.X),(int)MathF.Floor(b.Z),cx,cz))
        {
            b.State=BombState.Flying;b.ResumeRollingAfterFall=true;
            b.FlyVX=b.VX;b.FlyVZ=b.VZ;b.FlyVY=0;b.FlyY=previousY;
            b.KickHopRemaining=0;
            TickFallingKickedBomb(context,b,dt);
            return;
        }

        // Check collision with a player (stun) or another resting bomb (billiard).
        foreach (var p in _players.Values)
        {
            if (!CanReceiveBombImpact(p) || p.Airborne || Math.Abs(p.Y - _stage.SurfaceAt(nx, nz)) > .6f) continue;
            // A player following an outgoing kick must not collide with its trailing edge.
            if((p.X-b.X)*b.VX+(p.Z-b.Z)*b.VZ<=0)continue;
            if (Dist2(nx, nz, p.X, p.Z) < (PlayerRadius + BlastBomb.Radius) * (PlayerRadius + BlastBomb.Radius))
            {
                KnockDown(p,1.5f,"rolling-bomb",b.Id,b.LastTouchedById??b.OwnerId);
                var speed=MathF.Sqrt(b.VX*b.VX+b.VZ*b.VZ);
                ApplyPlayerImpulse(p,b.VX/speed*BlastBomb.ImpactHorizontalSpeed,
                    b.VZ/speed*BlastBomb.ImpactHorizontalSpeed,BlastBomb.ImpactUpSpeed);
                b.X=nx;b.Z=nz;b.CellX=cx;b.CellZ=cz;
                b.State = BombState.Resting;
                b.VX = 0; b.VZ = 0;
                return;
            }
        }

        foreach (var other in _bombs)
        {
            if (other == b || other.State != BombState.Resting || Math.Abs(BombY(other) - _stage.SurfaceAt(nx, nz)) > .6f) continue;
            if (Dist2(nx, nz, other.X, other.Z) < (BlastBomb.Radius * 2 + 0.05f) * (BlastBomb.Radius * 2 + 0.05f))
            {
                // Transfer the shot, including its brief launch hop, without resetting either fuse.
                LaunchKickedBomb(other,b.VX,b.VZ,b.LastTouchedById);
                b.State = BombState.Resting;
                b.VX = 0; b.VZ = 0;
                return;
            }
        }

        b.X = nx;
        b.Z = nz;
        b.CellX = cx;
        b.CellZ = cz;
        b.Tier = (int)MathF.Round(_stage.SurfaceAt(b.X, b.Z));

        // Elimination: rolled off the stage.
        if (cell.IsElimination)
        {
            b.Exploded = true;
            DecrementOwnerBombs(b.OwnerId);
        }
    }

    private void TickFlyingBomb(ISessionContext context, BlastBomb b, float dt)
    {
        if(b.ResumeRollingAfterFall){TickFallingKickedBomb(context,b,dt);return;}
        var flight=BombThrowRules.AdvanceStatic(new(b.X,b.FlyY,b.Z),new(b.FlyVX,b.FlyVY,b.FlyVZ),dt,
            BombLandingHeight,(x,z)=>_stage.At(x,z).Kind,(x,z)=>_softBlocks.IsAlive(x,z),b.IsBouncy,b.GravityScale);
        b.X=flight.Position.X;b.Z=flight.Position.Z;b.FlyY=flight.Position.Y;b.FlyVY=flight.Velocity.Y;b.FlyVX=flight.Velocity.X;b.FlyVZ=flight.Velocity.Z;

        var landingY = StaticBombLandingHeight(b.X, b.Z);

        // A head hit is a bomb coming down onto a player's head; on the way up it clears the thrower.
        foreach (var p in _players.Values)
        {
            if (!CanReceiveBombImpact(p)) continue;
            if (b.FlyVY > 0 || b.FlyY > p.Y + HeadHeight || b.FlyY < p.Y) continue;
            if (Dist2(b.X, b.Z, p.X, p.Z) < (PlayerRadius + BlastBomb.Radius) * (PlayerRadius + BlastBomb.Radius))
            {
                // Head hit: stun the player, bomb bounces (continues).
                KnockDown(p,2.0f,"falling-bomb",b.Id,b.LastTouchedById??b.OwnerId);
                // A head impact dissipates energy instead of relaunching a large bomb.
                b.FlyVY=Math.Min(MathF.Abs(b.FlyVY)*.3f,b.IsBouncy?3.2f:2.0f)/b.GravityScale;
                float retention=b.IsBouncy?.7f:.5f/b.GravityScale;
                b.FlyVX*=retention;b.FlyVZ*=retention;
            }
        }

        // Landed.
        if (b.FlyY <= landingY)
        {
            b.FlyY = landingY;
            var cell = _stage.At((int)b.X, (int)b.Z);
            if (cell.IsElimination || b.X < 0 || b.X >= _stage.Width || b.Z < 0 || b.Z >= _stage.Height)
            {
                b.Exploded = true;
                DecrementOwnerBombs(b.OwnerId);
            }
            else if(b.IsBouncy && b.Pumps>0 && BombThrowRules.CanBounce(b.Bounces,b.FlyVY))
            {
                b.Bounces++; b.FlyVY=BombThrowRules.BounceVelocity(b.FlyVY);
                PresentBomb("bounce",b,b.LastTouchedById);
            }
            else
            {
                b.State = BombState.Resting;
                b.LandingY=landingY;
                b.CellX = (int)b.X;
                b.CellZ = (int)b.Z;
                b.Tier = (int)MathF.Round(landingY); // update tier to where it landed
                b.FlyVX = 0; b.FlyVZ = 0; b.FlyVY = 0;
            }
        }
    }

    private void UpdateCarriedBombPosition(BlastBomb b)
    {
        b.LandingY=null;
        // Whoever holds it, which need not be the player who placed it.
        var carrier = _players.Values.FirstOrDefault(p => p.CarryingBombId == b.Id);
        if (carrier is null) return;
        b.X = carrier.X + carrier.FacingX * (PlayerRadius + BlastBomb.Radius + 0.05f);
        b.Z = carrier.Z + carrier.FacingZ * (PlayerRadius + BlastBomb.Radius + 0.05f);
        b.Tier = carrier.Tier;
        b.CellX = (int)b.X;
        b.CellZ = (int)b.Z;
    }
}

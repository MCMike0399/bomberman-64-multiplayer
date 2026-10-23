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
}

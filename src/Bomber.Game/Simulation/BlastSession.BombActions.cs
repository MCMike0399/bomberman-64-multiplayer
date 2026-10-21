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
                var count=_bombs.Count;
                TryPlaceBomb(context,p,input.B);
                if(_bombs.Count>count)
                {
                    p.CarryFromPrimary=false;
                    if(!input.B)p.PrimaryBombCandidateId=_bombs[^1].Id;
                }
            }
        }
        if(!input.A)
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
        bomb.State=BombState.Rolling;bomb.LastTouchedById=touchedBy;bomb.ResumeRollingAfterFall=false;
        bomb.VX=vx;bomb.VZ=vz;bomb.KickHopRemaining=BlastBomb.KickHopSeconds;
    }

    private bool CanPlaceBombAt(BlastPlayer player,float x,float z)
    {
        var cell=_stage.At((int)MathF.Floor(x),(int)MathF.Floor(z));
        if (!cell.IsPassable || !BombTerrainClear(x,z,(int)MathF.Floor(player.X),(int)MathF.Floor(player.Z))) return false;
        var y=_stage.SurfaceAt(x,z);
        if (Math.Abs(y-player.Y)>.6f) return false;
        foreach(var other in _players.Values)
            if(other.Alive&&!other.Ghost&&other.CarriedById is null&&!other.Airborne&&Math.Abs(other.Y-y)<.6f&&
                Dist2(x,z,other.X,other.Z)<(PlayerRadius+BlastBomb.Radius)*(PlayerRadius+BlastBomb.Radius)) return false;
        foreach(var bomb in _bombs)
            if(!bomb.Exploded&&bomb.State!=BombState.Carried&&Math.Abs(BombY(bomb)-y)<.6f&&
                Dist2(x,z,bomb.X,bomb.Z)<4*BlastBomb.Radius*BlastBomb.Radius) return false;
        return true;
    }

    private bool BombTerrainClear(float x,float z,int originX,int originZ,bool allowDescent=false)
    {
        if(x<BlastBomb.Radius||z<BlastBomb.Radius||x>_stage.Width-BlastBomb.Radius||z>_stage.Height-BlastBomb.Radius) return false;
        for(var cx=(int)MathF.Floor(x-BlastBomb.Radius);cx<=(int)MathF.Floor(x+BlastBomb.Radius);cx++)
            for(var cz=(int)MathF.Floor(z-BlastBomb.Radius);cz<=(int)MathF.Floor(z+BlastBomb.Radius);cz++)
            {
                var dx=x-Math.Clamp(x,cx,cx+1f);var dz=z-Math.Clamp(z,cz,cz+1f);
                if(dx*dx+dz*dz>=BlastBomb.Radius*BlastBomb.Radius) continue;
                var cell=_stage.At(cx,cz);
                if(cell.Kind==CellKind.Pillar||_softBlocks.IsAlive(cx,cz)||
                    (cell.IsPassable&&!CellsConnected(originX,originZ,cx,cz)&&
                     !(allowDescent&&BombCellsDescend(originX,originZ,cx,cz)))) return false;
            }
        return true;
    }
}

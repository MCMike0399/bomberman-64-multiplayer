// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using System.Numerics;
namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private Vector2 BlizzardWind=>_stage.Id==BlastStages.BlizzardBattle?BlizzardRules.Wind(_weatherTime,_weatherSeed,_roundNum):Vector2.Zero;

    private void DriftBlizzardPlayers(float dt)
    {
        var wind=BlizzardWind;
        if(wind==Vector2.Zero)return;
        foreach(var p in _players.Values)
        {
            if(!p.Alive||p.Ghost||p.CarriedById!=null||p.Airborne||p.PendingRoundStart)continue;
            ApplyMoveDelta(p,wind.X*BlizzardRules.PlayerWindScale*dt,wind.Y*BlizzardRules.PlayerWindScale*dt,dt);
        }
    }

    private void DriftBlizzardBomb(BlastBomb bomb,float dt)
    {
        var wind=BlizzardWind;
        if(wind==Vector2.Zero||bomb.State is BombState.Carried or BombState.Flying)return;
        bool Clear(float x,float z)=>BombTerrainClear(x,z,(int)bomb.X,(int)bomb.Z,movingBomb:bomb)&&
            !_players.Values.Any(p=>p.Alive&&!p.Ghost&&p.CarriedById==null&&Math.Abs(p.Y-BombY(bomb))<.6f&&
                !(bomb.OwnerMayLeave&&p.Id==bomb.OwnerId)&&Dist2(x,z,p.X,p.Z)<MathF.Pow(PlayerRadius+BlastBomb.Radius,2))&&
            !_bombs.Any(b=>b!=bomb&&!b.Exploded&&b.State!=BombState.Carried&&Math.Abs(BombY(b)-BombY(bomb))<.6f&&
                Dist2(x,z,b.X,b.Z)<MathF.Pow(BlastBomb.Radius*2,2));
        float nx=bomb.X+wind.X*dt,nz=bomb.Z+wind.Y*dt;
        if(Clear(nx,bomb.Z))bomb.X=nx;
        if(Clear(bomb.X,nz))bomb.Z=nz;
        bomb.CellX=(int)MathF.Floor(bomb.X);bomb.CellZ=(int)MathF.Floor(bomb.Z);
    }
}

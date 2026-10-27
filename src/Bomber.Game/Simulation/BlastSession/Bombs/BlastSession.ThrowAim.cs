using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.State;

namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private float StaticBombLandingHeight(float x,float z,float y=float.PositiveInfinity) => BombThrowRules.LandingHeight(x,z,
        (x,z)=>BombLandingHeight(x,z,y),(cx,cz)=>_stage.AtHeight(cx,cz,y).Kind,(cx,cz)=>_softBlocks.IsAlive(cx,cz),(cx,cz)=>_stage.AtHeight(cx,cz,y).ObstacleHeight??BombThrowRules.PillarHeight,_stage.SoftBlockHeight);

    private static void ClearThrowAim(BlastPlayer player)
    {
        player.Aiming=false;player.AimSet=false;
        player.AimX=0;player.AimZ=1;player.AimRange=BombThrowRules.DefaultAimRange;
        ClearAimRamp(player);
    }

    private static void ClearAimRamp(BlastPlayer player)
    {
        player.AimCursorXAge=player.AimCursorZAge=0;
        player.AimCursorXSign=player.AimCursorZSign=0;
    }

    private static float RampAimAxis(float axis,ref float age,ref int previousSign,float dt)
    {
        var sign=Math.Sign(axis);
        if(sign==0){age=0;previousSign=0;return 0;}
        if(sign!=previousSign)age=0;
        previousSign=sign;
        var scale=BombThrowRules.AimFineScale+(1-BombThrowRules.AimFineScale)*Math.Clamp(age/BombThrowRules.AimRampSeconds,0,1);
        age=Math.Min(BombThrowRules.AimRampSeconds,age+dt);
        return axis*scale;
    }

    private void UpdateThrowAim(BlastPlayer player,InputState input,float dt)
    {
        if(player.CarryingBombId is null){ClearThrowAim(player);return;}
        player.Aiming=input.Aim;
        // A pointer target is resolved after this tick's movement (see ProcessInputs).
        if(input.AimTarget is not null){ClearAimRamp(player);return;}
        if(!player.Aiming)
        {
            ClearAimRamp(player);
            if(player.AimSet){player.FacingX=player.AimX;player.FacingZ=player.AimZ;}
            return;
        }
        if(!player.AimSet)
        {
            player.AimX=player.FacingX;player.AimZ=player.FacingZ;
            player.AimRange=BombThrowRules.DefaultAimRange;player.AimSet=true;
        }
        var x=RampAimAxis(input.X,ref player.AimCursorXAge,ref player.AimCursorXSign,dt);
        var z=RampAimAxis(input.Z,ref player.AimCursorZAge,ref player.AimCursorZSign,dt);
        var aim=BombThrowRules.AdjustAim(new(player.AimX,player.AimZ,player.AimRange),x,z,dt);
        if(player.HasFocus && input.Length<.001f)
        {
            float tx=player.X+aim.X*aim.Range,tz=player.Z+aim.Z*aim.Range;
            var target=_players.Values.Where(p=>p.Id!=player.Id&&p.Alive&&!p.Ghost&&
                Dist2(tx,tz,p.X,p.Z)<1.1f*1.1f &&
                !BlastGeometry.Occluded(_stage,_softBlocks,player.X,player.Y+.8f,player.Z,p.X,p.Y+.8f,p.Z))
                .OrderBy(p=>Dist2(tx,tz,p.X,p.Z)).FirstOrDefault();
            if(target!=null)
            {
                float dx=target.X-tx,dz=target.Z-tz,length=MathF.Sqrt(dx*dx+dz*dz);
                float step=Math.Min(length,.7f*dt);
                if(length>.001f)
                {
                    tx+=dx/length*step;tz+=dz/length*step;
                    dx=tx-player.X;dz=tz-player.Z;length=MathF.Sqrt(dx*dx+dz*dz);
                    if(length>.001f)aim=new(dx/length,dz/length,Math.Min(length,BombThrowRules.MaxAimRange));
                }
            }
        }
        player.AimX=aim.X;player.AimZ=aim.Z;player.AimRange=aim.Range;
        player.FacingX=aim.X;player.FacingZ=aim.Z;
    }

    /// <summary>A pointer names the landing point directly. The carrier keeps walking unless the aim
    /// stance is also held; the chosen intent persists once the pointer stops reporting.</summary>
    private void PointThrowAim(BlastPlayer player,System.Numerics.Vector2 target)
    {
        float dx=target.X-player.X,dz=target.Y-player.Z,distance=MathF.Sqrt(dx*dx+dz*dz);
        if(distance>.001f){player.AimX=dx/distance;player.AimZ=dz/distance;}
        else if(!player.AimSet){player.AimX=player.FacingX;player.AimZ=player.FacingZ;}
        // Compensate for the target's height so the bomb lands on the pointed surface.
        float rise=0;
        if(target.X>=0&&target.Y>=0&&target.X<_stage.Width&&target.Y<_stage.Height)
        {
            float apex=player.Y+BombThrowRules.LaunchHeight+BombThrowRules.AimLiftSpeed*BombThrowRules.AimLiftSpeed/(2*BlastBomb.Gravity);
            rise=StaticBombLandingHeight(target.X,target.Y,apex)-player.Y;
        }
        int pumps=_bombs.FirstOrDefault(b=>b.Id==player.CarryingBombId)?.Pumps??0;
        player.AimRange=BombThrowRules.RangeToTarget(distance,rise,pumps);
        player.AimSet=true;ClearAimRamp(player);
        player.FacingX=player.AimX;player.FacingZ=player.AimZ;
    }
}

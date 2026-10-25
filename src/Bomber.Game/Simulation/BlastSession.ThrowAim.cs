namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private float StaticBombLandingHeight(float x,float z,float ceiling=float.PositiveInfinity) => BombThrowRules.LandingHeight(x,z,
        (px,pz)=>BombLandingHeight(px,pz,ceiling),(cx,cz)=>_stage.At(cx,cz).Kind,(cx,cz)=>_softBlocks.IsAlive(cx,cz),(cx,cz)=>_stage.At(cx,cz).ObstacleHeight??BombThrowRules.PillarHeight);

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
}

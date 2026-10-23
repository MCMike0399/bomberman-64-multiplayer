namespace Bomber.Game.Simulation;

public sealed partial class BlastSession
{
    private float StaticBombLandingHeight(float x,float z) => BombThrowRules.LandingHeight(x,z,
        BombLandingHeight,(cx,cz)=>_stage.At(cx,cz).Kind,(cx,cz)=>_softBlocks.IsAlive(cx,cz));

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

    private static void UpdateThrowAim(BlastPlayer player,InputState input,float dt)
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
        player.AimX=aim.X;player.AimZ=aim.Z;player.AimRange=aim.Range;
        player.FacingX=aim.X;player.FacingZ=aim.Z;
    }
}

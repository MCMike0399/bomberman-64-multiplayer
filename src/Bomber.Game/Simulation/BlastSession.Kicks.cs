namespace Bomber.Game.Simulation;

/// <summary>A committed contact strike: plant, launch, then regain running speed.</summary>
public sealed partial class BlastSession
{
    private const float KickWindupSeconds=4f/30f,KickRecoverySeconds=5f/30f;

    private static void ClearKick(BlastPlayer p)
    {p.KickBombId=null;p.KickWindupRemaining=0;p.KickRecoveryRemaining=0;}

    private void BeginKick(BlastPlayer p,BlastBomb bomb,float nx,float nz)
    {
        if(!_presentedKickContacts.Add((p.Id,bomb.Id)))return;
        p.KickBombId=bomb.Id;p.KickWindupRemaining=KickWindupSeconds;
        p.KickDirectionX=nx;p.KickDirectionZ=nz;
        p.FacingX=nx;p.FacingZ=nz;
        p.PrimaryBombCandidateId=null;p.PrimaryHoldTimer=0;
        PresentBomb("kick-windup",bomb,p.Id,KickWindupSeconds);
    }

    // Returns true while the planted phase consumes movement/action intent. The
    // button edges are still consumed by ProcessInputs, so recovery cannot replay them.
    private bool AdvanceKick(BlastPlayer p,float dt)
    {
        if(p.KickBombId is not {} id)return false;
        var bomb=_bombs.FirstOrDefault(b=>b.Id==id&&!b.Exploded&&b.State==BombState.Resting);
        if(bomb is null||Math.Abs(BombY(bomb)-p.Y)>.6f||
            Dist2(bomb.X,bomb.Z,p.X,p.Z)>1.05f*1.05f)
        {ClearKick(p);Present("kick-cancel",playerId:p.Id);return false;}
        p.FacingX=p.KickDirectionX;p.FacingZ=p.KickDirectionZ;
        p.KickWindupRemaining=Math.Max(0,p.KickWindupRemaining-dt);
        if(p.KickWindupRemaining>.00001f)return true;
        LaunchKickedBomb(bomb,p.KickDirectionX*BlastBomb.RollSpeed,p.KickDirectionZ*BlastBomb.RollSpeed,p.Id);
        p.KickBombId=null;p.KickRecoveryRemaining=KickRecoverySeconds;
        PresentBomb("kick",bomb,p.Id,BlastBomb.RollSpeed);
        return true;
    }

    private static float KickMovementScale(BlastPlayer p)=>p.KickRecoveryRemaining<=0?1:
        .2f+.8f*(1-p.KickRecoveryRemaining/KickRecoverySeconds);
}

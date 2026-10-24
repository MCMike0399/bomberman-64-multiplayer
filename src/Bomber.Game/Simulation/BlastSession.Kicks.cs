// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
namespace Bomber.Game.Simulation;

/// <summary>A committed contact strike: plant, launch, then regain running speed.</summary>
public sealed partial class BlastSession
{
    private const float KickWindupSeconds=4f/30f,KickRecoverySeconds=5f/30f;

    private static void ClearKick(BlastPlayer p)
    {p.KickBombId=null;p.KickWindupRemaining=0;p.KickRecoveryRemaining=0;}

    private void BeginKick(BlastPlayer p,BlastBomb bomb,float nx,float nz)
    {
        if(p.Ghost)return;
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

    private void StopKickedBombs(BlastPlayer player)
    {
        foreach(var bomb in _bombs)
        {
            if(bomb.Exploded||bomb.LastTouchedById!=player.Id)continue;
            if(bomb.State==BombState.Rolling)
            {bomb.VX=bomb.VZ=0;bomb.State=BombState.Resting;bomb.KickHopRemaining=0;}
            else if(bomb.State==BombState.Flying&&bomb.ResumeRollingAfterFall)
                bomb.FlyVX=bomb.FlyVZ=0; // gravity continues; never suspend a bomb over a drop
            else continue;
            PresentBomb("kick-stop",bomb,player.Id);
        }
    }

    private static float KickMovementScale(BlastPlayer p)=>p.KickRecoveryRemaining<=0?1:
        .2f+.8f*(1-p.KickRecoveryRemaining/KickRecoverySeconds);

    // ─── kicking ──────────────────────────────────────────────────────────────

    private void CheckKick(BlastPlayer p, InputState input, float startX, float startZ, float dt)
    {
        ForgetSeparatedKickContacts(p);
        if(p.KickBombId is not null||p.KickRecoveryRemaining>0||p.Airborne)return;
        if (input.X * input.X + input.Z * input.Z < 0.1f)
        {_presentedKickContacts.RemoveWhere(c=>c.PlayerId==p.Id);return;}

        foreach (var b in _bombs)
        {
            if (b.State != BombState.Resting || b.OwnerMayLeave&&b.OwnerId==p.Id || Math.Abs(BombY(b) - p.Y) > .6f) continue;
            var dx = b.X - p.X;
            var dz = b.Z - p.Z;
            var dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dx * input.X + dz * input.Z <= .001f) continue; // walking away never kicks a bomb behind the player
            if (dist > PlayerRadius + BlastBomb.Radius + 0.05f)
            {
                // Movement collision can stop up to one tick short of touching the bomb. Test
                // this tick's intended segment, rather than enlarging the proximity radius.
                var stepX = input.X * PlayerSpeed * p.StatusSpeed * dt;
                var stepZ = input.Z * PlayerSpeed * p.StatusSpeed * dt;
                var along = Math.Clamp(((b.X - startX) * stepX + (b.Z - startZ) * stepZ) /
                    (stepX * stepX + stepZ * stepZ), 0, 1);
                var clearance = PlayerRadius + BlastBomb.Radius;
                if (Dist2(b.X, b.Z, startX + along * stepX, startZ + along * stepZ) > clearance * clearance)
                    continue;
            }

            // Preserve the full stick angle; the bomb must clear a running kicker.
            var magnitude = MathF.Sqrt(input.X * input.X + input.Z * input.Z);
            var nx = input.X / magnitude;
            var nz = input.Z / magnitude;

            BeginKick(p,b,nx,nz);
            break; // one kick per tick
        }
    }
}

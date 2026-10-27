// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.
using Burbuja.Realtime.Abstractions;

namespace Bomber.Game.Simulation;

/// <summary>Per-tick semantic input: edges, recovery, carry/primary actions and movement dispatch.</summary>
public sealed partial class BlastSession
{
    // ─── input processing ─────────────────────────────────────────────────────

    private void ProcessInputs(ISessionContext context, float dt)
    {
        // Read each victim's own intent before applying the ghost's controls. Do
        // this before the player loop so dictionary order cannot change shake-off.
        foreach (var ghost in _players.Values)
            if (ghost.Alive && ghost.Ghost && ghost.LatchedToId is { } victimId)
            {
                if (_players.TryGetValue(victimId, out var victim) && victim.Alive && !victim.Ghost)
                    TrackLatch(ghost, victim, dt);
                else
                    DetachGhost(ghost);
            }
        foreach (var p in _players.Values)
        {
            if (!p.Alive || p.PendingRoundStart)
                continue;

            // Determine effective input (may be overridden by a latching ghost).
            var possessor = _players.Values.FirstOrDefault(g => g.Alive && g.Ghost && g.LatchedToId == p.Id);
            var input = possessor is null ? p.Input : p.Input with
            {
                X = p.Input.X + possessor.Input.X*.3f,
                Z = p.Input.Z + possessor.Input.Z*.3f,
            };
            if(input.Length>1)input=input with {X=input.X/input.Length,Z=input.Z/input.Length};
            // Clients preserve stick magnitude for precision aim. With no bomb,
            // the ignored modifier must retain ordinary full-speed walking.
            if(input.Aim&&p.CarryingBombId is null&&input.Length>0)
                input=input with {X=input.X/input.Length,Z=input.Z/input.Length};

            // Count down timers.
            if (p.InvincibleTimer > 0) p.InvincibleTimer = Math.Max(0,p.InvincibleTimer-dt);
            if (p.ImpactProtectionTimer > 0) p.ImpactProtectionTimer = Math.Max(0,p.ImpactProtectionTimer-dt);
            if(p.Falling||p.Airborne||p.StunTimer>0||p.CarriedById is not null)ClearKick(p);
            if(p.KickRecoveryRemaining>0)p.KickRecoveryRemaining=Math.Max(0,p.KickRecoveryRemaining-dt);
            if(p.Falling)
            {
                SteerFallingPlayer(p,input,dt);
                if(input.Length>.1f){p.FacingX=input.X/input.Length;p.FacingZ=input.Z/input.Length;}
                p.PrevA=input.A;p.PrevB=input.B;p.PrevR=input.R;p.PrevStop=input.Stop;
                continue;
            }
            if (ProcessRecovery(p,p.Input,dt)) continue;

            // Edge detection for buttons.
            var aPress = input.A && !p.PrevA;
            var bPress = input.B && !p.PrevB;
            var bRelease = !input.B && p.PrevB;
            var rPress = input.R && !p.PrevR;
            var stopPress = input.Stop && !p.PrevStop;
            p.PrevA = input.A;
            p.PrevB = input.B;
            p.PrevR = input.R;p.PrevStop=input.Stop;

            // Ghosts retain movement/latching, but no bomb input or collision path.
            if(p.Ghost)
            {
                ClearKick(p);ClearThrowAim(p);
                if(p.LatchedToId is null)MovePlayer(p,input,dt);
                if(_ghostsEnabled&&p.LatchedToId is null)TryLatch(p);
                continue;
            }
            if(stopPress)StopKickedBombs(p);
            if(AdvanceKick(p,dt))continue;
            UpdateThrowAim(p,input,dt);

            // A planted aim stance exchanges movement for deliberate direction and range.
            if(!p.Aiming)
            {
                if (p.CarryingBombId is null && p.CarryingPlayerId is null)
                    MovePlayer(p, input, dt);
                else
                    MovePlayerCarrying(p, input, dt);
            }
            if(input.AimTarget is {} point&&p.CarryingBombId is not null)
                PointThrowAim(p,input.AimRelative?new(p.X+point.X,p.Z+point.Y):point);

            // R: remote detonate.
            if (rPress && p.HasRemote)
                RemoteDetonate(context, p);

            if(p.KickBombId is null&&p.KickRecoveryRemaining<=0)
                ProcessBombActions(context,p,input,aPress,bPress,bRelease,dt);
        }
    }
}

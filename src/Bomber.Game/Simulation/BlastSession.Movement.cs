// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.

namespace Bomber.Game.Simulation;

/// <summary>Continuous player movement, tier support and solid collision.</summary>
public sealed partial class BlastSession
{
    // ─── movement ─────────────────────────────────────────────────────────────

    private const float PlayerSpeed = 4.5f;  // cells/sec
    private const float PlayerRadius = 0.4f;

    /// <summary>How high above its tier a player stands: a bomb coming down below this lands on their head.</summary>
    private const float HeadHeight = 1.2f;

    private void MovePlayer(BlastPlayer p, InputState input, float dt)
    {
        var startX = p.X;
        var startZ = p.Z;
        var speed=PlayerSpeed*p.StatusSpeed*KickMovementScale(p);
        ApplyMoveDelta(p, input.X * speed * dt, input.Z * speed * dt,dt);

        // Update facing for non-zero input.
        if (input.X * input.X + input.Z * input.Z > 0.01f)
        {
            p.FacingX = input.X;
            p.FacingZ = input.Z;
            // Normalise.
            var len = MathF.Sqrt(p.FacingX * p.FacingX + p.FacingZ * p.FacingZ);
            p.FacingX /= len;
            p.FacingZ /= len;
        }

        // Check kick: walk into a resting bomb.
        if(!p.Ghost)CheckKick(p, input, startX, startZ, dt);
    }

    private void MovePlayerCarrying(BlastPlayer p, InputState input, float dt)
    {
        ApplyMoveDelta(p, input.X * PlayerSpeed * p.StatusSpeed * dt, input.Z * PlayerSpeed * p.StatusSpeed * dt,dt);
        if (!p.AimSet && input.X * input.X + input.Z * input.Z > 0.01f)
        {
            p.FacingX = input.X; p.FacingZ = input.Z;
            var len = MathF.Sqrt(p.FacingX * p.FacingX + p.FacingZ * p.FacingZ);
            p.FacingX /= len; p.FacingZ /= len;
        }
    }

    private void ApplyMoveDelta(BlastPlayer p, float dx, float dz,float dt)
    {
        var originX=(int)MathF.Floor(p.X);var originZ=(int)MathF.Floor(p.Z);
        var previousY=p.Y;
        // Axis-separated to allow sliding along walls.
        var nx = p.X + dx;
        if (!CollidesWithSolid(nx, p.Z, p.Tier, p))
            p.X = nx;
        var nz = p.Z + dz;
        if (!CollidesWithSolid(p.X, nz, p.Tier, p))
            p.Z = nz;

        // Clamp to prevent overshooting entirely past thin walls (rarely matters at 30 Hz).
        p.X = Math.Clamp(p.X, -0.5f, _stage.Width + 0.5f);
        p.Z = Math.Clamp(p.Z, -0.5f, _stage.Height + 0.5f);

        var cell = _stage.AtHeight((int)MathF.Floor(p.X), (int)MathF.Floor(p.Z),previousY);
        var ground=PlayerLandingHeight(p.X,p.Z,previousY);
        if(!p.Ghost&&ground<previousY-.05f&&!CellsConnected(originX,originZ,(int)MathF.Floor(p.X),(int)MathF.Floor(p.Z),previousY))
        {
            p.Airborne=true;p.Falling=true;p.AirVY=0;
            p.AirVX=dx/Math.Max(dt,.0001f);p.AirVZ=dz/Math.Max(dt,.0001f);
            Present("player-fall",p.Id,p.Id,p.X,p.Y,p.Z);
            return;
        }
        p.SurfaceY=ground;p.Tier=(int)MathF.Round(ground);

        // Elimination: out-of-bounds or void/water cell.
        if (cell.IsElimination || p.X < 0 || p.X >= _stage.Width || p.Z < 0 || p.Z >= _stage.Height)
            Eliminate(null, p, cell.Kind == CellKind.Water ? "water" : "fall");
    }

    // Water/void cells have no floor. Their default Tier=0 is serialization
    // metadata, not a surface that can support or drown a falling player.
    // Use the same water plane published to the renderer; void falls below the
    // lowest floor before elimination as well. The player center still defines
    // the ledge, so this does not extend the island by the collision radius.
    private float PlayerLandingHeight(float x,float z,float y=float.PositiveInfinity) =>
        _stage.AtHeight((int)MathF.Floor(x),(int)MathF.Floor(z),y).Kind switch
        {
            CellKind.Water => _waterLevel,
            CellKind.Void => -1,
            _ => _stage.SurfaceAt(x,z,y),
        };

    private bool CollidesWithSolid(float x, float z, int tier, BlastPlayer mover)
    {
        // Ghosts stay on land after their comeback, including when the fall input is still held.
        if(mover.Ghost&&_stage.AtHeight((int)MathF.Floor(x),(int)MathF.Floor(z),mover.Y).IsElimination)return true;
        // Check a small set of cells the circle could touch.
        for (var cx = (int)MathF.Floor(x - PlayerRadius); cx <= (int)MathF.Floor(x + PlayerRadius); cx++)
            for (var cz = (int)MathF.Floor(z - PlayerRadius); cz <= (int)MathF.Floor(z + PlayerRadius); cz++)
            {
                if(_stage.DeckOverlaps(cx,cz,mover.Y,HeadHeight)&&CircleOverlapsCell(x,z,cx,cz)&&!EscapingCell(x,z,mover,cx,cz))return true;
                var cell = _stage.AtHeight(cx, cz,mover.Y);
                if (cell.IsPassable && CircleOverlapsCell(x, z, cx, cz) &&
                    !CellsConnected((int)MathF.Floor(mover.X), (int)MathF.Floor(mover.Z), cx, cz,mover.Y)&&
                    _stage.SurfaceAt(Math.Clamp(x,cx+.001f,cx+.999f),Math.Clamp(z,cz+.001f,cz+.999f),mover.Y)>mover.Y+.05f&&
                    !EscapingCell(x,z,mover,cx,cz)) return true;
                if (cell.Kind == CellKind.Pillar) // hard wall
                {
                    if (CircleOverlapsCell(x, z, cx, cz))
                        return true;
                }
                if (_softBlocks.IsAlive(cx, cz))
                {
                    if (CircleOverlapsCell(x, z, cx, cz))
                        return true;
                }
            }

        if(mover.Ghost)return false;
        // Resting/stationary bombs on the same tier block movement.
        foreach (var b in _bombs)
        {
            if (b.State != BombState.Resting) continue;
            if (Math.Abs(BombY(b) - _stage.SurfaceAt(x, z,mover.Y)) > .6f) continue;
            if (b.OwnerId == mover.Id && b.OwnerMayLeave) continue; // first departure is geometric, not a time-limited escape
            var distance = Dist2(b.X,b.Z,x,z);
            var previousDistance = Dist2(b.X,b.Z,mover.X,mover.Z);
            var clearance = (PlayerRadius + BlastBomb.Radius) * (PlayerRadius + BlastBomb.Radius);
            if (distance < clearance && !(previousDistance < clearance && distance > previousDistance + .00001f))
                return true;
        }
        return false;
    }

    private bool CellsConnected(int ax, int az, int bx, int bz,float y=float.PositiveInfinity)
    {
        if (ax == bx && az == bz) return true;
        if (ax == bx || az == bz) return _stage.CanCross(ax, az, bx, bz,y);
        return (_stage.CanCross(ax, az, bx, az,y) && _stage.CanCross(bx, az, bx, bz,y)) ||
            (_stage.CanCross(ax, az, ax, bz,y) && _stage.CanCross(ax, bz, bx, bz,y));
    }

    private static bool CircleOverlapsCell(float x, float z, int cx, int cz)
    {
        var nx = Math.Clamp(x, cx, cx + 1f);
        var nz = Math.Clamp(z, cz, cz + 1f);
        var ddx = x - nx;
        var ddz = z - nz;
        return ddx * ddx + ddz * ddz < PlayerRadius * PlayerRadius - .00001f;
    }

    private static bool EscapingCell(float x,float z,BlastPlayer p,int cx,int cz)
    {
        static float Distance(float x,float z,int cx,int cz)
        {var dx=x-Math.Clamp(x,cx,cx+1f);var dz=z-Math.Clamp(z,cz,cz+1f);return dx*dx+dz*dz;}
        var old=Distance(p.X,p.Z,cx,cz);
        return old<=PlayerRadius*PlayerRadius+.000001f&&Distance(x,z,cx,cz)>=old-.000001f;
    }
}

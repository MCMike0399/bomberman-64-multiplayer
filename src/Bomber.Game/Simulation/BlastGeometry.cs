namespace Bomber.Game.Simulation;

/// <summary>Shared three-dimensional blast volume and sampled solid cover, used by damage and bot planning.</summary>
public static class BlastGeometry
{
    public static bool Contains(StageLayout stage, SoftBlockGrid soft, float bx, float by, float bz, float radius,
        float x, float y, float z, bool piercing = false)
    {
        var dx = x - bx; var dy = y - by; var dz = z - bz;
        return dx * dx + dy * dy + dz * dz <= radius * radius &&
            !Occluded(stage, soft, bx, by + .35f, bz, x, y + .35f, z, piercing);
    }

    /// <summary>Red blasts pass soft cover; hard stone and terrain always block them.
    /// End cells are excluded so a cover block can itself be hit rather than shielding itself.</summary>
    public static bool Occluded(StageLayout stage, SoftBlockGrid soft, float ax, float ay, float az,
        float bx, float by, float bz, bool piercing = false)
    {
        var dx = bx - ax; var dy = by - ay; var dz = bz - az;
        var length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        var steps = Math.Max(2, (int)MathF.Ceiling(length / .10f));
        var acx = (int)MathF.Floor(ax); var acz = (int)MathF.Floor(az);
        var bcx = (int)MathF.Floor(bx); var bcz = (int)MathF.Floor(bz);
        for (var i = 1; i < steps; i++)
        {
            var t = (float)i / steps; var x = ax + dx * t; var z = az + dz * t; var y = ay + dy * t;
            var cx = (int)MathF.Floor(x); var cz = (int)MathF.Floor(z);
            var cell = stage.At(cx, cz);
            var surface = stage.SurfaceAt(x, z);
            if (!cell.IsElimination && surface > y + .02f) return true;
            if (cell.Kind == CellKind.Pillar && surface + 1.75f > y) return true;
            if ((cx == acx && cz == acz) || (cx == bcx && cz == bcz)) continue;
            if (!piercing && soft.IsAlive(cx, cz) && surface + 1 > y) return true;
        }
        return false;
    }
}

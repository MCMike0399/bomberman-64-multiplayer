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
            var top = stage.At(cx, cz);
            if(stage.DecksBlockBlasts && top.Below is not null && y >= top.Tier - CellDef.DeckThickness && y <= top.Tier) return true;
            var cell = !stage.DecksBlockBlasts&&top.Below is not null&&y<top.Tier ? top.Below : stage.AtHeight(cx, cz,y);
            var surface = stage.SurfaceAt(x, z,!stage.DecksBlockBlasts&&top.Below is not null&&y<top.Tier?float.NegativeInfinity:y);
            if (!cell.IsElimination && surface > y + .02f) return true;
            if (cell.Kind == CellKind.Pillar && surface + (cell.ObstacleHeight ?? 1.75f) > y) return true;
            if ((cx == acx && cz == acz) || (cx == bcx && cz == bcz)) continue;
            // Retain the original one-cell blast shielding minimum; taller authored cover extends it.
            if (!piercing && soft.IsAlive(cx, cz) && stage.SurfaceAt(x,z) <= y && stage.SurfaceAt(x,z) + Math.Max(1,stage.SoftBlockHeight) > y) return true;
        }
        return false;
    }
}

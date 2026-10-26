// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.

namespace Bomber.Game.Simulation.State;

/// <summary>The mutable set of soft blocks still standing (one bool per grid cell).</summary>
public sealed class SoftBlockGrid
{
    private readonly bool[,] _alive;

    public SoftBlockGrid(int width, int height) => _alive = new bool[width, height];

    public bool IsAlive(int x, int z)
    {
        if (x < 0 || x >= _alive.GetLength(0) || z < 0 || z >= _alive.GetLength(1))
            return false;
        return _alive[x, z];
    }

    public void Set(int x, int z, bool value) => _alive[x, z] = value;

    /// <summary>Compact bitset as base64 (row-major).</summary>
    public string ToBase64()
    {
        var w = _alive.GetLength(0);
        var h = _alive.GetLength(1);
        var bits = new byte[(w * h + 7) / 8];
        for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
            {
                var idx = z * w + x;
                if (_alive[x, z])
                    bits[idx / 8] |= (byte)(1 << (idx % 8));
            }
        return Convert.ToBase64String(bits);
    }
}

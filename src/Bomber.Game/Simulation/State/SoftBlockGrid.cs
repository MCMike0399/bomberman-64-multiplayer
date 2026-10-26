// Adapted from burbuja-kernel Example.Blast, MIT (copyright 2026 MCMike0399).
// Product-owned simulation; original baseline b0758339984bbeead1b3d1236f31e980aaa5f20e.

namespace Bomber.Game.Simulation.State;

/// <summary>The mutable set of soft blocks still standing, in the snapshot's row-major bit order.</summary>
public sealed class SoftBlockGrid
{
    private readonly int _width,_height;
    private readonly byte[] _bits;
    private string? _encoded;

    public SoftBlockGrid(int width, int height)
    {
        if(width<0||height<0)throw new OverflowException("Array dimensions must be nonnegative.");
        _width=width;_height=height;
        _bits=new byte[(checked(width*height)+7L)/8];
    }

    public bool IsAlive(int x, int z)
    {
        if ((uint)x >= (uint)_width || (uint)z >= (uint)_height)
            return false;
        int index=z*_width+x;
        return (_bits[index>>3]&(1<<(index&7)))!=0;
    }

    public void Set(int x, int z, bool value)
    {
        // Preserve bounds checks: an invalid X must never alias the adjacent row.
        if((uint)x>=(uint)_width||(uint)z>=(uint)_height)throw new IndexOutOfRangeException();
        int index=z*_width+x;byte mask=(byte)(1<<(index&7));
        if (((_bits[index>>3]&mask)!=0) == value) return;
        if(value)_bits[index>>3]|=mask;else _bits[index>>3]&=(byte)~mask;
        _encoded = null;
    }

    /// <summary>Compact bitset as base64 (row-major).</summary>
    public string ToBase64()
    {
        if (_encoded is not null) return _encoded;
        return _encoded = Convert.ToBase64String(_bits);
    }
}

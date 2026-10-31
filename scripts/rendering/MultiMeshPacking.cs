using Godot;
namespace BlastCircuit.Rendering;

/// <summary>
/// Packs Transform3D+color instances into a managed MultiMesh buffer. Assigning
/// MultiMesh.Buffer keeps the exact floats observable to headless input capture;
/// per-instance setters leave the dummy renderer's buffer empty.
/// </summary>
internal static class MultiMeshPacking
{
    internal const int Stride=16;
    internal static void Write(float[] buffer,int index,Transform3D transform,Color color)
    {
        int at=index*Stride;Basis b=transform.Basis;
        // Rows of the 3x4 matrix (basis row, then origin), followed by RGBA.
        buffer[at]=b.Row0.X;buffer[at+1]=b.Row0.Y;buffer[at+2]=b.Row0.Z;buffer[at+3]=transform.Origin.X;
        buffer[at+4]=b.Row1.X;buffer[at+5]=b.Row1.Y;buffer[at+6]=b.Row1.Z;buffer[at+7]=transform.Origin.Y;
        buffer[at+8]=b.Row2.X;buffer[at+9]=b.Row2.Y;buffer[at+10]=b.Row2.Z;buffer[at+11]=transform.Origin.Z;
        buffer[at+12]=color.R;buffer[at+13]=color.G;buffer[at+14]=color.B;buffer[at+15]=color.A;
    }
}

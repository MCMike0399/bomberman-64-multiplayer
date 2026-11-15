using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Rendering;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private Node3D CreateHollowStone(Node3D root,int cell)
    {
        root.Name="HollowStandingStone"+cell;
        if(_hollowStoneMeshes.Length==0)_hollowStoneMeshes=Enumerable.Range(0,3).Select(HollowStandingStoneMesh).ToArray();
        root.AddChild(new MeshInstance3D{Mesh=_hollowStoneMeshes[cell%3],MaterialOverride=_hollowStoneMaterial});
        root.Basis=PresentationMath.Rotation(new(0,(cell*2.39996f)%Mathf.Tau,0));
        return root;
    }

    // Three original volumes share the exact authority peak and broad foot:
    // a broad slab, tapered marker and leaning sentinel. None reuses garden rocks.
    private Mesh HollowStandingStoneMesh(int variant)
    {
        const int sides=7;float height=Layout.SoftBlockHeight*HeightScale;
        var rings=new Vector3[4,sides];
        float upper=variant==0?.40f:variant==1?.32f:.24f;
        float cap=variant==0?.31f:variant==1?.24f:.17f;
        float lean=variant==2?.14f:variant==1?-.07f:.015f;
        for(int row=0;row<4;row++)for(int i=0;i<sides;i++)
        {
            float angle=i*Mathf.Tau/sides;
            var(s,c)=PresentationMath.SinCos(angle);
            float radius=row==0?.46f:row==1?.44f:row==2?upper:cap;
            radius*=1+.055f*PresentationMath.Sin(i*2.2f+variant);
            float y=row==0?.015f:row==1?height*.22f:row==2?height*.75f:height;
            rings[row,i]=new(c*radius+lean*row/3f,y,s*radius);
        }
        var mesh=new SurfaceTool();mesh.Begin(Mesh.PrimitiveType.Triangles);
        for(int i=0;i<sides;i++)
        {
            int j=(i+1)%sides;
            for(int row=0;row<3;row++)
                HollowQuad(mesh,rings[row,i],rings[row,j],rings[row+1,j],rings[row+1,i],(rings[row,i]+rings[row,j])/2-new Vector3(0,height*.5f,0));
            HollowTriangle(mesh,new(lean,height,0),rings[3,i],rings[3,j],Vector3.Up);
            HollowTriangle(mesh,new(0,.015f,0),rings[0,j],rings[0,i],Vector3.Down);
        }
        mesh.Index();return mesh.Commit();
    }
}

using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private Material WindPaint(Color color,bool blades=false)
    {
        // Coastal trees retain their approved paint and silhouette.
        if(!_garden)return _art.Color(color,.9f);
        string key=color.ToHtml()+":"+blades;
        if(_foliageMaterials.TryGetValue(key,out var material))return material;
        material=_art.Foliage(color,blades);_foliageMaterials[key]=material;
        _ambientMaterials.Add(material);return material;
    }

    /// <summary>One opaque draw of rooted grass, entirely outside the stone enclosure.</summary>
    private void MeadowGrass()
    {
        if(!SceneryMeshes.TryGetValue("meadow-blades",out var mesh))
        {
            var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
            // Three crossed, segmented blades per tuft. UV.y pins each root.
            for(int blade=0;blade<3;blade++)
            {
                float angle=blade*Mathf.Tau/3;
                Vector3 Point(float x,float y)=>new(Mathf.Cos(angle)*x+.12f*y*y,y,Mathf.Sin(angle)*x);
                void Vertex(float x,float y){surface.SetNormal(new Vector3(Mathf.Sin(angle),.25f,-Mathf.Cos(angle)).Normalized());surface.SetUV(new(x+.5f,y));surface.AddVertex(Point(x,y));}
                Vertex(-.095f,0);Vertex(.055f,.55f);Vertex(.095f,0);
                Vertex(-.095f,0);Vertex(-.055f,.55f);Vertex(.055f,.55f);
                Vertex(-.055f,.55f);Vertex(.12f,1);Vertex(.055f,.55f);
            }
            surface.Index();SceneryMeshes["meadow-blades"]=mesh=surface.Commit();
        }
        const int count=1500;
        var grass=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,Mesh=mesh,InstanceCount=count};
        var random=new Random(364);var buffer=new float[count*MultiMeshPacking.Stride];
        for(int i=0;i<count;i++)
        {
            // Keep front grass short and sparse so the court remains the focal point.
            float x,z;
            do{x=-7+(float)random.NextDouble()*(Width+14);z=-7+(float)random.NextDouble()*(Depth+11);}
            while(x>-.75f&&x<Width+.75f&&z>-.75f&&z<Depth+.75f||Math.Abs(z+3.4f)<1.15f);
            float h=.24f+(float)random.NextDouble()*.38f;
            if(z>Depth)h*=.65f;
            // Managed sin/cos: Basis.FromEuler goes through each OS's libm.
            var (s,c)=PresentationMath.SinCos((float)random.NextDouble()*Mathf.Tau);
            var basis=new Basis(c,0,s,0,1,0,-s,0,c).Scaled(new(.7f,h,.7f));
            MultiMeshPacking.Write(buffer,i,new(basis,new(x,-.24f,z)),new Color(.88f+(float)random.NextDouble()*.12f,1,.9f,1));
        }
        grass.Buffer=buffer;
        _scenery!.AddChild(new MultiMeshInstance3D{Name="WindGrass",Multimesh=grass,MaterialOverride=WindPaint(new("76a747"),true),
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,ExtraCullMargin=.3f});

        // A small number of airborne leaves follows the same prevailing breeze.
        var leaves=Prop(_scenery,"DriftingLeaves",Vector3.Zero);
        var leafPaint=WindPaint(new("bfd46c"));
        for(int i=0;i<12;i++)
        {
            float phase=i*2.39996f;bool right=i%2==0;
            var leaf=Bead(leaves,Vector3.Zero,new(.16f,.025f,.08f),leafPaint);
            leaf.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
            _sceneryMotion.Add(t=>
            {
                float u=t*.55f+phase;
                leaf.Position=new(right?Width+1.7f+Mathf.Sin(u*.6f)*.6f:-1.7f+Mathf.Sin(u*.6f)*.6f,
                    .4f+Mathf.PosMod(phase-t*.12f,1.8f),Mathf.PosMod(u*1.4f,Depth+5)-2.5f);
                leaf.Basis=PresentationMath.Rotation(new(u*1.6f,u*.9f,PresentationMath.Sin(u*2)*.6f),new(.16f,.025f,.08f));
            });
        }
    }
}

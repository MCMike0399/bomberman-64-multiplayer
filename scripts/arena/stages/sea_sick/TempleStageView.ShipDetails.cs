using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    // Long axis is X. The square authority court remains a separate opening in
    // a broad toy hull: the existing galleries read as the raised ship's ends.
    private void BuildShipHull(Material timber)
    {
        var navy=_art.Color(new("263d56"),.84f);
        var cream=_art.Color(new("e8dcb9"),.8f);
        AddChild(new MeshInstance3D{Name="CutterHull",Mesh=TubbyHullMesh(0,Mathf.Pi,true),MaterialOverride=navy});
        AddChild(new MeshInstance3D{Name="CutterWaleNear",Mesh=TubbyHullMesh(.055f,.145f),MaterialOverride=cream});
        AddChild(new MeshInstance3D{Name="CutterWaleFar",Mesh=TubbyHullMesh(Mathf.Pi-.145f,Mathf.Pi-.055f),MaterialOverride=cream});
        AddChild(new MeshInstance3D{Name="CutterShoulders",Mesh=TubbyShoulderMesh(),MaterialOverride=timber});

        // Large silhouette features only for the first shape study. The bow is
        // right, stern left: neither is hidden under the battle HUD or footer.
        var bowsprit=ShipLathe(this,new(22.5f,1.8f,9.5f),[(-.7f,.22f),(1.35f,.13f)],timber,"tubby-bowsprit");
        bowsprit.Basis=PresentationMath.Rotation(new(0,0,-Mathf.Pi/2));bowsprit.Name="CutterBowsprit";
        var rudder=Box(this,new(-3.55f,-1.5f,9.5f),new(.42f,2.9f,1.9f),navy,.2f);rudder.Name="CutterRudder";
        Box(this,new(-2.95f,1.45f,9.5f),new(2.2f,.28f,6.4f),timber,.18f).Name="SternTransomCap";
    }

    private static (float X,float Beam,float Sheer,float Keel)[] TubbySections()=>TubbyHullSections;

    private static (float Beam,float Sheer,float Keel) TubbySection(float x)
    {
        var sections=TubbySections();
        int i=0;while(i<sections.Length-2&&x>sections[i+1].X)i++;
        var a=sections[i];var b=sections[i+1];var before=sections[Math.Max(0,i-1)];var after=sections[Math.Min(sections.Length-1,i+2)];
        float length=b.X-a.X,t=Mathf.Clamp((x-a.X)/length,0,1),t2=t*t,t3=t2*t;
        float Curve(float p0,float p1,float p2,float p3)
        {
            float m0=(p2-p0)/(b.X-before.X)*length,m1=(p3-p1)/(after.X-a.X)*length;
            return (2*t3-3*t2+1)*p1+(t3-2*t2+t)*m0+(-2*t3+3*t2)*p2+(t3-t2)*m1;
        }
        return (Curve(before.Beam,a.Beam,b.Beam,after.Beam),Curve(before.Sheer,a.Sheer,b.Sheer,after.Sheer),Curve(before.Keel,a.Keel,b.Keel,after.Keel));
    }

    private static Vector3 TubbyPoint(float x,float angle)
    {
        var section=TubbySection(x);var(sin,cos)=PresentationMath.SinCos(angle);
        float fall=sin*(1.1f-.1f*sin);
        return new(x,section.Sheer-(section.Sheer-section.Keel)*fall,9.5f+section.Beam*cos*(1+.22f*sin));
    }

    private static Vector3 TubbyNormal(float x,float angle)
    {
        float left=Math.Max(-3.5f,x-.01f),right=Math.Min(22.7f,x+.01f);
        float low=Math.Max(0,angle-.002f),high=Math.Min(Mathf.Pi,angle+.002f);
        var along=TubbyPoint(right,angle)-TubbyPoint(left,angle);
        var around=TubbyPoint(x,high)-TubbyPoint(x,low);
        return around.Cross(along).Normalized();
    }

    private static float[] TubbySlices()
    {
        var sections=TubbySections();var slices=new List<float>();
        for(int i=0;i<sections.Length-1;i++)for(int step=0;step<8;step++)
            slices.Add(Mathf.Lerp(sections[i].X,sections[i+1].X,step/8f));
        slices.Add(sections[^1].X);return slices.ToArray();
    }

    /// <summary>Lofted U-sections with smooth normals and Godot clockwise faces.
    /// Bands occupy a tiny outward offset, rather than coplanar duplicate faces.</summary>
    private static ArrayMesh TubbyHullMesh(float from,float to,bool caps=false)
    {
        string key=$"tubby-hull:{from}:{to}:{caps}";
        if(ShipMeshes.TryGetValue(key,out var cached))return cached;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        var slices=TubbySlices();const int rings=40;
        void Vertex(float x,float angle)
        {
            var normal=TubbyNormal(x,angle);var p=TubbyPoint(x,angle)+normal*(caps?0:.012f);
            surface.SetNormal(normal);surface.SetUV(new((x+3.5f)/26.2f,angle/Mathf.Pi));surface.AddVertex(p);
        }
        for(int i=0;i<slices.Length-1;i++)for(int ring=0;ring<rings;ring++)
        {
            float a=Mathf.Lerp(from,to,ring/(float)rings),b=Mathf.Lerp(from,to,(ring+1f)/rings),x=slices[i],next=slices[i+1];
            // (c-a) cross (b-a) points outward: Godot's visible front is clockwise.
            Vertex(x,a);Vertex(next,a);Vertex(next,b);Vertex(x,a);Vertex(next,b);Vertex(x,b);
        }
        if(caps)foreach(var (x,normal) in new[]{(-3.5f,Vector3.Left),(22.7f,Vector3.Right)})
        {
            var section=TubbySection(x);var centre=new Vector3(x,(section.Sheer+section.Keel)/2,9.5f);
            for(int ring=0;ring<rings;ring++)
            {
                var a=TubbyPoint(x,ring*Mathf.Pi/rings);var b=TubbyPoint(x,(ring+1f)*Mathf.Pi/rings);
                if((b-centre).Cross(a-centre).Dot(normal)<0)(a,b)=(b,a);
                foreach(var p in new[]{centre,a,b}){surface.SetNormal(normal);surface.SetUV(new(p.Z,p.Y));surface.AddVertex(p);}
            }
            var first=TubbyPoint(x,0);var last=TubbyPoint(x,Mathf.Pi);
            if((last-centre).Cross(first-centre).Dot(normal)<0)(first,last)=(last,first);
            foreach(var p in new[]{centre,first,last}){surface.SetNormal(normal);surface.SetUV(new(p.Z,p.Y));surface.AddVertex(p);}
        }
        surface.Index();return ShipMeshes[key]=surface.Commit();
    }

    /// <summary>Curved shoulders outside the unchanged authority rectangle. The
    /// top strips roll up to the sheer; the actual court is never re-levelled.</summary>
    private static ArrayMesh TubbyShoulderMesh()
    {
        const string key="tubby-shoulder";
        if(ShipMeshes.TryGetValue(key,out var cached))return cached;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);var slices=TubbySlices();
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            if((c-a).Cross(b-a).Y<0)(b,c)=(c,b);
            var normal=(c-a).Cross(b-a).Normalized();
            foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.X*.2f,p.Z));surface.AddVertex(p);}
        }
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){Triangle(a,b,c);Triangle(a,c,d);}
        for(int i=0;i<slices.Length-1;i++)
        {
            float x=slices[i],next=slices[i+1];
            if(next<=0||x>=19)
            {
                Quad(TubbyPoint(x,0),TubbyPoint(next,0),TubbyPoint(next,Mathf.Pi),TubbyPoint(x,Mathf.Pi));
                continue;
            }
            float Floor(float at)=>at<=3.01f||at>=15.99f?1.20f:-.15f;
            foreach(bool near in new[]{true,false})
            {
                float angle=near?0:Mathf.Pi,edge=near?19:0;
                Quad(new(x,Floor(x),edge),new(next,Floor(next),edge),TubbyPoint(next,angle),TubbyPoint(x,angle));
            }
        }
        surface.Index();return ShipMeshes[key]=surface.Commit();
    }

    /// <summary>Inspection-only material study; gameplay never enables this.</summary>
    public void SetShipShapeStudy(bool enabled)
    {
        if(!_seaSick)return;
        IEnumerable<Node> Descendants(Node parent)
        {
            foreach(var child in parent.GetChildren()){yield return child;foreach(var nested in Descendants(child))yield return nested;}
        }
        if(!enabled)
        {
            foreach(var (mesh,material) in _shipStudyMaterials)if(GodotObject.IsInstanceValid(mesh))mesh.MaterialOverride=material;
            _shipStudyMaterials.Clear();_graphicsRevision=-1;return;
        }
        var body=_art.Color(new Color(.27f,.27f,.27f),.95f);var trim=_art.Color(new Color(.80f,.80f,.80f),.95f);
        var deck=_art.Color(new Color(.60f,.60f,.60f),.95f);var sea=_art.Color(new Color(.38f,.38f,.38f),.95f);
        foreach(var mesh in Descendants(this).OfType<MeshInstance3D>())
        {
            _shipStudyMaterials.TryAdd(mesh,mesh.MaterialOverride);
            string name=mesh.Name.ToString();
            mesh.MaterialOverride=name.StartsWith("CutterWale")?trim:name=="ShipOcean"||name=="Seabed"?sea:
                name=="CutterHull"||name=="CutterRudder"?body:deck;
        }
    }
}

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

        // The converging bow is a rising cambered mass. The stern is a broad
        // transom and curved taffrail; neither end is an extra flat shelf.
        var heel=new Vector3(22.5f,2.95f,9.5f);var tip=TubbyBowspritTip();var up=(tip-heel).Normalized();
        var bowsprit=ShipLathe(this,heel,[(0,.23f),((tip-heel).Length(),.16f)],timber,"tubby-raked-bowsprit");
        var right=up.Cross(Vector3.Back).Normalized();bowsprit.Basis=new(right,up,right.Cross(up));bowsprit.Name="CutterBowsprit";
        Box(this,new(-3.55f,-1.5f,9.5f),new(.42f,2.9f,1.9f),navy,.2f).Name="CutterRudder";
        AddChild(new MeshInstance3D{Name="CutterTaffrail",Mesh=TubbyTaffrailMesh(),MaterialOverride=cream});
        var slices=TubbySlices();
        foreach(float angle in new[]{0f,Mathf.Pi})for(int i=0;i<slices.Length-1;i++)
            if(slices[i]>=19)
                ShipRope(TubbyPoint(slices[i],angle)+Vector3.Up*.1f,TubbyPoint(slices[i+1],angle)+Vector3.Up*.1f,.105f,cream).Name="ConvergingBowRail";

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
        float q=Mathf.Clamp(sin/.22f,0,1),belly=.355f*16*q*q*(1-q)*(1-q);
        return new(x,section.Sheer-(section.Sheer-section.Keel)*fall,9.5f+(section.Beam+belly)*cos);
    }

    private static Vector3 TubbyNormal(float x,float angle)
    {
        float left=Math.Max(-3.2f,x-.01f),right=Math.Min(22.8f,x+.01f);
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
            surface.SetNormal(normal);surface.SetUV(new((x+3.2f)/26f,angle/Mathf.Pi));surface.AddVertex(p);
        }
        for(int i=0;i<slices.Length-1;i++)for(int ring=0;ring<rings;ring++)
        {
            float a=Mathf.Lerp(from,to,ring/(float)rings),b=Mathf.Lerp(from,to,(ring+1f)/rings),x=slices[i],next=slices[i+1];
            // (c-a) cross (b-a) points outward: Godot's visible front is clockwise.
            Vertex(x,a);Vertex(next,a);Vertex(next,b);Vertex(x,a);Vertex(next,b);Vertex(x,b);
        }
        if(caps)foreach(var (x,normal) in new[]{(-3.2f,Vector3.Left),(22.8f,Vector3.Right)})
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
                const int across=24;
                Vector3 Cap(float at,float v)
                {
                    var section=TubbySection(at);
                    float hump=at>=19?.40f*PresentationMath.Sin(Mathf.Clamp((at-19)/3.8f,0,1)*Mathf.Pi):.10f;
                    return new(at,section.Sheer+hump*(1-v*v),9.5f+section.Beam*v);
                }
                for(int span=0;span<across;span++)
                {
                    float a=-1+2*span/(float)across,b=-1+2*(span+1)/(float)across;
                    Quad(Cap(x,a),Cap(next,a),Cap(next,b),Cap(x,b));
                }
                continue;
            }
            float Rail(float at)=>at<3||at>=16?1.795f:.445f;
            foreach(bool near in new[]{true,false})
            {
                float angle=near?0:Mathf.Pi,edge=near?18.5f:.5f;
                Quad(new(x,Rail(x),edge),new(next,Rail(next),edge),TubbyPoint(next,angle),TubbyPoint(x,angle));
            }
        }
        surface.Index();return ShipMeshes[key]=surface.Commit();
    }


    private static Vector3 TubbyBowspritTip()
    {
        var(sin,cos)=PresentationMath.SinCos(Mathf.Pi/9);
        return new Vector3(22.5f,2.95f,9.5f)+new Vector3(cos,sin,0)*2.2f;
    }

    private static ArrayMesh TubbyTaffrailMesh()
    {
        const string key="tubby-taffrail";if(ShipMeshes.TryGetValue(key,out var cached))return cached;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(float v,bool outer,bool top)=>new(-3.15f-.35f*(1-v*v)+(outer?-.18f:.18f),top?3.15f+.12f*(1-v*v):1.05f,9.5f+8.25f*v);
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
        {
            foreach(var triangle in new[]{new[]{a,b,c},new[]{a,c,d}})
            {
                var normal=(triangle[2]-triangle[0]).Cross(triangle[1]-triangle[0]).Normalized();
                if(normal.Dot(outward)<0){(triangle[1],triangle[2])=(triangle[2],triangle[1]);normal=-normal;}
                foreach(var p in triangle){surface.SetNormal(normal);surface.SetUV(new(p.Z*.2f,p.Y));surface.AddVertex(p);}
            }
        }
        for(int i=0;i<32;i++)
        {
            float a=-1+i/16f,b=-1+(i+1)/16f;
            Quad(Point(a,true,false),Point(b,true,false),Point(b,true,true),Point(a,true,true),Vector3.Left);
            Quad(Point(a,false,false),Point(b,false,false),Point(b,false,true),Point(a,false,true),Vector3.Right);
            Quad(Point(a,true,true),Point(b,true,true),Point(b,false,true),Point(a,false,true),Vector3.Up);
            Quad(Point(a,true,false),Point(b,true,false),Point(b,false,false),Point(a,false,false),Vector3.Down);
        }
        foreach(float end in new[]{-1f,1f})Quad(Point(end,true,false),Point(end,false,false),Point(end,false,true),Point(end,true,true),end<0?Vector3.Forward:Vector3.Back);
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
        var deck=_art.Color(new Color(.60f,.60f,.60f),.95f);
        foreach(var mesh in Descendants(this).OfType<MeshInstance3D>())
        {
            string name=mesh.Name.ToString();
            if(name is "ShipOcean" or "Seabed")continue;
            _shipStudyMaterials.TryAdd(mesh,mesh.MaterialOverride);
            mesh.MaterialOverride=name.StartsWith("CutterWale")?trim:
                name=="CutterHull"||name=="CutterRudder"?body:deck;
        }
    }
}

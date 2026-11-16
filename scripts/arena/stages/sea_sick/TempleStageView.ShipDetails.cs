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
        var navy=ShipPaint("263d56",style:2);
        var cream=_art.Color(new("e8dcb9"),.8f);
        AddChild(new MeshInstance3D{Name="CutterHull",Mesh=TubbyHullMesh(0,Mathf.Pi,true),MaterialOverride=navy});
        float bandFrom=.18f*Mathf.Pi/2,bandTo=.26f*Mathf.Pi/2;
        AddChild(new MeshInstance3D{Name="CutterWaleNear",Mesh=TubbyHullMesh(bandFrom,bandTo),MaterialOverride=cream});
        AddChild(new MeshInstance3D{Name="CutterWaleFar",Mesh=TubbyHullMesh(Mathf.Pi-bandTo,Mathf.Pi-bandFrom),MaterialOverride=cream});
        AddChild(new MeshInstance3D{Name="CutterShoulders",Mesh=TubbyShoulderMesh(),MaterialOverride=ShipPaint("b98450",style:1)});

        // The converging bow is a rising cambered mass. The stern is a broad
        // transom and curved taffrail; neither end is an extra flat shelf.
        var heel=new Vector3(25.3f,5.0f,9.5f);var tip=TubbyBowspritTip();var up=(tip-heel).Normalized();
        var bowsprit=ShipLathe(this,heel,[(0,.23f),((tip-heel).Length(),.16f)],timber,"tubby-raked-bowsprit");
        var right=up.Cross(Vector3.Back).Normalized();bowsprit.Basis=new(right,up,right.Cross(up));bowsprit.Name="CutterBowsprit";
        Box(this,new(-1.72f,-1.5f,9.5f),new(.42f,2.9f,1.9f),navy,.2f).Name="CutterRudder";
        AddChild(new MeshInstance3D{Name="CutterTaffrail",Mesh=TubbyTaffrailMesh(),MaterialOverride=cream});
        AddChild(new MeshInstance3D{Name="GalleryBulwarks",Mesh=TubbyGalleryBulwarks(),MaterialOverride=ShipPaint("65452f",true)});
        foreach(float x in new[]{3f,16f})foreach(float z in new[]{.5f,18.5f})
            ShipLathe(this,new(x,0,z),[(TubbySection(x).Sheer,.28f),(1.87f,.28f)],cream,$"gallery-end:{x}").Name="GalleryBulwarkPost";
        var slices=TubbySlices();
        foreach(float v in new[]{-1f,1f})for(int i=0;i<slices.Length-1;i++)
            if(slices[i]>=19)
                ShipBeam(TubbyCapPoint(slices[i],v)+Vector3.Up*.09f,TubbyCapPoint(slices[i+1],v)+Vector3.Up*.09f,.28f,.18f,cream).Name="BowCapRail";
        for(int i=0;i<slices.Length-1;i++)if(slices[i]>=19)
            ShipBeam(TubbyCapPoint(slices[i],0)+Vector3.Up*.10f,TubbyCapPoint(slices[i+1],0)+Vector3.Up*.10f,.24f,.20f,timber).Name="BowCentreRidge";


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

    /// <summary>Explicit rail/wale/waterline/bilge/keel section. The parameter
    /// only walks the two sides; no circular bowl narrows beneath the rail.</summary>
    private static Vector3 TubbyPoint(float x,float angle)
    {
        var section=TubbySection(x);bool near=angle<=Mathf.Pi/2;
        float u=Mathf.Clamp((near?angle:Mathf.Pi-angle)/(Mathf.Pi/2),0,1);
        float wale=section.Sheer-.9f;
        // At the raised stem the authored keel is above the generic -3.4 bilge.
        // Compress only those lower points above that keel to keep a closed,
        // monotonic section. Deep-body station coordinates remain exact.
        float water=Math.Max(-2.1f,Mathf.Lerp(wale,section.Keel,.70f));
        float bilge=Math.Max(-3.4f,Mathf.Lerp(water,section.Keel,.70f));
        Span<Vector3> points=stackalloc Vector3[5];
        points[0]=new(0,section.Beam,section.Sheer);
        points[1]=new(.22f,section.Beam+.35f,wale);
        points[2]=new(.50f,section.Beam+.45f,water);
        points[3]=new(.75f,Math.Max(section.Beam-.6f,section.Beam*.10f),bilge);
        points[4]=new(1,0,section.Keel);
        int i=0;while(i<3&&u>points[i+1].X)i++;
        var a=points[i];var b=points[i+1];var before=points[Math.Max(0,i-1)];var after=points[Math.Min(4,i+2)];
        float length=b.X-a.X,t=(u-a.X)/length,t2=t*t,t3=t2*t;
        float Curve(float p0,float p1,float p2,float p3)
        {
            float m0=(p2-p0)/(b.X-before.X)*length,m1=(p3-p1)/(after.X-a.X)*length;
            return (2*t3-3*t2+1)*p1+(t3-2*t2+t)*m0+(-2*t3+3*t2)*p2+(t3-t2)*m1;
        }
        float breadth=Math.Max(0,Curve(before.Y,a.Y,b.Y,after.Y));
        return new(x,Curve(before.Z,a.Z,b.Z,after.Z),9.5f+(near?1:-1)*breadth);
    }

    private static Vector3 TubbyNormal(float x,float angle)
    {
        float left=Math.Max(-1.6f,x-.01f),right=Math.Min(25.5f,x+.01f);
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
            surface.SetNormal(normal);surface.SetUV(new((x+1.6f)/27.1f,angle/Mathf.Pi));surface.AddVertex(p);
        }
        for(int i=0;i<slices.Length-1;i++)for(int ring=0;ring<rings;ring++)
        {
            float a=Mathf.Lerp(from,to,ring/(float)rings),b=Mathf.Lerp(from,to,(ring+1f)/rings),x=slices[i],next=slices[i+1];
            // (c-a) cross (b-a) points outward: Godot's visible front is clockwise.
            Vertex(x,a);Vertex(next,a);Vertex(next,b);Vertex(x,a);Vertex(next,b);Vertex(x,b);
        }
        if(caps)foreach(var (x,normal) in new[]{(-1.6f,Vector3.Left),(25.5f,Vector3.Right)})
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
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
        {
            if((c-a).Cross(b-a).Dot(outward)<0)(b,c)=(c,b);
            var normal=(c-a).Cross(b-a).Normalized();
            foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.X*.2f,p.Z));surface.AddVertex(p);}
        }
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward){Triangle(a,b,c,outward);Triangle(a,c,d,outward);}
        for(int i=0;i<slices.Length-1;i++)
        {
            float x=slices[i],next=slices[i+1];
            if(next<=-1.0f)continue; // The taffrail closes the transom; no broad poop pad.
            if(next<=0||x>=19)
            {
                const int across=32;
                Vector3 Cap(float at,float v)=>TubbyCapPoint(at,v);
                for(int span=0;span<across;span++)
                {
                    float a=-1+2*span/(float)across,b=-1+2*(span+1)/(float)across;
                    Quad(Cap(x,a),Cap(next,a),Cap(next,b),Cap(x,b),Vector3.Up);
                }
                foreach(bool near in new[]{true,false})
                {
                    float angle=near?0:Mathf.Pi,v=near?1:-1;
                    Quad(TubbyPoint(x,angle),TubbyPoint(next,angle),Cap(next,v),Cap(x,v),near?Vector3.Back:Vector3.Forward);
                }
                continue;
            }
            foreach(bool near in new[]{true,false})
            {
                float angle=near?0:Mathf.Pi,edge=near?18.5f:.5f;
                Quad(new(x,TubbySection(x).Sheer,edge),new(next,TubbySection(next).Sheer,edge),TubbyPoint(next,angle),TubbyPoint(x,angle),Vector3.Up);
            }
        }
        surface.Index();return ShipMeshes[key]=surface.Commit();
    }


    private static Vector3 TubbyCapPoint(float at,float v)
    {
        var section=TubbySection(at);float av=Math.Abs(v),ridge=1-(float)(av*Math.Sqrt(av));
        if(at>=19)
        {
            float u=Mathf.Clamp((at-19)/6.5f,0,1);
            return new(at,Mathf.Lerp(1.8f,3.5f,u)+Mathf.Lerp(.6f,1.6f,u)*ridge,9.5f+section.Beam*v);
        }
        return new(at,1.35f+.35f*ridge,9.5f+section.Beam*v);
    }

    private MeshInstance3D ShipBeam(Vector3 a,Vector3 b,float width,float height,Material paint)
    {
        var along=(b-a).Normalized();var across=along.Cross(Vector3.Up).Normalized();var up=across.Cross(along).Normalized();
        var beam=Box(this,(a+b)/2,new((b-a).Length()+.025f,height,width),paint,.035f);
        beam.Basis=new(along,up,across);return beam;
    }

    private static ArrayMesh TubbyGalleryBulwarks()
    {
        const string key="tubby-gallery-bulwarks";if(ShipMeshes.TryGetValue(key,out var cached))return cached;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
        {
            foreach(var triangle in new[]{new[]{a,b,c},new[]{a,c,d}})
            {
                var normal=(triangle[2]-triangle[0]).Cross(triangle[1]-triangle[0]).Normalized();
                if(normal.Dot(outward)<0){(triangle[1],triangle[2])=(triangle[2],triangle[1]);normal=-normal;}
                foreach(var p in triangle){surface.SetNormal(normal);surface.SetUV(new(p.X,p.Y));surface.AddVertex(p);}
            }
        }
        foreach(var(from,to) in new[]{(0f,3f),(16f,19f)})foreach(bool near in new[]{true,false})
        {
            float z=near?18.5f:.5f,sign=near?1:-1;
            for(int i=0;i<16;i++)
            {
                float x=Mathf.Lerp(from,to,i/16f),next=Mathf.Lerp(from,to,(i+1)/16f),bottom=TubbySection(x).Sheer,other=TubbySection(next).Sheer;
                Quad(new(x,bottom,z+sign*.10f),new(next,other,z+sign*.10f),new(next,1.795f,z+sign*.10f),new(x,1.795f,z+sign*.10f),new(0,0,sign));
                Quad(new(x,bottom,z-sign*.10f),new(next,other,z-sign*.10f),new(next,1.795f,z-sign*.10f),new(x,1.795f,z-sign*.10f),new(0,0,-sign));
                Quad(new(x,1.795f,z-.10f),new(next,1.795f,z-.10f),new(next,1.795f,z+.10f),new(x,1.795f,z+.10f),Vector3.Up);
            }
            foreach(float x in new[]{from,to})Quad(new(x,TubbySection(x).Sheer,z-.1f),new(x,TubbySection(x).Sheer,z+.1f),new(x,1.795f,z+.1f),new(x,1.795f,z-.1f),x==from?Vector3.Left:Vector3.Right);
        }
        surface.Index();return ShipMeshes[key]=surface.Commit();
    }

    private static Vector3 TubbyBowspritTip()
    {
        var(sin,cos)=PresentationMath.SinCos(Mathf.Pi/9);
        return new Vector3(25.3f,5.0f,9.5f)+new Vector3(cos,sin,0)*2.2f;
    }

    private static ArrayMesh TubbyTaffrailMesh()
    {
        const string key="tubby-taffrail";if(ShipMeshes.TryGetValue(key,out var cached))return cached;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(float v,bool outer,bool top)=>new(-1.05f-.35f*(1-v*v)+(outer?-.18f:.18f),top?3.15f+.12f*(1-v*v):1.05f,9.5f+8.25f*v);
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

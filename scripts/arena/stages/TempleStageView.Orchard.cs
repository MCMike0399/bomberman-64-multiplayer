using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private Node3D? _orchardMillSails,_orchardBranch;
    private float _orchardClock;
    private StandardMaterial3D? _orchardMillGlass;
    private bool? _orchardWindowNight;

    private void ProcessOrchard(float delta)
    {
        bool night=_lighting?.Label=="Night";
        if(_orchardMillGlass!=null&&_orchardWindowNight!=night)
        {_orchardWindowNight=night;_orchardMillGlass.AlbedoColor=new(night?"e8bd7c":"6d735d");_orchardMillGlass.EmissionEnergyMultiplier=night?1.15f:0;}
        if(!WeatherActive)return;
        _orchardClock+=delta;
        if(_orchardMillSails!=null)_orchardMillSails.Basis=PresentationMath.Rotation(new(0,0,-_orchardClock*.5f));
        if(_orchardBranch!=null)_orchardBranch.Basis=PresentationMath.Rotation(new(0,0,PresentationMath.Sin(_orchardClock*.7f)*.014f),new(1,.85f,1));
    }

    private void BuildOrchardBranch()
    {
        _orchardBranch=new Node3D{Name="OrchardFruitBranch",Position=new(9.5f,-.24f,-.6f),Basis=PresentationMath.Rotation(Vector3.Zero,new(1,.85f,1))};AddChild(_orchardBranch);
        var timber=_art.Toy(new Color("806a49"),.88f);
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] path=[new(-3.2f,0,-.4f),new(-3.1f,.6f,-.4f),new(-2.3f,1.1f,-.25f),
            new(-.9f,1.2f,-.1f),new(.7f,1.1f,0),new(2f,.95f,.05f)];
        const int sides=10;
        (Vector3 P,Vector3 N) At(int section,int side)
        {
            var direction=(path[Math.Min(path.Length-1,section+1)]-path[Math.Max(0,section-1)]).Normalized();
            var across=direction.Cross(Vector3.Forward).Normalized();var other=direction.Cross(across).Normalized();
            var (sin,cos)=PresentationMath.SinCos((side%sides)*Mathf.Tau/sides);
            var normal=across*cos+other*sin;float radius=Mathf.Lerp(.19f,.065f,section/(float)(path.Length-1));
            return (path[section]+normal*radius,normal);
        }
        void Triangle((Vector3 P,Vector3 N) a,(Vector3 P,Vector3 N) b,(Vector3 P,Vector3 N) c)
        {
            if((b.P-a.P).Cross(c.P-a.P).Dot(a.N+b.N+c.N)>0)(b,c)=(c,b);
            foreach(var p in new[]{a,b,c}){surface.SetNormal(p.N);surface.SetUV(new(p.P.X,p.P.Y));surface.AddVertex(p.P);}
        }
        for(int section=0;section<path.Length-1;section++)for(int side=0;side<sides;side++)
        {var a=At(section,side);var b=At(section+1,side);var c=At(section+1,side+1);var d=At(section,side+1);Triangle(a,b,c);Triangle(a,c,d);}
        surface.Index();_orchardBranch.AddChild(new MeshInstance3D{Name="BowedOrchardWood",Mesh=surface.Commit(),MaterialOverride=timber});
        foreach(var (x,y,z,scale) in new[]{(-2.1f,1.23f,-.2f,1f),(-.8f,1.34f,-.12f,.9f),(.55f,1.22f,.02f,.85f),(1.7f,1.08f,.03f,.65f)})
        {
            Bead(_orchardBranch,new(x,y,z),new Vector3(.94f,.66f,.75f)*scale,WindPaint(new Color("6c9251")));
            Bead(_orchardBranch,new(x+.25f,y-.1f,z+.12f),new Vector3(.62f,.54f,.60f)*scale,WindPaint(new Color("87a864")));
            foreach(int side in new[]{-1,1})
                Bead(_orchardBranch,new(x+side*.24f,y-.28f,z+.12f),new(.17f,.19f,.17f),_art.Toy(new Color(side<0?"c88248":"b6a448"),.8f));
        }
    }

    private static ArrayMesh OrchardStandingStoneMesh(float height,Material stone,Material moss)
    {
        const int sides=20;float[] levels=[0,.08f,.70f,.82f,.90f,.97f,1];
        float[] radii=[.43f,.485f,.43f,.49f,.49f,.47f,.46f];
        var body=new SurfaceTool();body.Begin(Mesh.PrimitiveType.Triangles);var shoulder=new SurfaceTool();shoulder.Begin(Mesh.PrimitiveType.Triangles);
        (Vector3 P,Vector3 N) At(int ring,int side)
        {
            var(sin,cos)=PresentationMath.SinCos((side%sides)*Mathf.Tau/sides);
            int previous=Math.Max(0,ring-1),next=Math.Min(levels.Length-1,ring+1);
            var normal=new Vector3(cos*(levels[next]-levels[previous])*height,radii[previous]-radii[next],sin*(levels[next]-levels[previous])*height).Normalized();
            return (new(cos*radii[ring],levels[ring]*height,sin*radii[ring]),normal);
        }
        void Triangle(SurfaceTool target,(Vector3 P,Vector3 N) a,(Vector3 P,Vector3 N) b,(Vector3 P,Vector3 N) c)
        {
            if((b.P-a.P).Cross(c.P-a.P).Dot(a.N+b.N+c.N)>0)(b,c)=(c,b);
            foreach(var p in new[]{a,b,c}){target.SetNormal(p.N);target.SetUV(new(p.P.X,p.P.Y));target.AddVertex(p.P);}
        }
        for(int ring=0;ring<levels.Length-1;ring++)for(int side=0;side<sides;side++)
        {
            var a=At(ring,side);var b=At(ring,side+1);var c=At(ring+1,side+1);var d=At(ring+1,side);
            var target=ring is 2 or 3?shoulder:body;Triangle(target,a,b,c);Triangle(target,a,c,d);
        }
        for(int side=0;side<sides;side++)
            Triangle(body,(new Vector3(0,height,0),Vector3.Up),(At(6,side).P,Vector3.Up),(At(6,side+1).P,Vector3.Up));
        body.Index();shoulder.Index();var mesh=body.Commit();shoulder.Commit(mesh);mesh.SurfaceSetMaterial(0,stone);mesh.SurfaceSetMaterial(1,moss);return mesh;
    }
}

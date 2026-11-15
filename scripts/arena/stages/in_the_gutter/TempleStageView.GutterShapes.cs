using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    // Every owned triangle is emitted clockwise against its declared outward
    // normal. The shape journey audits the actual packed arrays after indexing.
    private static void GutterTriangle(SurfaceTool surface,Vector3 a,Vector3 b,Vector3 c,Vector3 na,Vector3 nb,Vector3 nc)
    {
        if((b-a).Cross(c-a).Dot(na+nb+nc)>=0){(b,c)=(c,b);(nb,nc)=(nc,nb);}
        foreach(var (p,n) in new[]{(a,na),(b,nb),(c,nc)})
        {
            surface.SetNormal(n);surface.SetUV(new(p.X,p.Y));surface.AddVertex(p);
        }
    }

    private static void GutterFlatTriangle(SurfaceTool surface,Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
    {
        var normal=(c-a).Cross(b-a).Normalized();
        if(normal.Dot(outward)<0)normal=-normal;
        GutterTriangle(surface,a,b,c,normal,normal,normal);
    }

    private static MeshInstance3D GutterRing(Node parent,Vector3 position,float radius,float tube,Material material,Vector3? angles=null)
    {
        string key=$"gutter-ring:{radius}:{tube}";
        if(!SceneryMeshes.TryGetValue(key,out var mesh))
        {
            const int major=24,minor=8;
            var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
            (Vector3 Point,Vector3 Normal) At(int i,int j)
            {
                var (sin,cos)=PresentationMath.SinCos(i*Mathf.Tau/major);
                var (st,ct)=PresentationMath.SinCos(j*Mathf.Tau/minor);
                var n=new Vector3(cos*ct,sin*ct,st);
                return (new Vector3(cos*radius,sin*radius,0)+n*tube,n);
            }
            for(int i=0;i<major;i++)for(int j=0;j<minor;j++)
            {
                var a=At(i,j);var b=At(i+1,j);var c=At(i+1,j+1);var d=At(i,j+1);
                GutterTriangle(surface,a.Point,b.Point,c.Point,a.Normal,b.Normal,c.Normal);
                GutterTriangle(surface,a.Point,c.Point,d.Point,a.Normal,c.Normal,d.Normal);
            }
            surface.Index();SceneryMeshes[key]=mesh=surface.Commit();
        }
        var node=new MeshInstance3D{Name="CopperLockRing",Position=position,Mesh=mesh,MaterialOverride=material,
            Basis=PresentationMath.Rotation(angles??Vector3.Zero)};
        parent.AddChild(node);return node;
    }

    private Node3D GutterGear(Node3D parent,Vector3 position,float phase,Material verdigris,Material brass)
    {
        var gear=Prop(parent,"CopperLockDriveGear",position);
        GutterRing(gear,Vector3.Zero,.9f,.17f,brass);
        for(int spoke=0;spoke<3;spoke++)
        {
            var bar=Box(gear,Vector3.Zero,new(1.65f,.18f,.19f),verdigris,.055f);
            bar.Basis=PresentationMath.Rotation(new(0,0,spoke*Mathf.Pi/3));
        }
        for(int tooth=0;tooth<10;tooth++)
        {
            float angle=tooth*Mathf.Tau/10;var (sin,cos)=PresentationMath.SinCos(angle);
            var tip=Box(gear,new(cos*1.07f,sin*1.07f,0),new(.37f,.32f,.24f),brass,.07f);
            tip.Basis=PresentationMath.Rotation(new(0,0,angle));
        }
        RoundedBox(gear,Vector3.Zero,new(.48f,.48f,.35f),brass,.8f);
        _gutterGears.Add((gear,phase));
        return gear;
    }

    private void GutterGantry(Node3D parent,int side,Material verdigris,Material brass,Material cream)
    {
        float x=side<0?.5f:Width-.5f;
        var machine=Prop(parent,side<0?"CopperLockWestMachine":"CopperLockEastMachine",new(x,0,0));
        // Six large cast bars are the gate itself, rather than a thin fence.
        var gate=Prop(machine,"CopperLockGatePanel",Vector3.Zero);
        _gutterGates.Add(gate);
        Box(gate,new(0,.16f,Depth/2f),new(.55f,.32f,Depth-1.6f),verdigris,.11f);
        for(int bar=0;bar<6;bar++)
        {
            float z=1.8f+bar*(Depth-3.6f)/5;
            Box(gate,new(0,1.39f,z),new(.44f,2.22f,.44f),verdigris,.15f).Name="CopperLockFatGateBar";
            Sphere(gate,new(0,2.61f,z),Vector3.One*.58f,brass).Name="CopperLockGateFinial";
        }
        Box(gate,new(0,2.25f,Depth/2f),new(.55f,.34f,Depth-1.6f),verdigris,.11f);
        // A bowed, thick beam makes an independently readable cast gantry.
        var beam=new SurfaceTool();beam.Begin(Mesh.PrimitiveType.Triangles);
        const int segments=12;const float half=.48f;
        Vector3 Point(int step,bool top,bool outside)
        {
            float t=step/(float)segments,z=.65f+t*(Depth-1.3f);
            float y=2.98f+PresentationMath.Sin(t*Mathf.Pi)*.38f+(top?.28f:-.28f);
            return new(outside?half:-half,y,z);
        }
        for(int i=0;i<segments;i++)
        {
            var a=Point(i,false,false);var b=Point(i+1,false,false);var c=Point(i+1,true,false);var d=Point(i,true,false);
            GutterFlatTriangle(beam,a,b,c,Vector3.Left);GutterFlatTriangle(beam,a,c,d,Vector3.Left);
            a=Point(i,false,true);b=Point(i+1,false,true);c=Point(i+1,true,true);d=Point(i,true,true);
            GutterFlatTriangle(beam,a,b,c,Vector3.Right);GutterFlatTriangle(beam,a,c,d,Vector3.Right);
            foreach(bool top in new[]{false,true})
            {
                a=Point(i,top,false);b=Point(i+1,top,false);c=Point(i+1,top,true);d=Point(i,top,true);
                GutterFlatTriangle(beam,a,b,c,top?Vector3.Up:Vector3.Down);GutterFlatTriangle(beam,a,c,d,top?Vector3.Up:Vector3.Down);
            }
        }
        foreach(int i in new[]{0,segments})
        {
            var a=Point(i,false,false);var b=Point(i,false,true);var c=Point(i,true,true);var d=Point(i,true,false);
            var normal=i==0?Vector3.Forward:Vector3.Back;
            GutterFlatTriangle(beam,a,b,c,normal);GutterFlatTriangle(beam,a,c,d,normal);
        }
        beam.Index();machine.AddChild(new MeshInstance3D{Name="CopperLockBowedGantry",Mesh=beam.Commit(),MaterialOverride=verdigris});
        foreach(float z in new[]{.95f,Depth-.95f})
        {
            RoundedBox(machine,new(0,1.45f,z),new(.88f,2.9f,1.05f),cream,.23f).Name="CopperLockGantryPier";
            RoundedBox(machine,new(0,.2f,z),new(.98f,.4f,1.34f),verdigris,.5f);
            Sphere(machine,new(0,3.16f,z),new(1.0f,.65f,1.0f),brass);
        }
        // Offset mechanisms form an asymmetric pair in the side margins, below HUD.
        float gearZ=side<0?7.1f:13.9f,outerX=side*1.82f;
        GutterGear(machine,new(outerX,1.98f,gearZ),side<0?.14f:.7f,verdigris,brass);
        Box(machine,new(outerX,1.98f,gearZ-.26f),new(2.6f,.34f,.6f),verdigris,.11f);
        Box(machine,new(side*.95f,1.95f,gearZ),new(1.7f,.33f,.34f),brass,.1f);
        float counterZ=gearZ+(side<0?2.6f:-2.6f);
        Box(machine,new(outerX,2.5f,counterZ),new(.2f,1.4f,.2f),brass,.07f);
        RoundedBox(machine,new(outerX,1.48f,counterZ),new(.82f,1.14f,.75f),verdigris,.45f).Name="CopperLockCounterweight";
        Box(machine,new(outerX,1.5f,counterZ+.39f),new(.68f,.22f,.08f),brass,.025f);
    }

    private void GutterBollard(int x,int z,Material brick,Material cream,Material brass,Material algae)
    {
        float top=2*HeightScale;
        var post=Prop(this,"CopperLockBollard",new(x+1,0,z+1));
        RoundedBox(post,new(0,(top-.32f)/2,0),new(1.92f,top-.32f,1.84f),brick,.28f).Name="GutterRaisedPost";
        RoundedBox(post,new(0,.16f,0),new(1.99f,.32f,1.99f),algae,.35f);
        RoundedBox(post,new(0,top-.2f,0),new(1.99f,.4f,1.99f),cream,.38f).Name="CopperLockBollardCap";
        // The ring is recessed within the original two-cell footprint.
        GutterRing(post,new(0,top*.62f,.93f),.28f,.09f,brass);
        RoundedBox(post,new(0,top*.62f,.80f),new(.54f,.56f,.15f),brick,.25f);
        AddGutterWake(post,new(0,.07f,0),1.95f);
    }

    private void GutterFishVane(Material brick,Material cream,Material brass,Material dark)
    {
        float x=10.5f,z=10.5f,top=1.1f*HeightScale;
        var post=Prop(this,"CopperLockCenterBollard",new(x,0,z));
        RoundedBox(post,new(0,.28f,0),new(.98f,.56f,.98f),brick,.35f);
        for(int band=0;band<4;band++)
            RoundedBox(post,new(0,.57f+band*.22f,0),new(.32f,.24f,.32f),band%2==0?cream:brass,.75f);
        RoundedBox(post,new(0,top-.07f,0),new(.83f,.14f,.83f),cream,.5f);
        _gutterArrow=Prop(post,"GutterCurrentMarker",new(0,top+.24f,0));
        Sphere(_gutterArrow,new(.04f,0,0),new(.68f,.39f,.21f),brass).Name="CopperLockFishBody";
        Plate(_gutterArrow,new(-.36f,0,0),[new(-.16f,-.2f),new(.12f,0),new(-.16f,.2f)],.14f,brass);
        Plate(_gutterArrow,new(.01f,.17f,0),[new(-.17f,0),new(.12f,0),new(-.08f,.2f)],.1f,brass);
        foreach(int face in new[]{-1,1})
            Sphere(_gutterArrow,new(.25f,.05f,face*.102f),Vector3.One*.065f,dark);
        AddGutterWake(post,new(0,.07f,0),1);
    }

    public void SetGutterCompression(float left,float right)
    {
        if(!_gutter||_gutterGates.Count!=2)return;
        _gutterGates[0].Position=new(Math.Max(0,left-.72f),0,0);
        _gutterGates[1].Position=new(-Math.Max(0,right-.72f),0,0);
    }
}

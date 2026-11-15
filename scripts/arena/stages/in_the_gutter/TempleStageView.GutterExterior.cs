using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildCopperLockEnds(Material brick,Material cream,Material verdigris,Material brass,Material dark)
    {
        // Short cross-channel arms are joined to the body, rather than islets
        // beside a raft. The upstream culvert is the end wall of the west arm.
        foreach(int side in new[]{-1,1})
        {
            float x=side<0?-.6f:Width+.6f;
            RoundedBox(this,new(x,-.7f,Depth/2f),new(2.2f,1.25f,3.8f),brick,.22f).Name="CopperLockChannelStub";
            foreach(int bank in new[]{-1,1})
            {
                RoundedBox(this,new(x,-.17f,Depth/2f+bank*1.7f),new(2.3f,1.2f,.42f),brick,.2f);
                RoundedBox(this,new(x,.43f,Depth/2f+bank*1.7f),new(2.38f,.18f,.5f),cream,.65f);
            }
            AddChild(new MeshInstance3D{Name="CopperLockStubWater",Mesh=GutterPlane(side<0?-1.5f:Width-1,side<0?1:Width+1.5f,Depth/2f-1.48f,Depth/2f+1.48f,6),
                MaterialOverride=_gutterWater,Position=new(0,.035f,0),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        }
        var culvert=Prop(this,"CopperLockInflow",new(-1.7f,-.05f,Depth/2f));
        culvert.Basis=PresentationMath.Rotation(new(0,Mathf.Pi/2,0));
        var arch=new SurfaceTool();arch.Begin(Mesh.PrimitiveType.Triangles);
        const int segments=16;const float outer=1.43f,inner=1.04f,depth=.56f,spring=.75f;
        Vector3 At(int i,float radius,float z)
        {
            var (sin,cos)=PresentationMath.SinCos(i*Mathf.Pi/segments);
            return new(cos*radius,spring+sin*radius,z);
        }
        for(int i=0;i<segments;i++)
        {
            foreach(float z in new[]{-depth,depth})
            {
                var a=At(i,outer,z);var b=At(i+1,outer,z);var c=At(i+1,inner,z);var d=At(i,inner,z);
                var normal=z>0?Vector3.Back:Vector3.Forward;
                GutterFlatTriangle(arch,a,b,c,normal);GutterFlatTriangle(arch,a,c,d,normal);
            }
            foreach(float radius in new[]{outer,inner})
            {
                var a=At(i,radius,-depth);var b=At(i+1,radius,-depth);var c=At(i+1,radius,depth);var d=At(i,radius,depth);
                var normal=(a+b)*.5f-new Vector3(0,spring,0);normal.Z=0;normal=normal.Normalized()*(radius==outer?1:-1);
                GutterFlatTriangle(arch,a,b,c,normal);GutterFlatTriangle(arch,a,c,d,normal);
            }
        }
        arch.Index();culvert.AddChild(new MeshInstance3D{Name="CopperLockCulvertArch",Mesh=arch.Commit(),MaterialOverride=cream});
        foreach(int side in new[]{-1,1})
            RoundedBox(culvert,new(side*(outer+inner)/2,spring/2,0),new(outer-inner,spring,depth*2),cream,.2f);
        var mouth=new List<Vector2>{new(-inner,0),new(inner,0),new(inner,spring)};
        for(int i=1;i<=segments;i++)
        {
            var (sin,cos)=PresentationMath.SinCos(i*Mathf.Pi/segments);
            mouth.Add(new(cos*inner,spring+sin*inner));
        }
        Plate(culvert,new(0,0,-.36f),mouth.ToArray(),.12f,dark).Name="CopperLockCulvertDepth";
        RoundedBox(culvert,new(0,-.15f,.35f),new(3.35f,.3f,2.35f),brick,.2f);
        culvert.AddChild(new MeshInstance3D{Name="CopperLockInflowWater",Mesh=GutterPlane(-1.03f,1.03f,-.23f,1.35f,6),
            MaterialOverride=_gutterWater,Position=new(0,.22f,0),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        GutterRing(culvert,new(0,1.44f,.64f),.24f,.09f,brass);

        var spillway=Prop(this,"CopperLockCurvedSpillway",new(Width+1.25f,0,Depth/2f));
        var waterSurface=new SurfaceTool();waterSurface.Begin(Mesh.PrimitiveType.Triangles);
        var foundation=new SurfaceTool();foundation.Begin(Mesh.PrimitiveType.Triangles);
        const int spillSteps=20;const float spillLength=1.75f,spillWidth=1.42f;
        Vector3 Spill(int step,float z)
        {
            float t=step/(float)spillSteps;
            return new(t*spillLength,.035f-.9f*t*t*(3-2*t),z);
        }
        for(int i=0;i<spillSteps;i++)
        {
            var a=Spill(i,-spillWidth);var b=Spill(i+1,-spillWidth);var c=Spill(i+1,spillWidth);var d=Spill(i,spillWidth);
            GutterFlatTriangle(waterSurface,a,b,c,Vector3.Up);GutterFlatTriangle(waterSurface,a,c,d,Vector3.Up);
            foreach(int side in new[]{-1,1})
            {
                var e=Spill(i,side*(spillWidth+.19f));var f=Spill(i+1,side*(spillWidth+.19f));
                var g=new Vector3(f.X,-1.16f,f.Z);var h=new Vector3(e.X,-1.16f,e.Z);
                GutterFlatTriangle(foundation,e,f,g,new(0,0,side));GutterFlatTriangle(foundation,e,g,h,new(0,0,side));
            }
        }
        waterSurface.Index();foundation.Index();
        spillway.AddChild(new MeshInstance3D{Name="CopperLockCurvedWeirWater",Mesh=waterSurface.Commit(),MaterialOverride=_gutterWater,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        spillway.AddChild(new MeshInstance3D{Name="CopperLockSpillwayCheeks",Mesh=foundation.Commit(),MaterialOverride=cream});
        RoundedBox(spillway,new(.9f,-1.15f,0),new(2.3f,.3f,3.4f),brick,.2f);
    }

    private void BuildCopperLockCoreWater()
    {
        AddChild(new MeshInstance3D{Name="CopperLockOuterWater",Mesh=GutterPlane(-55,Width+55,-55,Depth+55,90),
            MaterialOverride=_gutterWater,Position=new(0,-.96f,0),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        Box(this,new(Width/2f,-3.2f,Depth/2f),new(150,.05f,150),_art.Color(new Color("254c47"),.94f)).Name="CopperLockSeabed";
    }

    private void BuildGutterExterior()
    {
        // Quiet premium water supplies negative space. A single low reed bank
        // beside the inflow grounds the machine; no house/hedge prop perimeter.
        var scenery=_scenery!;
        var bank=_art.Color(new Color("65766a"),.92f);
        var turf=_art.Color(new Color("5e8064"),.94f);
        RoundedBox(scenery,new(-3.3f,-.83f,13.3f),new(3.2f,.75f,5.2f),bank,.7f).Name="CopperLockReedBank";
        Sphere(scenery,new(-3.4f,-.43f,13.4f),new(2.8f,.35f,4.65f),turf);
        var reeds=Prop(scenery,"CopperLockReeds",new(-3.4f,-.2f,13.7f));
        for(int n=0;n<5;n++)
        {
            var leaf=Plate(reeds,new((n%3-1)*.35f,0,(n/3)*.4f),[new(-.09f,0),new(.16f,.65f),new(.27f,1.15f),new(.36f,.74f),new(.14f,0)],.055f,_art.Color(new Color(n%2==0?"507d5e":"79976c"),.93f));
            leaf.Basis=PresentationMath.Rotation(new(0,n*1.7f,0));
        }
        _sceneryMotion.Add(time=>reeds.Basis=PresentationMath.Rotation(new(0,0,PresentationMath.Sin(time*.6f)*.035f)));
    }
}

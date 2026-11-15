using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildCopperLockEnds(Material brick,Material cream,Material verdigris,Material brass,Material dark)
    {
        // One arched inflow and an offset stepped weir form an asymmetric
        // industrial vignette. They belong to the arena body and survive Low.
        var culvert=Prop(this,"CopperLockInflow",new(-2.2f,-.15f,12.3f));
        culvert.Basis=PresentationMath.Rotation(new(0,.6f,0));
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

        var weir=Prop(this,"CopperLockSteppedWeir",new(Width+.18f,0,6.1f));
        for(int step=0;step<3;step++)
        {
            float x=.48f+step*.82f,y=.02f-step*.31f;
            float h=y+1.07f;
            Box(weir,new(x,-1.15f+h/2,0),new(.82f,h,2.9f),brick,.08f);
            Box(weir,new(x,y-.04f,0),new(.86f,.1f,3.06f),cream,.045f);
            weir.AddChild(new MeshInstance3D{Name="CopperLockWeirWater"+step,Mesh=GutterPlane(x-.4f,x+.4f,-1.4f,1.4f,5),
                MaterialOverride=_gutterWater,Position=new(0,y+.035f,0),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
            foreach(int side in new[]{-1,1})
                RoundedBox(weir,new(x,y+.1f,side*1.57f),new(.91f,.36f,.25f),verdigris,.45f);
        }
    }

    private void BuildGutterExterior()
    {
        // Quiet premium water supplies negative space. A single low reed bank
        // beside the inflow grounds the machine; no house/hedge prop perimeter.
        var scenery=_scenery!;
        scenery.AddChild(new MeshInstance3D{Name="CopperLockOuterWater",Mesh=GutterPlane(-55,Width+55,-55,Depth+55,90),
            MaterialOverride=_gutterWater,Position=new(0,-.96f,0),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        Box(scenery,new(Width/2f,-3.2f,Depth/2f),new(150,.05f,150),_art.Color(new Color("254c47"),.94f)).Name="CopperLockSeabed";
        var bank=_art.Color(new Color("65766a"),.92f);
        var turf=_art.Color(new Color("5e8064"),.94f);
        RoundedBox(scenery,new(-4.2f,-.83f,13.1f),new(3.2f,.75f,5.2f),bank,.7f).Name="CopperLockReedBank";
        Sphere(scenery,new(-4.3f,-.43f,13.2f),new(2.8f,.35f,4.65f),turf);
        var reeds=Prop(scenery,"CopperLockReeds",new(-4.3f,-.2f,13.5f));
        for(int n=0;n<5;n++)
        {
            var leaf=Plate(reeds,new((n%3-1)*.35f,0,(n/3)*.4f),[new(-.09f,0),new(.16f,.65f),new(.27f,1.15f),new(.36f,.74f),new(.14f,0)],.055f,_art.Color(new Color(n%2==0?"507d5e":"79976c"),.93f));
            leaf.Basis=PresentationMath.Rotation(new(0,n*1.7f,0));
        }
        _sceneryMotion.Add(time=>reeds.Basis=PresentationMath.Rotation(new(0,0,PresentationMath.Sin(time*.6f)*.035f)));
    }
}

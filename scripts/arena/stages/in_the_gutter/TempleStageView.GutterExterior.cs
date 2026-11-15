using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildCopperLockEnds(Material brick,Material cream,Material verdigris,Material brass,Material dark,Material mosaic)
    {
        // The full gate opening continues into a short real channel. Walls
        // and machine pedestals form one body with the lock, including on Low.
        foreach(int side in new[]{-1,1})
        {
            float x=side<0?-1.45f:Width+1.45f,endX=side<0?-3.35f:Width+3.35f;
            RoundedBox(this,new(x,-.7f,Depth/2f),new(4f,1.25f,Depth-1.4f),brick,.22f).Name="CopperLockChannelStub";
            Box(this,new(x,-.11f,Depth/2f),new(3.85f,.1f,Depth-2.8f),mosaic,.035f);
            foreach(float bankZ in new[]{1.3f,Depth-1.3f})
            {
                RoundedBox(this,new(x,-.17f,bankZ),new(4.0f,1.2f,.58f),brick,.2f);
                RoundedBox(this,new(x,.43f,bankZ),new(4.08f,.18f,.62f),cream,.65f);
            }
            // Low end-wall sections meet the arch/spillway opening directly.
            foreach(int end in new[]{-1,1})
            {
                float segment=(Depth-2.6f-3f)/2,z=end<0?1.3f+segment/2:Depth-1.3f-segment/2;
                RoundedBox(this,new(endX,-.15f,z),new(.8f,1.6f,segment),brick,.18f);
                RoundedBox(this,new(endX,.67f,z),new(.88f,.16f,segment+.08f),cream,.6f);
            }
            float gearZ=Depth/2f;
            RoundedBox(this,new(endX,.15f,gearZ-.65f),new(.95f,2.15f,1.2f),verdigris,.24f).Name="CopperLockAxlePedestal";
            RoundedBox(this,new(endX,.23f,gearZ-2.6f),new(1.15f,1.35f,1.15f),brick,.22f).Name="CopperLockCounterweightSeat";
            GutterWaterPlane(this,side<0?"CopperLockStubWaterWest":"CopperLockStubWaterEast",
                side<0?-3f:Width-1,side<0?1:Width+3f,1.6f,Depth-1.6f,.035f,_gutterWater,14);
        }
        var culvert=Prop(this,"CopperLockInflow",new(-3.35f,-.05f,Depth/2f));
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

        var spillway=Prop(this,"CopperLockCurvedSpillway",new(Width+3f,0,Depth/2f));
        var waterSurface=new SurfaceTool();waterSurface.Begin(Mesh.PrimitiveType.Triangles);
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

        }
        waterSurface.Index();
        spillway.AddChild(new MeshInstance3D{Name="CopperLockCurvedWeirWater",Mesh=waterSurface.Commit(),MaterialOverride=_gutterWater,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        RoundedBox(spillway,new(.9f,-1.15f,0),new(2.3f,.3f,3.4f),brick,.2f);
    }

    private void BuildCopperLockCoreWater()
    {
        // A separate material and physical center keep the darker outer
        // channel distinct. Its local High variant reduces undersampling
        // on cream tower reflections without changing other stages.
        _gutterOceanWater=new ShaderMaterial{Shader=ArtMaterials.GutterOceanShaderFor(GraphicsQuality.Current),RenderPriority=-16};
        _gutterOceanWater.SetShaderParameter("seabed_height",-3.2f);
        _gutterOceanWater.SetShaderParameter("wind_strength",.35f);
        GutterWaterPlane(this,"CopperLockOuterWater",-55,Width+55,-55,Depth+55,-.96f,_gutterOceanWater,90);
        Box(this,new(Width/2f,-3.2f,Depth/2f),new(150,.05f,150),_art.Color(new Color("254c47"),.94f)).Name="CopperLockSeabed";
    }

    private void ApplyGutterOceanLighting()
    {
        if(_gutterOceanWater==null||_lighting==null)return;
        _lighting.ApplyWater(_gutterOceanWater);
        // A darker outer channel separates it from the shallow jade court,
        // including the opaque Low tier. It remains real shared-preset water.
        foreach(string name in new[]{"deep_color","surface_color"})
        {
            Color color=_gutterOceanWater.GetShaderParameter(name).AsColor();
            _gutterOceanWater.SetShaderParameter(name,color.Darkened(.23f));
        }
    }

    private void BuildGutterExterior()
    {
        // The former separate green islet is removed. Copper Lock's setting is
        // now its connected core channel; decorative scenery may remain empty.
    }
}

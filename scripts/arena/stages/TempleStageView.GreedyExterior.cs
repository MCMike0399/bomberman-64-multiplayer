using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    /// <summary>Original Blue Resort-informed scenery outside the authority's arena.
    /// The reservoir, quays and town are decorative, with no collision bodies.</summary>
    private void BuildGreedyExterior()
    {
        _greedyExterior=new Node3D{Name="GreedyExterior"};AddChild(_greedyExterior);
        var grass=_art.GardenSurface(grass:true);
        grass.SetShaderParameter("grass_dark",new Color("4c6255"));
        grass.SetShaderParameter("grass_light",new Color("75876a"));
        var quay=_art.Masonry();
        quay.SetShaderParameter("base_color",new Color("b8ad92"));
        quay.SetShaderParameter("mortar_color",new Color("737768"));
        quay.SetShaderParameter("water_height",GreedyMoatHeight);
        var paving=_art.GardenSurface();paving.SetShaderParameter("stone_color",new Color("b6b8a2"));
        var bed=_art.GardenSurface();bed.SetShaderParameter("stone_color",new Color("738c83"));
        _greedyExteriorMaterials.AddRange([grass,quay,paving,bed]);
        var coping=_art.Color(new("c3bda2"),.9f);
        var slate=_art.Color(new("385e70"),.76f);
        var dark=_art.Color(new("354d4b"));
        var rail=_art.Color(new("587968"),.78f);
        float cx=Width*.5f,cz=Depth*.5f;

        // A complete broad foot meets the moat instead of leaving a floating arena.
        Box(_greedyExterior,new(cx,-1.5f,cz),new(Width+1.2f,1.8f,Depth+1.2f),_greedyMaterials[1]);
        Box(_greedyExterior,new(cx,-.59f,cz),new(Width+1.45f,.10f,Depth+1.45f),coping,.04f);
        Box(_greedyExterior,new(cx,-2.55f,cz),new(160,.3f,160),bed).Name="GreedyReservoirBed";
        _greedyMoat=new MeshInstance3D{Name="GreedyReservoirWater",
            Mesh=new PlaneMesh{Size=new(160,160),SubdivideWidth=160,SubdivideDepth=160},
            MaterialOverride=_art.Water(),Position=new(cx,GreedyMoatHeight,cz),
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
        _greedyExterior.AddChild(_greedyMoat);

        // Four broad banks leave a quiet reservoir band around the fortress.
        // Their top is below every playable floor, including the bottom chamber.
        float x0=-4.2f,x1=Width+4.2f,z0=-4.2f,z1=Depth+4.2f;
        Box(_greedyExterior,new(x0-40,-1.4f,cz),new(80,1.4f,160),grass);
        Box(_greedyExterior,new(x1+40,-1.4f,cz),new(80,1.4f,160),grass);
        Box(_greedyExterior,new(cx,-1.4f,z0-40),new(x1-x0,1.4f,80),grass);
        Box(_greedyExterior,new(cx,-1.4f,z1+40),new(x1-x0,1.4f,80),grass);
        foreach(float x in new[]{x0-.9f,x1+.9f})
        {
            Box(_greedyExterior,new(x,-1.30f,cz),new(1.8f,1.2f,Depth+12),quay);
            Box(_greedyExterior,new(x,-.66f,cz),new(2,.08f,Depth+12),paving,.025f);
        }
        foreach(float z in new[]{z0-.9f,z1+.9f})
        {
            Box(_greedyExterior,new(cx,-1.30f,z),new(Width+8.4f,1.2f,1.8f),quay);
            Box(_greedyExterior,new(cx,-.66f,z),new(Width+8.4f,.08f,2),paving,.025f);
        }

        // Lower causeways belong to the waterside setting, behind the solid arena
        // wall and well below its court. They add no playable exits.
        foreach(int side in new[]{-1,1})
        {
            float x=side<0?-2.55f:Width+2.55f;
            Box(_greedyExterior,new(x,-.78f,cz),new(3.75f,.28f,1.35f),quay);
            Box(_greedyExterior,new(x,-.61f,cz),new(3.85f,.06f,1.40f),paving,.025f);
            foreach(float z in new[]{cz-.63f,cz+.63f})
            {
                Box(_greedyExterior,new(x,-.13f,z),new(3.8f,.07f,.07f),rail);
                for(int post=0;post<5;post++)
                    Box(_greedyExterior,new(x-1.72f+post*.86f,-.35f,z),new(.09f,.57f,.09f),rail,.012f);
            }
        }

        var roof=new CylinderMesh{TopRadius=0,BottomRadius=1.85f,Height=1.35f,RadialSegments=4};
        foreach(var (x,z) in new[]{(-6.4f,1.5f),(Width+6.4f,2.5f),(-6.4f,Depth-3),(Width+6.4f,Depth-2)})
        {
            var tower=new Node3D{Position=new(x,-.62f,z)};_greedyExterior.AddChild(tower);
            Box(tower,new(0,.12f,0),new(2.55f,.24f,2.55f),coping,.06f);
            Box(tower,new(0,1.1f,0),new(2.25f,1.72f,2.25f),quay,.05f);
            Box(tower,new(0,2.02f,0),new(2.6f,.16f,2.6f),coping,.04f);
            tower.AddChild(new MeshInstance3D{Mesh=roof,MaterialOverride=slate,
                Position=new(0,2.775f,0),Rotation=new(0,Mathf.Pi/4,0)});
            foreach(float dx in new[]{-.53f,.53f})
            {
                Box(tower,new(dx,1.1f,1.135f),new(.36f,.59f,.025f),dark,.045f);
                Box(tower,new(dx,.78f,1.16f),new(.48f,.07f,.09f),coping,.018f);
            }
        }

        // Small lawn insets and round trees echo Blue Resort's publisher art.
        foreach(var (x,z) in new[]{(-7.1f,cz+2),(Width+7.1f,cz-2)})
        {
            Cylinder(_greedyExterior,new(x,-.49f,z),1.6f,.18f,coping);
            Cylinder(_greedyExterior,new(x,-.37f,z),1.45f,.12f,grass);
            Cylinder(_greedyExterior,new(x,.04f,z),.15f,.76f,_art.Color(new("77644c")));
            Sphere(_greedyExterior,new(x,.98f,z),new(1.65f,1.8f,1.65f),_art.Color(new("687f5a"),.95f));
            Sphere(_greedyExterior,new(x-.35f,1.2f,z-.12f),new(1.05f,1.25f,1.1f),_art.Color(new("81956a"),.95f));
        }

        // Put the rotating wheel beside the quay, in the water. The fixed mount
        // turns its local XY wheel plane along the canal (YZ in arena space),
        // while the rotor keeps its local Z animation axis. Moving the old wheel
        // outward alone left the paddles sweeping through the solid bank.
        var timber=_art.Color(new("786343"),.85f);var rim=_art.Color(new("aa8b54"),.7f);
        float wheelZ=cz-3;
        var mount=new Node3D{Name="GreedyWaterwheelMount",Position=new(-3.6f,-.25f,wheelZ),Rotation=new(0,Mathf.Pi/2,0)};
        _greedyExterior.AddChild(mount);
        _greedyWaterwheel=new Node3D{Name="GreedyWaterwheel"};mount.AddChild(_greedyWaterwheel);
        // The pedestal sits on the quay; only the stationary shaft crosses its edge.
        Box(_greedyExterior,new(-4.55f,-.40f,wheelZ),new(.42f,.44f,.48f),quay,.035f).Name="GreedyWheelPedestal";
        var shaft=Cylinder(_greedyExterior,new(-4.1f,-.25f,wheelZ),.12f,1.25f,rim);
        shaft.Name="GreedyWheelShaft";shaft.RotationDegrees=new(0,0,90);
        var wheelRim=new TorusMesh{InnerRadius=.91f,OuterRadius=1.06f,Rings=32,RingSegments=8};
        foreach(float zz in new[]{-.22f,.22f})
            _greedyWaterwheel.AddChild(new MeshInstance3D{Mesh=wheelRim,MaterialOverride=rim,Position=new(0,0,zz),RotationDegrees=new(90,0,0)});
        for(int i=0;i<8;i++)
        {
            float angle=i*Mathf.Tau/8;
            var paddle=Box(_greedyWaterwheel,new(Mathf.Cos(angle)*.95f,Mathf.Sin(angle)*.95f,0),new(.42f,.16f,.55f),timber,.02f);
            paddle.Rotation=new(0,0,angle+Mathf.Pi/2);
            if(i<4)Box(_greedyWaterwheel,Vector3.Zero,new(1.9f,.1f,.12f),timber).Rotation=new(0,0,angle);
        }
        var axle=Cylinder(_greedyWaterwheel,Vector3.Zero,.17f,.7f,rim);axle.RotationDegrees=new(90,0,0);
        foreach(var material in _greedyExteriorMaterials)GraphicsQuality.ApplyShaderDetail(material);
    }

    private void UpdateGreedyExteriorQuality()
    {
        foreach(var material in _greedyExteriorMaterials)GraphicsQuality.ApplyShaderDetail(material);
        if(_greedyMoat?.MaterialOverride is ShaderMaterial water)
        {
            water.Shader=ArtMaterials.WaterShaderFor(GraphicsQuality.Current);
            GraphicsQuality.ApplyShaderDetail(water);
            if(_lighting!=null)_lighting.ApplyWater(water);
        }
    }
}

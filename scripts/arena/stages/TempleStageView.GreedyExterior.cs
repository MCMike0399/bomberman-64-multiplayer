using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private StandardMaterial3D? _greedyHouseWindows;
    private bool? _greedyWindowsAtNight;
    private readonly List<(Node3D Float,Vector3 Anchor,float Phase)> _greedyFloats=[];
    private ShaderMaterial? _greedySluicePaint;
    private float _greedyReservoirClock;

    /// <summary>Original Blue Resort-informed scenery outside the authority's arena.
    /// The reservoir, quays and town are decorative, with no collision bodies.</summary>
    private void BuildGreedyExterior()
    {
        _greedyExterior=new Node3D{Name="GreedyExterior"};AddChild(_greedyExterior);
        var quay=_art.Masonry();
        quay.SetShaderParameter("base_color",new Color("b8ad92"));
        quay.SetShaderParameter("mortar_color",new Color("737768"));
        quay.SetShaderParameter("water_height",GreedyMoatHeight);
        var paving=_art.GardenSurface();paving.SetShaderParameter("stone_color",new Color("b6b8a2"));
        var bed=_art.Color(new("254c52"),.94f);
        _greedyExteriorMaterials.AddRange([quay,paving]);
        var coping=_art.Color(new("e3d6b8"),.9f);
        var slate=_art.Color(new("385e70"),.76f);
        _greedyHouseWindows=new StandardMaterial3D {AlbedoColor=new("354d4b"),Roughness=.64f,EmissionEnabled=true};
        var rail=_art.Color(new("587968"),.78f);
        float cx=Width*.5f,cz=Depth*.5f;

        // A complete broad foot meets the moat instead of leaving a floating arena.
        Box(_greedyExterior,new(cx,-1.5f,cz),new(Width+1.2f,1.8f,Depth+1.2f),_greedyMaterials[1]);
        Box(_greedyExterior,new(cx,-.59f,cz),new(Width+1.45f,.10f,Depth+1.45f),coping,.04f);
        Box(_greedyExterior,new(cx,-2.55f,cz),new(160,.3f,160),bed).Name="GreedyReservoirBed";
        _greedyMoat=new MeshInstance3D{Name="GreedyReservoirWater",
            Mesh=PrimitiveTemplates.Get("PlaneMesh",("size",new Vector2(160,160)),("subdivide_width",160),("subdivide_depth",160)),
            MaterialOverride=_art.Water(),Position=new(cx,GreedyMoatHeight,cz),
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
        _greedyExterior.AddChild(_greedyMoat);

        // The existing water sheet now surrounds the whole fort. Short local
        // brick quays support the houses; no endless pale ground hides the lake.
        // Lower causeways belong to the waterside setting, behind the solid arena
        // wall and well below its court. They add no playable exits.
        foreach(int side in new[]{-1,1})
        {
            float x=side<0?-2.8f:Width+2.8f;
            float pathZ=side<0?Depth*.35f:Depth*.68f;
            Box(_greedyExterior,new(x,-.78f,pathZ),new(4.5f,.28f,1.35f),quay);
            Box(_greedyExterior,new(x,-.61f,pathZ),new(4.6f,.06f,1.40f),paving,.025f);
            foreach(float z in new[]{pathZ-.63f,pathZ+.63f})
            {
                Box(_greedyExterior,new(x,-.13f,z),new(4.5f,.07f,.07f),rail);
                for(int post=0;post<5;post++)
                    Box(_greedyExterior,new(x-2.05f+post*1.025f,-.35f,z),new(.09f,.57f,.09f),rail,.012f);
            }
        }

        var roof=PrimitiveTemplates.Get("CylinderMesh",("top_radius",0),("bottom_radius",1.85f),("height",1.35f),("radial_segments",4));
        foreach(var (x,z) in new[]{(-5.4f,Depth*.35f),(Width+4.5f,Depth*.68f)})
        {
            bool hero=x<0;
            Box(_greedyExterior,new(x,-1.08f,z),new(4.0f,.9f,4.2f),_greedyMaterials[1],.15f).Name="GreedyHouseBrickQuay";
            Box(_greedyExterior,new(x,-.59f,z),new(4.1f,.12f,4.3f),coping,.07f);
            var tower=new Node3D{Name=hero?"GreedyCountingHouse":"GreedyQuayHouseEast",Position=new(x,-.62f,z),Scale=Vector3.One*(hero?1.08f:.88f)};_greedyExterior.AddChild(tower);
            Box(tower,new(0,.12f,0),new(2.55f,.24f,2.55f),coping,.06f);
            Box(tower,new(0,1.1f,0),new(2.25f,1.72f,2.25f),quay,.05f);
            Box(tower,new(0,2.02f,0),new(2.6f,.16f,2.6f),coping,.04f);
            tower.AddChild(new MeshInstance3D{Mesh=roof,MaterialOverride=slate,
                Position=new(0,2.775f,0),Rotation=new(0,Mathf.Pi/4,0)});
            if(hero)
            {
                Sphere(tower,new(0,1.15f,1.16f),new(1.42f,1.42f,.12f),_greedyMaterials[5]);
                Sphere(tower,new(0,1.15f,1.225f),new(1.13f,1.13f,.10f),_greedyHouseWindows).Name="GreedyFloatGaugeWindow";
                Box(tower,new(-.16f,1.15f,1.29f),new(.08f,.74f,.035f),coping,.025f);
                var floatNeedle=Box(tower,new(.1f,1.02f,1.29f),new(.49f,.13f,.035f),coping,.035f);
                _greedyFloats.Add((floatNeedle,floatNeedle.Position,1.7f));
            }
            else foreach(float dx in new[]{-.53f,.53f})
            {
                Box(tower,new(dx,1.1f,1.135f),new(.36f,.59f,.025f),_greedyHouseWindows,.045f).Name="GreedyHouseWindow";
                Box(tower,new(dx,.78f,1.16f),new(.48f,.07f,.09f),coping,.018f);
            }
        }

        // Put the rotating wheel beside the quay, in the water. The fixed mount
        // keeps its XY wheel face toward the battle camera. Its short rear pier
        // and shaft connect back to the fort while paddles sweep only open water.
        var timber=_art.Color(new("786343"),.85f);var rim=_art.Color(new("aa8b54"),.7f);
        float wheelZ=Depth*.66f;
        var mount=new Node3D{Name="GreedyWaterwheelMount",Position=new(-3.9f,.05f,wheelZ)};
        _greedyExterior.AddChild(mount);
        _greedyWaterwheel=new Node3D{Name="GreedyWaterwheel",Scale=new(1.32f,1.32f,1)};mount.AddChild(_greedyWaterwheel);
        // The pedestal sits on the quay; only the stationary shaft crosses its edge.
        Box(_greedyExterior,new(-2.25f,-1.3f,wheelZ-.9f),new(4.3f,1.1f,1.0f),_greedyMaterials[1],.10f).Name="GreedyMachineQuay";
        Box(_greedyExterior,new(-3.9f,-.4f,wheelZ-.9f),new(.5f,1.5f,.7f),quay,.035f).Name="GreedyWheelPedestal";
        var shaft=Cylinder(_greedyExterior,new(-3.9f,.05f,wheelZ-.45f),.12f,1.25f,rim);
        shaft.Name="GreedyWheelShaft";shaft.RotationDegrees=new(90,0,0);
        var wheelRim=PrimitiveTemplates.Get("TorusMesh",("inner_radius",.91f),("outer_radius",1.06f),("rings",32),("ring_segments",8));
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
        BuildGreedySluice(wheelZ,quay,coping);
        int floatOrdinal=0;
        foreach(var (x,z,phase) in new[]{(-3.0f,Depth*.51f,.2f),(-3.4f,Depth*.57f,2.0f),(Width+3.2f,Depth*.43f,4.1f)})
        {
            var node=Prop(_greedyExterior,"GreedyReservoirFloat"+floatOrdinal++,new(x,GreedyMoatHeight+.1f,z));
            Sphere(node,Vector3.Zero,new(.36f,.28f,.36f),coping);
            Sphere(node,new(0,.15f,0),new(.19f,.18f,.19f),rim);
            _greedyFloats.Add((node,node.Position,phase));
        }
        foreach(var material in _greedyExteriorMaterials)GraphicsQuality.ApplyShaderDetail(material);
        UpdateGreedyWindowLighting();
    }

    private void BuildGreedySluice(float wheelZ,Material quay,Material coping)
    {
        var root=Prop(_greedyExterior!,"GreedyWheelSluice",new(-3.9f,0,wheelZ-.15f));
        RoundedBox(root,new(3.12f,.83f,-.9f),new(.5f,.85f,1.0f),quay,.18f);
        RoundedBox(root,new(1.5f,1.25f,-.9f),new(3.8f,.22f,.86f),coping,.17f);
        RoundedBox(root,new(0,1.25f,-.65f),new(.64f,.22f,.4f),coping,.10f);
        _greedySluicePaint=new ShaderMaterial{Shader=StageShader(GreedySluiceShader)};
        _greedyExteriorMaterials.Add(_greedySluicePaint);
        Box(root,new(0,0,.02f),new(.32f,2.5f,.64f),_greedySluicePaint,.07f).Name="GreedySluiceWater";
    }

    private void ProcessGreedyReservoir(float delta)
    {
        if(!WeatherActive)return;
        _greedyReservoirClock+=delta;
        foreach(var(node,rest,phase) in _greedyFloats)
            node.Position=rest+Vector3.Up*(PresentationMath.Sin(_greedyReservoirClock*.65f+phase)*.045f);
        _greedySluicePaint?.SetShaderParameter("water_clock",_greedyReservoirClock);
    }

    private const string GreedySluiceShader="""
shader_type spatial;
render_mode diffuse_burley, blend_mix;
uniform float water_clock=0.0;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
void fragment(){
    float ribbon=.5+.5*sin(UV.y*11.0+water_clock*2.1);
    ALBEDO=mix(vec3(.14,.44,.44),vec3(.61,.82,.72),smoothstep(.65,.95,ribbon)*.38);
    ROUGHNESS=.32;SPECULAR=.45;ALPHA=.74;
}
""";

    private void UpdateGreedyWindowLighting()
    {
        bool night=_lighting?.Label=="Night";
        if(_greedyHouseWindows is null||_greedyWindowsAtNight==night)return;
        _greedyWindowsAtNight=night;
        _greedyHouseWindows.AlbedoColor=new(night?"e8b975":"354d4b");
        _greedyHouseWindows.Emission=new("efb263");
        _greedyHouseWindows.EmissionEnergyMultiplier=night?1.2f:0;
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

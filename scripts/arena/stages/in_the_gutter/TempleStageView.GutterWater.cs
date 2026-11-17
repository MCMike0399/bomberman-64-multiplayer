using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private ShaderMaterial? _gutterFoamMaterial;
    private readonly List<OmniLight3D> _gutterWorkLights=[];

    private void AddGutterFoam(Node3D parent,Vector3 position,float footprint)
    {
        if(_gutterFoamMaterial is null)
        {
            _gutterFoamMaterial=new ShaderMaterial{Shader=StageShader(GutterFoamShader),RenderPriority=-14};
            _gutterMaterials.Add(_gutterFoamMaterial);
        }
        if(!SceneryMeshes.TryGetValue("copper-lock-contact-foam",out var mesh))
            SceneryMeshes["copper-lock-contact-foam"]=mesh=GutterPlane(-.7f,.7f,-.7f,.7f,1);
        parent.AddChild(new MeshInstance3D{Name="CopperLockWaterlineFoam",Mesh=mesh,MaterialOverride=_gutterFoamMaterial,
            Position=position,Scale=new(footprint,1,footprint),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }

    private const string GutterFoamShader="""
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never;
void fragment(){
    vec2 p=abs((UV-.5)*1.4)-vec2(.36);
    float edge=length(max(p,vec2(0.0)))+min(max(p.x,p.y),0.0)-.10;
    float ring=(1.0-smoothstep(.015,.075,abs(edge)));
    ALBEDO=vec3(.72,.87,.76);
    ALPHA=ring*.24;
}
""";
    private void AddGutterWake(Node3D parent,Vector3 position,float scale)
    {
        if(!SceneryMeshes.TryGetValue("copper-lock-wake",out var mesh))
            SceneryMeshes["copper-lock-wake"]=mesh=GutterPlane(-2.4f,2.4f,-1.5f,1.5f,1);
        parent.AddChild(new MeshInstance3D{Name="CopperLockFoamWake",Mesh=mesh,MaterialOverride=_gutterWakeMaterial,
            Position=position,Scale=new(scale,1,scale),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }

    private void BuildGutterLeaves()
    {
        var amber=_art.Toy(new Color("c19b51"),.89f);
        int ordinal=0;
        foreach(var (x,z,phase) in new[]{(6.4f,2.7f,.2f),(14.5f,8.7f,1.4f),(5.7f,12.1f,2.7f),(12.1f,18.2f,4.1f)})
        {
            var leaf=Prop(this,"CopperLockDriftingLeaf"+ordinal++,new(x,.078f,z));
            Plate(leaf,Vector3.Zero,[new(-.16f,0),new(-.03f,.095f),new(.18f,0),new(.02f,-.08f)],.014f,amber);
            _gutterLeaves.Add((leaf,phase));
        }
    }

    private const string GutterWakeShader="""
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never;
uniform float current_clock=0.0;
uniform float current=0.0;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
void fragment(){
    vec2 p=(UV-.5)*vec2(4.8,3.0);
    float d=p.x*sign(current)-.3;
    if(d<0.0||d>.78)discard;
    float bend=.39*exp(-d*1.65)+.12;
    float curved=abs(abs(p.y)-bend);
    float stroke=1.0-smoothstep(.025,.19,curved);
    float fade=smoothstep(.0,.18,d)*(1.0-smoothstep(.35,.78,d));
    float drift=.88+.12*sin(current_clock*.8-d*3.0);
    ALBEDO=vec3(.72,.87,.73);
    ALPHA=stroke*fade*drift*.24*min(1.0,abs(current))*quality_detail;
}
""";
}

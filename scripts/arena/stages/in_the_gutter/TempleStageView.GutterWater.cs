using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
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
        foreach(var (x,z,phase) in new[]{(6.4f,2.7f,.2f),(14.5f,8.7f,1.4f),(5.7f,12.1f,2.7f),(12.1f,18.2f,4.1f)})
        {
            var leaf=Prop(this,"CopperLockDriftingLeaf",new(x,.078f,z));
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
    float d=p.x*sign(current)-.46;
    if(d<-.12||d>1.6)discard;
    float spread=.42+.25*d+.035*sin(d*3.0-current_clock*1.4);
    float curved=abs(abs(p.y)-spread);
    float stroke=1.0-smoothstep(.035,.14,curved);
    float fade=smoothstep(-.12,.16,d)*(1.0-smoothstep(.95,1.6,d));
    ALBEDO=vec3(.76,.91,.77);
    ALPHA=stroke*fade*.23*min(1.0,abs(current));
}
""";
}

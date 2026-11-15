using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    private static Shader? _observationGlassShader;
    public ShaderMaterial ObservationGlass()
    {
        var material=new ShaderMaterial{Shader=_observationGlassShader??=new Shader{Code=ObservationGlassShader}};
        GraphicsQuality.ApplyShaderDetail(material);return material;
    }

    // Original coated violet observation glass. A translucent, depth-writing
    // floor reveals real marine diorama geometry underneath without refraction.
    private const string ObservationGlassShader="""
shader_type spatial;
render_mode diffuse_burley, depth_draw_always;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
uniform float ambient_clock=0.0;
varying vec3 world;
"""+"\n"+IntegerNoiseHash+"\n"+"""
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
void fragment(){
 vec2 p=world.xz;
 float wash=.5+.5*sin(p.x*.23+p.y*.17);
 vec3 violet=mix(vec3(.20,.16,.38),vec3(.36,.30,.56),wash);
 // Broad diagonal bands retain the guide's glass identity at the battle camera.
 float streak=pow(max(0.0,sin((p.x+p.y*.7)*.55)),18.0);
 ALBEDO=violet+vec3(.10,.12,.16)*streak;
 if(quality_detail>.5){
   float pane=hash(floor(p/3.0));
   ALBEDO*=.97+pane*.06;
   vec2 seam=abs(fract(p/3.0)-.5);
   float joint=smoothstep(.495-fwidth(p.x/3.0),.5,max(seam.x,seam.y));
   ALBEDO=mix(ALBEDO,vec3(.46,.50,.64),joint*.16);
 }
 if(quality_premium>.5){
   float light=.5+.5*sin(p.x*.52-p.y*.31+ambient_clock*.24);
   ALBEDO+=vec3(.035,.055,.065)*light*streak;
 }
 ALPHA=.79;
 METALLIC=.08;ROUGHNESS=.34;
 CLEARCOAT=.22;CLEARCOAT_ROUGHNESS=.5;
}
""";
}

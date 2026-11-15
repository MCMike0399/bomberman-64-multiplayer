using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    private static Shader? _observationGlassShader;
    public ShaderMaterial ObservationGlass()
    {
        var material=new ShaderMaterial{Shader=_observationGlassShader??=new Shader{Code=ObservationGlassShader},RenderPriority=-8};
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
uniform vec4 glass_bounds=vec4(3.0,3.0,18.0,18.0);
varying vec3 world;
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
void fragment(){
 vec2 p=world.xz;
 float edge=min(min(p.x-glass_bounds.x,glass_bounds.z-p.x),min(p.y-glass_bounds.y,glass_bounds.w-p.y));
 float depth=smoothstep(.0,4.0,edge);
 vec3 violet=mix(vec3(.095,.12,.30),vec3(.028,.019,.17),depth);
 // Two broad, quiet diagonal sheen bands: material identity survives on Low.
 float sheen=pow(max(0.0,sin((p.x+p.y*.78)*.48)),14.0);
 ALBEDO=violet+vec3(.065,.085,.16)*sheen;
 // Authored soft beacon reflections respond to the actual viewing direction.
 // They are shallow paint in the coated glass, not a new reflective scene pass.
 vec2 centre=(glass_bounds.xy+glass_bounds.zw)*.5;
 vec2 towers[4]={centre+vec2(0,-4),centre+vec2(-4,0),centre+vec2(4,0),centre+vec2(0,4)};
 vec3 camera=INV_VIEW_MATRIX[3].xyz;
 for(int i=0;i<4;i++){
   vec2 ray=camera.xz-towers[i];
   vec2 direction=ray/max(length(ray),.01);
   vec2 delta=p-towers[i];
   float along=dot(delta,direction),across=abs(dot(delta,vec2(-direction.y,direction.x)));
   float reflection_length=max(.1,2.835*length(ray)/max(camera.y,.2));
   float reflection=(1.0-smoothstep(.22,.48,across))*smoothstep(.04,.2,along)*(1.0-smoothstep(reflection_length*.65,reflection_length,along));
   ALBEDO=mix(ALBEDO,vec3(.14,.058,.025),reflection*.17);
 }
 if(quality_premium>.5){
   float slow_light=.5+.5*sin(p.x*.31-p.y*.19+ambient_clock*.18);
   ALBEDO+=vec3(.012,.018,.028)*slow_light*sheen;
 }
 ALPHA=mix(.93,.84,depth);
 METALLIC=.10;ROUGHNESS=.30;
 CLEARCOAT=.35;CLEARCOAT_ROUGHNESS=.42;
}
""";
}

using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    private static Shader? _hollowSurfaceShader,_hollowGrassShader;
    public ShaderMaterial HollowSurface(int kind)
    {
        var material=new ShaderMaterial{Shader=_hollowSurfaceShader??=new Shader{Code=HollowSurfaceShader}};
        material.SetShaderParameter("surface_kind",kind);GraphicsQuality.ApplyShaderDetail(material);return material;
    }
    public ShaderMaterial HollowGrass()=>new(){Shader=_hollowGrassShader??=new Shader{Code=HollowGrassShader}};

    private const string HollowSurfaceShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform int surface_kind=0;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
varying vec3 world;
varying vec3 world_normal;
"""+"\n"+IntegerNoiseHash+"\n"+"""
float cloud(vec2 p){
 vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
 return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),f.x),f.y);
}
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1)).xyz;world_normal=normalize(MODEL_NORMAL_MATRIX*NORMAL);}
void fragment(){
 float wash=cloud(world.xz*.48);
 if(surface_kind==0){
   ALBEDO=mix(vec3(.24,.075,.043),vec3(.43,.19,.085),wash);
   // Broad depositional bands belong to the cut faces, never to the ramps.
   if(abs(world_normal.y)<.35){
     float level=world.y+1.2+.13*sin(world.x*.6)+.06*sin(world.z*.4);
     float layer=floor(level/.75);
     ALBEDO*=mix(.76,1.12,fract(layer*.39));
     float seam=1.0-smoothstep(.0,.085,abs(fract(level/.75)-.5));
     ALBEDO=mix(ALBEDO,vec3(.52,.29,.14),seam*.22);
   }
 }else if(surface_kind==1){
   ALBEDO=mix(vec3(.045,.12,.035),vec3(.12,.23,.065),wash);
 }else if(surface_kind==2){
   ALBEDO=mix(vec3(.24,.30,.29),vec3(.40,.46,.41),wash);
   float cap=smoothstep(.85,1.3,world.y)*smoothstep(.15,.65,world_normal.y);
   ALBEDO=mix(ALBEDO,vec3(.16,.26,.075),cap*.8);
   if(quality_premium>.5){
     float lichen=smoothstep(.68,.82,cloud(world.xz*5.0+world.y));
     ALBEDO=mix(ALBEDO,vec3(.53,.58,.31),lichen*.22);
   }
 }else{
   ALBEDO=mix(vec3(.16,.23,.19),vec3(.27,.33,.25),wash);
 }
 ROUGHNESS=.94;SPECULAR=.2;
}
""";

    private const string HollowGrassShader="""
shader_type spatial;
render_mode diffuse_burley, cull_disabled;
uniform vec4 paint : source_color=vec4(.306,.475,.247,1);
uniform vec4 tip_paint : source_color=vec4(.70,.64,.32,1);
uniform float ambient_clock=0.0;
varying float gust;
void vertex(){
 vec3 world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
 gust=.55+.25*sin(world.x*.63+world.z*.37-ambient_clock*1.65)+.2*sin(world.x*.27-world.z*.41-ambient_clock*.73);
 float flutter=sin(world.x*3.1+world.z*2.7-ambient_clock*4.2);
 vec3 bend=vec3(.13*gust+.025*flutter,0,.065*gust)*UV.y*UV.y;
 VERTEX+=inverse(mat3(MODEL_MATRIX))*bend;
}
void fragment(){
 float tip=smoothstep(.55,1.0,UV.y)*.43;
 ALBEDO=mix(paint.rgb,tip_paint.rgb,tip)*COLOR.rgb*mix(.70,1.04,UV.y)*(1.0+gust*.05);
 ROUGHNESS=.92;SPECULAR=.15;
}
""";
}

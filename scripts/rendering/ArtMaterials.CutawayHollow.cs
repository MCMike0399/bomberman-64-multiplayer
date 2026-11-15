using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    private static Shader? _hollowSurfaceShader,_hollowGrassShader;
    public ShaderMaterial HollowSurface(int kind)
    {
        var material=new ShaderMaterial{Shader=_hollowSurfaceShader??=new Shader{Code=HollowSurfaceShader}};
        material.SetShaderParameter("surface_kind",kind);
        foreach(var(name,color)in new[]{("topsoil","4a2f22"),("loam","a8623c"),("clay","d2a373"),("base_soil","6b4a3a"),
            ("valley_turf","426d35"),("plateau_turf","789372")})material.SetShaderParameter(name,new Color(color));
        GraphicsQuality.ApplyShaderDetail(material);return material;
    }
    public ShaderMaterial HollowGrass()=>new(){Shader=_hollowGrassShader??=new Shader{Code=HollowGrassShader}};

    private const string HollowSurfaceShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform int surface_kind=0;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
uniform vec4 topsoil : source_color;
uniform vec4 loam : source_color;
uniform vec4 clay : source_color;
uniform vec4 base_soil : source_color;
uniform vec4 valley_turf : source_color;
uniform vec4 plateau_turf : source_color;
uniform vec4 stone_tint : source_color=vec4(1);
varying vec3 world;
varying vec3 arena;
varying vec3 world_normal;
"""+"\n"+IntegerNoiseHash+"\n"+"""
float cloud(vec2 p){
 vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
 return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),f.x),f.y);
}
float cut_top(float x){
 if(x<1.0)return 2.6+.9775*smoothstep(-1.5,1.0,x);
 if(x<1.5)return mix(3.5775,2.85,smoothstep(1.0,1.5,x));
 if(x<3.0)return 2.85;
 if(x<5.0)return (5.0-x)*1.35+.15;
 if(x<16.0)return .15;
 if(x<18.0)return (x-16.0)*1.35+.15;
 if(x<19.5)return 2.85;
 if(x<20.0)return mix(2.85,3.5775,smoothstep(19.5,20.0,x));
 return 2.6+.9775*(1.0-smoothstep(20.0,22.5,x));
}
void vertex(){arena=VERTEX;world=(MODEL_MATRIX*vec4(VERTEX,1)).xyz;world_normal=normalize(MODEL_NORMAL_MATRIX*NORMAL);}
void fragment(){
 float wash=cloud(arena.xz*.42);
 if(surface_kind==0){
   // Four broad deposits, with no repeated grain or thin linework.
   ALBEDO=loam.rgb*(.94+wash*.12);
   float clay_band=smoothstep(-1.34,-1.27,arena.y)*(1.0-smoothstep(-.83,-.76,arena.y));
   ALBEDO=mix(ALBEDO,clay.rgb,clay_band);
   ALBEDO=mix(ALBEDO,base_soil.rgb,1.0-smoothstep(-1.38,-1.30,arena.y));
   ALBEDO=mix(ALBEDO,topsoil.rgb,smoothstep(cut_top(arena.x)-.32,cut_top(arena.x)-.26,arena.y));
 }else if(surface_kind==1){
   float path_distance=min(abs(arena.x-2.0),abs(arena.x-19.0));
   float worn=1.0-smoothstep(.5,.72,path_distance+(cloud(vec2(arena.z*.8,2.0))-.5)*.24);
   bool ramp=arena.x>3.0&&arena.x<5.0||arena.x>16.0&&arena.x<18.0;
   if(ramp){
     float down=arena.x<10.5?arena.x-3.0:18.0-arena.x;
     float reach=.3+cloud(vec2(arena.z*.65,5.0))*.5;
     float turf=1.0-smoothstep(reach-.12,reach+.12,down);
     ALBEDO=mix(loam.rgb*.96,valley_turf.rgb,turf);
   }else ALBEDO=mix(valley_turf.rgb,loam.rgb*.95,worn*.88);
   ALBEDO*=.94+wash*.12;
 }else if(surface_kind==2){
   ALBEDO=mix(vec3(.25,.31,.30),vec3(.40,.46,.41),wash)*stone_tint.rgb;
   float cap=smoothstep(.85,1.3,arena.y)*smoothstep(.15,.65,world_normal.y);
   ALBEDO=mix(ALBEDO,vec3(.16,.26,.075),cap*.8);
 }else if(surface_kind==4){
   ALBEDO=plateau_turf.rgb*(.91+wash*.17);
 }else if(surface_kind==5)ALBEDO=valley_turf.rgb*(.93+wash*.14);
 else ALBEDO=mix(vec3(.13,.19,.15),vec3(.23,.29,.20),wash);
 ROUGHNESS=.95;SPECULAR=.18;
}
""";

    private const string HollowGrassShader="""
shader_type spatial;
render_mode diffuse_burley, cull_disabled;
uniform vec4 paint : source_color=vec4(.306,.475,.247,1);
uniform vec4 tip_paint : source_color=vec4(.77,.71,.42,1);
uniform float ambient_clock=0.0;
varying float gust;
void vertex(){
 vec3 world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
 gust=.55+.25*sin(world.x*.63+world.z*.37-ambient_clock*1.65)+.2*sin(world.x*.27-world.z*.41-ambient_clock*.73);
 float flutter=sin(world.x*3.1+world.z*2.7-ambient_clock*4.2);
 vec3 bend=vec3(.13*gust+.025*flutter,0,.065*gust)*UV.y*UV.y*COLOR.a;
 VERTEX+=inverse(mat3(MODEL_MATRIX))*bend;
}
void fragment(){
 float tip=smoothstep(.32,.82,UV.y)*.68;
 ALBEDO=mix(paint.rgb,tip_paint.rgb,tip)*COLOR.rgb*mix(.70,1.04,UV.y)*(1.0+gust*.05);
 ROUGHNESS=.92;SPECULAR=.15;
}
""";
}

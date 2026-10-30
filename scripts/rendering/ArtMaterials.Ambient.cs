using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    private static Shader? _foliageShader,_planetShader,_nebulaShader;

    /// <summary>Opaque painted foliage. The owner supplies a pausable clock; roots stay fixed.</summary>
    public ShaderMaterial Foliage(Color color,bool blades=false)
    {
        var material=new ShaderMaterial{Shader=_foliageShader??=new Shader{Code=FoliageShader}};
        material.SetShaderParameter("paint",color);
        material.SetShaderParameter("blades",blades);
        return material;
    }

    public ShaderMaterial Planet(Color dark,Color light,bool moon=false)
    {
        var material=new ShaderMaterial{Shader=_planetShader??=new Shader{Code=PlanetShader}};
        material.SetShaderParameter("dark_paint",dark);material.SetShaderParameter("light_paint",light);
        material.SetShaderParameter("moon",moon);return material;
    }

    public ShaderMaterial Nebula()=>new(){Shader=_nebulaShader??=new Shader{Code=NebulaShader}};

    private const string FoliageShader="""
shader_type spatial;
render_mode diffuse_burley, cull_disabled;
uniform vec4 paint : source_color;
uniform bool blades=false;
uniform float ambient_clock=0.0;
varying float gust;
void vertex(){
 vec3 world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
 float wave=sin(world.x*.63+world.z*.37-ambient_clock*1.65);
 gust=.55+.25*wave+.2*sin(world.x*.27-world.z*.41-ambient_clock*.73);
 float flutter=sin(world.x*3.1+world.z*2.7-ambient_clock*4.2);
 float weight=blades?UV.y*UV.y:1.0;
 // World-space gusts connect separate plants. Convert back to each instance's
 // basis so MultiMesh yaw/scale never rotates the prevailing wind direction.
 vec3 bend=vec3(.13*gust+.025*flutter,0.0,.065*gust)*weight;
 VERTEX+=inverse(mat3(MODEL_MATRIX))*bend;
}
void fragment(){
 float tip=blades?UV.y:clamp(UV.y,.0,1.0);
 ALBEDO=paint.rgb*COLOR.rgb*mix(.78,1.15,tip)*(1.0+gust*.07);
 ROUGHNESS=.88;
}
""";

    // Painted astronomical miniatures: broad cloud bands, crater rings and a
    // fixed illustrated terminator, independent of the arena's four times of day.
    private const string PlanetShader="""
shader_type spatial;
render_mode unshaded, fog_disabled;
uniform vec4 dark_paint : source_color;
uniform vec4 light_paint : source_color;
uniform bool moon=false;
uniform float ambient_clock=0.0;
varying vec3 sphere_normal;
""" + "\n" + IntegerNoiseHash + "\n" + """
void vertex(){sphere_normal=normalize(MODEL_NORMAL_MATRIX*NORMAL);}
void fragment(){
 vec2 p=UV+vec2(ambient_clock*.003,0.0);
 float stripe=sin(p.y*44.0+sin(p.x*25.13274+p.y*13.0)*.45);
 float band=smoothstep(-.5,.7,stripe)*.7+sin(p.y*23.0)*.15;
 if(moon){
   vec2 q=p*vec2(19.0,10.0),cell=floor(q);
   vec2 centre=vec2(hash(cell),hash(cell+4.3))*.4+.3;
   float d=length(fract(q)-centre);
   float crater=(1.0-smoothstep(.16,.26,d))*.7;
   float rim=smoothstep(.23,.27,d)*(1.0-smoothstep(.27,.33,d))*.3;
   band=.65-crater+rim;
 }
 float sun=dot(normalize(sphere_normal),normalize(vec3(-.6,.65,.55)));
 float light=smoothstep(-.25,.65,sun);
 vec3 color=mix(dark_paint.rgb,light_paint.rgb,clamp(band,.0,1.0));
 float limb=pow(1.0-max(dot(NORMAL,VIEW),0.0),3.0);
 ALBEDO=color*mix(.18,1.05,light)+light_paint.rgb*limb*.18;
}
""";

    private const string NebulaShader="""
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled;
uniform float ambient_clock=0.0;
uniform vec2 stage_center=vec2(9.5);
varying vec3 world;
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
void fragment(){
 vec2 p=UV*7.0;
 float cloud=sin(p.x*.8+p.y*.55+sin(p.y*1.9)*.8+ambient_clock*.008);
 float veil=smoothstep(.15,1.0,cloud)*smoothstep(-.5,1.0,sin(p.y*1.3-p.x*.45));
 // Fade to the black void well before the finite backdrop ends. Neither a
 // mesh edge nor the far clip should make a horizon across the dark setting.
 float envelope=1.0-smoothstep(36.0,68.0,length(world.xz-stage_center));
 ALBEDO=mix(vec3(.003,.005,.014),vec3(.033,.018,.07),veil)*envelope;
}
""";
}

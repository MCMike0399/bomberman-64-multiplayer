using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    private static Shader? _foliageShader,_planetShader,_planetRingShader,_nebulaShader;

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

    public ShaderMaterial PlanetRings()=>new(){Shader=_planetRingShader??=new Shader{Code=PlanetRingShader}};

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
varying vec3 local_sphere;
""" + "\n" + IntegerNoiseHash + "\n" + """
void vertex(){
 sphere_normal=normalize(MODEL_NORMAL_MATRIX*NORMAL);
 local_sphere=normalize(VERTEX);
}
void fragment(){
 vec3 n=normalize(local_sphere);
 // Seamless low-frequency eddies break up broad brush-painted latitudes.
 // Small differences in band width keep the worlds from reading as beach balls.
 float drift=ambient_clock*.014;
 float eddy=sin(n.x*7.0+n.z*5.0+drift)*sin(n.z*8.0-n.y*3.0-drift*.6);
 float latitude=n.y*17.0+eddy*.85+sin(n.y*9.0)*.65;
 float band=.48+sin(latitude)*.18+sin(latitude*2.1+eddy)*.075;
 band+=sin(n.x*16.0+n.z*12.0+n.y*28.0+drift)*.025;
 if(moon){
   vec2 q=UV*vec2(11.0,6.0),cell=floor(q);
   vec2 centre=vec2(hash(cell),hash(cell+4.3))*.4+.3;
   float d=length(fract(q)-centre);
   float crater=(1.0-smoothstep(.12,.26,d))*.24;
   float rim=smoothstep(.21,.27,d)*(1.0-smoothstep(.27,.35,d))*.12;
   float maria=sin(n.x*5.0+n.z*3.0)*sin(n.z*4.0+n.y*6.0)*.13;
   band=.64+maria-crater+rim;
 }
 float sun=dot(normalize(sphere_normal),normalize(vec3(-.7,.55,.5)));
 float light=smoothstep(-.32,.85,sun);
 vec3 color=mix(dark_paint.rgb,light_paint.rgb,clamp(band,.0,1.0));
 float limb=pow(1.0-max(dot(NORMAL,VIEW),0.0),3.0);
 ALBEDO=color*mix(.12,.94,light)+light_paint.rgb*limb*.065;
}
""";

    private const string PlanetRingShader="""
shader_type spatial;
render_mode unshaded, fog_disabled, cull_disabled;
void fragment(){
 float radius=length(UV);
 float grain=.5+.5*sin(radius*95.0+sin(radius*31.0)*.45);
 float broad=.5+.5*sin(radius*8.5);
 vec3 paint=mix(vec3(.19,.17,.24),vec3(.43,.36,.30),broad*.6+grain*.15);
 // The globe casts a quiet painted shadow across the far ring. The actual
 // opaque globe supplies depth occlusion; no transparent halo or extra light.
 vec3 p=vec3(UV.x,0.0,UV.y),sun=normalize(vec3(-.7,.55,.5));
 float axial=dot(p,sun);
 float distance_to_axis=length(p-sun*axial);
 float shadow=(1.0-smoothstep(2.85,3.2,distance_to_axis))*(1.0-smoothstep(-.25,.1,axial));
 ALBEDO=paint*mix(1.0,.24,shadow);
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
 vec2 p=(world.xz-stage_center)*.035;
 float drift=ambient_clock*.003;
 float cloud=sin(p.x*.8+p.y*.55+sin(p.y*1.9)*.8+drift);
 float fold=sin(p.y*2.7-p.x*.6+sin(p.x*1.7)*.55);
 float veil=smoothstep(-.1,1.0,cloud)*smoothstep(-.6,1.0,fold);
 float dust=.5+.5*sin(p.x*6.0+p.y*4.0+sin(p.y*5.0)*.7);
 // Fade to the black void well before the finite backdrop ends. Neither a
 // mesh edge nor the far clip should make a horizon across the dark setting.
 float envelope=1.0-smoothstep(36.0,68.0,length(world.xz-stage_center));
 vec3 tint=mix(vec3(.014,.021,.035),vec3(.036,.019,.053),.5+.5*sin(p.x-p.y));
 ALBEDO=(vec3(.0012,.002,.006)+tint*veil*(.65+.35*dust))*envelope;
}
""";
}

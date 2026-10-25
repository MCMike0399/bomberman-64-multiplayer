using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    public ShaderMaterial GardenSurface(bool grass=false,bool boulder=false)
    {
        var material=new ShaderMaterial{Shader=_gardenShader??=new Shader{Code=GardenShader}};
        material.SetShaderParameter("surface_kind",grass?0:boulder?2:1);
        return material;
    }

    // Original world-space turf and limestone. Derivative filtering keeps the
    // small mineral/grass marks stable at the normal battle camera distance.
    private const string GardenShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform int surface_kind=0;
varying vec3 world;
varying vec3 normal_world;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
float noise(vec2 p){
 vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
 return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),f.x),f.y);
}
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1)).xyz;normal_world=normalize(MODEL_NORMAL_MATRIX*NORMAL);}
void fragment(){
 vec2 p=abs(normal_world.y)>.6?world.xz:vec2(abs(normal_world.x)>.6?world.z:world.x,world.y);
 float cloud=noise(p*.72)*.65+noise(p*2.3)*.35;
 float detail=mix(.5,noise(p*26.0),1.0-smoothstep(.25,1.1,length(fwidth(p*26.0))));
 if(surface_kind==0){
   ALBEDO=mix(vec3(.12,.28,.055),vec3(.35,.54,.14),cloud);
   ALBEDO*=.96+detail*.08;
   ROUGHNESS=.96;
 }else{
   vec3 base=surface_kind==2?vec3(.48,.49,.40):vec3(.64,.65,.55);
   ALBEDO=base*(.82+cloud*.29+(detail-.5)*.09);
   // Fine discontinuous vertical fissures, plus moss close to the ground.
   float crack=1.0-smoothstep(.018,.055,abs(sin(p.x*19.0+noise(p*2.0)*4.0)));
   crack*=smoothstep(.45,.7,noise(p*4.2));
   ALBEDO*=1.0-crack*.3;
   float moss=(1.0-smoothstep(.03,.25,world.y))*smoothstep(.35,.72,noise(world.xz*6.0));
   ALBEDO=mix(ALBEDO,vec3(.19,.28,.10),moss*.5);
   ROUGHNESS=.9;
 }
}
""";
}

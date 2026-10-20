using Godot;
namespace BlastCircuit.Rendering;

/// <summary>One palette and material library for the original arcade-style art.</summary>
public sealed class ArtMaterials
{
    public static readonly Color[] PlayerColors = [new("f6f0df"),new("30314a"),new("ec3d50"),new("356be0"),new("56b76b"),new("ffba37"),new("9d56ce"),new("31c9c5")];
    private readonly Dictionary<string, StandardMaterial3D> _cache = [];
    public StandardMaterial3D Color(Color color, float roughness=.72f, bool glow=false)
    {
        string key=$"{color.ToHtml()}:{roughness}:{glow}";
        if(_cache.TryGetValue(key,out var material))return material;
        material=new StandardMaterial3D {AlbedoColor=color,Roughness=roughness};
        if(glow){material.EmissionEnabled=true;material.Emission=color;material.EmissionEnergyMultiplier=.35f;}
        if(color.A<1) {material.Transparency=BaseMaterial3D.TransparencyEnum.Alpha; material.DepthDrawMode=BaseMaterial3D.DepthDrawModeEnum.Disabled;}
        _cache[key]=material;return material;
    }
    public ShaderMaterial Stone(bool cobble=false)
    {
        var material=new ShaderMaterial { Shader=new Shader { Code=StoneShader } };
        material.SetShaderParameter("base_color",cobble?new Color("70634c"):new Color("ceb377"));
        material.SetShaderParameter("mortar_color",cobble?new Color("514c43"):new Color("a18a61"));
        material.SetShaderParameter("tile_count",cobble?new Vector2(2,3):new Vector2(1,1));
        material.SetShaderParameter("grout",cobble?.045f:.014f);
        return material;
    }
    public ShaderMaterial Water()=>new() { Shader=new Shader { Code=WaterShader } };
    private const string StoneShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform vec4 base_color : source_color = vec4(.8,.7,.45,1);
uniform vec4 mortar_color : source_color = vec4(.5,.4,.25,1);
uniform vec2 tile_count=vec2(1);
uniform float grout=.018;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
void fragment(){
  vec2 v=UV*tile_count;
  v.x+=mod(floor(v.y),2.0)*.45;
  vec2 f=fract(v);vec2 cell=floor(v);
  float dist=min(min(f.x,1.0-f.x),min(f.y,1.0-f.y));
  float seam=smoothstep(grout*.5,grout*1.6,dist);
  float grain=hash(floor(UV*90.0))*.018;
  float tone=.96+hash(cell)*.07+grain;
  ALBEDO=mix(mortar_color.rgb,base_color.rgb*tone,seam);
  ROUGHNESS=.89;
}
""";
    private const string WaterShader="""
shader_type spatial;
render_mode diffuse_burley;
varying vec3 world;
uniform float flood_amount=0.0;
void vertex(){
  world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
  VERTEX.y+=sin(world.x*1.25+TIME*.6)*.035+sin(world.z*1.8-TIME*.8)*.025;
}
void fragment(){
  vec2 p=world.xz;
  float a=sin(p.x*1.2+sin(p.y*.9+TIME*.4));
  float b=sin(p.y*1.4+sin(p.x*.8-TIME*.3));
  float caustic=pow(max(0.0,1.0-abs(a+b)*.9),10.0);
  float wave=sin(p.x*.4+p.y*.7+TIME*.3)*.5+.5;
  ALBEDO=mix(vec3(.035,.34,.43),vec3(.075,.49,.56),wave*.55)+vec3(.035,.065,.055)*caustic;
  ALBEDO=mix(ALBEDO,ALBEDO*vec3(.64,.78,.95),flood_amount*.55);
  ROUGHNESS=.48; METALLIC=.08; SPECULAR=.4;
  NORMAL_MAP=vec3(.5+sin(p.x*1.2+TIME*.6)*.035,.5+cos(p.y*1.2+TIME*.6)*.035,1.0);
  NORMAL_MAP_DEPTH=.3;
}
""";
}

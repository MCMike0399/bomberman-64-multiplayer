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
    public ShaderMaterial Stone(bool cobble=false)=>StoneMaterial(cobble?1:0);
    public ShaderMaterial Masonry()=>StoneMaterial(2);
    private static ShaderMaterial StoneMaterial(int style)
    {
        var material=new ShaderMaterial {Shader=new Shader {Code=StoneShader}};
        material.SetShaderParameter("stone_style",style);
        material.SetShaderParameter("base_color",style==1?new Color("6e6653"):new Color("ceb37a"));
        material.SetShaderParameter("mortar_color",style==1?new Color("302b21"):new Color("81602e"));
        return material;
    }
    public ShaderMaterial Water()=>new() { Shader=new Shader { Code=WaterShader } };
    private const string StoneShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform vec4 base_color : source_color = vec4(.85,.71,.36,1);
uniform vec4 mortar_color : source_color = vec4(.5,.38,.18,1);
uniform int stone_style=0;
varying vec3 world;
varying vec3 world_normal;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
vec2 hash2(vec2 p){return vec2(hash(p),hash(p+vec2(43.7,17.2)));}
void vertex(){
  world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
  world_normal=normalize(MODEL_NORMAL_MATRIX*NORMAL);
}
void fragment(){
  // World coordinates keep courses continuous across the authority's individual cells.
  bool top=abs(world_normal.y)>.6;
  vec2 p=top?world.xz:vec2(abs(world_normal.x)>.6?world.z:world.x,world.y);
  float grain=(hash(floor(p*84.0))-.5)*.025;
  if(stone_style==1){
    // Offset Voronoi blocks produce the irregular broad cobbles of the recessed court.
    vec2 q=p*2.3,cell=floor(q),f=fract(q),owner=vec2(0);
    float nearest=9.0,second=9.0;
    for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){
      vec2 offset=vec2(float(x),float(y));
      vec2 seed=offset+.25+hash2(cell+offset)*.5;
      float distance=length(seed-f);
      if(distance<nearest){second=nearest;nearest=distance;owner=cell+offset;}
      else second=min(second,distance);
    }
    float edge=second-nearest;
    float bevel=smoothstep(.025,.15,edge);
    vec3 stone=base_color.rgb*(.76+hash(owner)*.33+grain);
    ALBEDO=mix(mortar_color.rgb,stone*(.76+bevel*.24),smoothstep(.018,.075,edge));
  }else{
    vec2 q=top?p:vec2(p.x/1.45,p.y/.52);
    if(!top)q.x+=mod(floor(q.y),2.0)*.5;
    vec2 f=fract(q),cell=floor(q);
    float edge=min(min(f.x,1.0-f.x),min(f.y,1.0-f.y));
    float seam=smoothstep(.007,.025,edge);
    float chipped=smoothstep(.02,.065,edge);
    float tone=.82+hash(cell)*.24+grain;
    // Fine gold edges frame the big squared blocks without a shiny polished surface.
    vec3 stone=base_color.rgb*tone*(.92+chipped*.08);
    ALBEDO=mix(mortar_color.rgb,stone,seam);
  }
  ROUGHNESS=.93;
  SPECULAR=.18;
}
""";
    private const string WaterShader="""
shader_type spatial;
render_mode diffuse_burley;
varying vec3 world;
uniform float flood_amount=0.0;
uniform vec4 deep_color : source_color = vec4(.09,.415,.514,1);
uniform vec4 surface_color : source_color = vec4(.21,.616,.69,1);
uniform vec4 ripple_color : source_color = vec4(.48,.745,.777,1);
void vertex(){
  world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
  VERTEX.y+=sin(world.x*1.8+TIME*.6)*.018+sin(world.z*2.1-TIME*.8)*.018;
}
void fragment(){
  vec2 p=world.xz;
  // Repeating small diagonal wavelets, with a second moving band for the cyan lattice.
  float u=p.x*3.8+p.y*2.5+sin(p.y*2.4+TIME*.7)*.6;
  float v=p.x*2.8-p.y*3.6+sin(p.x*2.7-TIME*.55)*.6;
  float a=sin(u+TIME*.55),b=sin(v-TIME*.45);
  float ridge=pow(max(0.0,.5+.5*sin(u+cos(v*.7)*.9+TIME*.55)),12.0);
  float swell=sin(p.x*1.9+p.y*1.4+TIME*.4)*.5+.5;
  ALBEDO=mix(deep_color.rgb,surface_color.rgb,.45+swell*.35);
  ALBEDO=mix(ALBEDO,ripple_color.rgb,ridge*.26);
  ALBEDO=mix(ALBEDO,ALBEDO*vec3(.74,.86,.96),flood_amount*.4);
  ROUGHNESS=.46; METALLIC=0.0; SPECULAR=.28;
  NORMAL_MAP=vec3(.5+cos(u+TIME*.55)*.045,.5+sin(v-TIME*.45)*.045,1.0);
  NORMAL_MAP_DEPTH=.3;
}
""";
}

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
    public ShaderMaterial Bedrock()=>StoneMaterial(3);
    private static ShaderMaterial StoneMaterial(int style)
    {
        var material=new ShaderMaterial {Shader=new Shader {Code=StoneShader}};
        material.SetShaderParameter("stone_style",style);
        material.SetShaderParameter("base_color",style==1?new Color("6e6653"):style==3?new Color("77745a"):new Color("c7ad79"));
        material.SetShaderParameter("mortar_color",style==1?new Color("302b21"):style==3?new Color("454b3d"):new Color("75613f"));
        return material;
    }
    public ShaderMaterial Water()=>new() { Shader=new Shader { Code=WaterShader } };
    private const string StoneShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform vec4 base_color : source_color = vec4(.85,.71,.36,1);
uniform vec4 mortar_color : source_color = vec4(.5,.38,.18,1);
uniform int stone_style=0;
uniform float water_height=-0.53;
varying vec3 world;
varying vec3 world_normal;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
vec2 hash2(vec2 p){return vec2(hash(p),hash(p+vec2(43.7,17.2)));}
float noise(vec2 p){
  vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
  return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),f.x),f.y);
}
void vertex(){
  world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
  world_normal=normalize(MODEL_NORMAL_MATRIX*NORMAL);
}
void fragment(){
  // World coordinates keep courses continuous across the authority's individual cells.
  bool top=abs(world_normal.y)>.6;
  vec2 p=top?world.xz:vec2(abs(world_normal.x)>.6?world.z:world.x,world.y);
  // Fade subpixel grain instead of letting it sparkle as the camera moves.
  float grain=(noise(p*84.0)-.5)*.025*(1.0-smoothstep(.3,1.0,length(fwidth(p*84.0))));
  float relief=0.0;
  float dry_roughness=.86;
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
    relief=bevel*.065;
    dry_roughness=.93;
  }else{
    vec2 q=top?p*vec2(.78,1.0):vec2(p.x/1.45,p.y/.52);
    q.x+=mod(floor(q.y),2.0)*.5;
    if(stone_style==3)q+=vec2(noise(p*2.0)*.35,noise(p*1.1)*.3);
    vec2 f=fract(q),cell=floor(q);
    float mineral=noise(p*5.1)*.6+noise(p*16.7)*.3+noise(p*48.0)*.1;
    float edge=min(min(f.x,1.0-f.x),min(f.y,1.0-f.y));
    // Uneven chamfers and occasional pits stay readable as stone at the game camera.
    float chippedEdge=edge-(noise(p*24.0)-.5)*.014;
    float seam=smoothstep(.008,.027,chippedEdge);
    float bevel=smoothstep(.015,.09,chippedEdge);
    float pits=smoothstep(.72,.85,noise(p*31.0));
    float tone=.84+hash(cell)*.19+(mineral-.5)*.15;
    vec3 stone=base_color.rgb*tone*(.85+bevel*.15);
    stone=mix(stone,stone*vec3(.83,.85,.79),pits*.3);
    // A restrained ochre vein and mineral clouding break uniform plastic-looking slabs.
    float vein=1.0-smoothstep(.025,.075,abs(noise(p*2.1)-.47));
    stone=mix(stone,stone*vec3(1.07,.98,.85),vein*.26);
    ALBEDO=mix(mortar_color.rgb,stone,seam);
    relief=seam*(bevel*.12+mineral*.035-pits*.012);
  }
  vec3 dpdx=dFdx(VERTEX),dpdy=dFdy(VERTEX);
  vec3 r1=cross(dpdy,NORMAL),r2=cross(NORMAL,dpdx);
  float determinant=dot(dpdx,r1);
  vec3 gradient=(r1*dFdx(relief)+r2*dFdy(relief))*sign(determinant)/max(abs(determinant),.00001);
  NORMAL=normalize(NORMAL-gradient*.48);
  float wet_edge=water_height+(noise(world.xz*3.5)-.5)*.08;
  float wet=1.0-smoothstep(wet_edge-.12,wet_edge+.18,world.y);
  ALBEDO=mix(ALBEDO,ALBEDO*vec3(.57,.67,.62),wet*.7);
  ROUGHNESS=mix(dry_roughness,.38,wet);
  SPECULAR=mix(.18,.42,wet);
}
""";
    private const string WaterShader="""
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;
uniform float flood_amount=0.0;
uniform float wind_strength=1.0;
uniform float surface_roughness=.31;
uniform vec4 deep_color : source_color = vec4(.055,.25,.32,1);
uniform vec4 surface_color : source_color = vec4(.12,.49,.52,1);
uniform vec4 foam_color : source_color = vec4(.60,.79,.72,1);
varying vec3 world;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
float noise(vec2 p){
  vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
  return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),f.x),f.y);
}
// Each component returns height and the exact horizontal slope. Several nonparallel
// wavelengths prevent the previous scrolling stripe pattern and keep normals coherent.
vec3 wave(vec2 p,vec2 direction,float frequency,float amplitude,float speed){
  vec2 transverse=vec2(-direction.y,direction.x);
  float clock=TIME*mix(.6,1.0,wind_strength);
  float bend=dot(p,transverse)*frequency*.53+clock*speed*.23;
  float phase=dot(p,direction)*frequency+.85*sin(bend)-clock*speed;
  vec2 phase_gradient=direction*frequency+transverse*(.85*cos(bend)*frequency*.53);
  return vec3(amplitude*sin(phase),phase_gradient*amplitude*cos(phase));
}
vec3 waves(vec2 p){
  return wave(p,normalize(vec2(1,.35)),1.2,.045,.72)
       +wave(p,normalize(vec2(-.4,1)),2.1,.022,1.05)
       +wave(p,normalize(vec2(.7,.8)),4.3,.009,1.42)
       +wave(p,normalize(vec2(-.8,.3)),7.7,.004,1.8);
}
void vertex(){
  world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
  float calm=wind_strength*mix(1.0,.2,smoothstep(0.0,.2,flood_amount));
  VERTEX.y+=waves(world.xz).x*calm+flood_amount*.12;
  world.y=(MODEL_MATRIX*vec4(VERTEX,1.0)).y;
}
void fragment(){
  vec3 w=waves(world.xz);
  // Fine capillary ripples add glints without more vertices; fade them in the
  // distance to avoid crawling highlights at the normal battle camera.
  float detail=1.0-smoothstep(.35,1.2,length(fwidth(world.xz*12.0)));
  w.yz+=detail*(wave(world.xz,normalize(vec2(.9,-.6)),12.0,.0025,2.0).yz
                +wave(world.xz,normalize(vec2(.2,1)),17.0,.0015,1.7).yz);
  w.yz*=wind_strength*mix(1.0,.2,smoothstep(0.0,.2,flood_amount));
  // Write a view-space geometric normal, as required by the spatial shader API.
  vec3 normal_world=normalize(vec3(-w.y,1.0,-w.z));
  NORMAL=normalize((VIEW_MATRIX*vec4(normal_world,0.0)).xyz);
  float raw_depth=textureLod(depth_texture,SCREEN_UV,0.0).r;
  vec3 ndc=vec3(SCREEN_UV*2.0-1.0,raw_depth);
  #if CURRENT_RENDERER == RENDERER_COMPATIBILITY
    ndc.z=raw_depth*2.0-1.0;
  #endif
  vec4 scene_view=INV_PROJECTION_MATRIX*vec4(ndc,1.0);
  scene_view.xyz/=max(scene_view.w,.00001);
  float thickness=max(0.0,-scene_view.z+VERTEX.z);
  float shallow=exp(-thickness*.72);
  float cloud=noise(world.xz*.65+vec2(TIME*.014,-TIME*.01));
  ALBEDO=mix(deep_color.rgb,surface_color.rgb,shallow*.65+cloud*.18);
  // Fresnel and sun/moon glints now come from the real sky radiance and lights,
  // not a fixed daytime tint painted into ALBEDO.
  // Depth-adaptive broken contact foam follows the actual submerged terrain, also
  // during flooding. It cannot invent a safe floor or change authority geometry.
  float contact=smoothstep(.12,.2,thickness)*(1.0-smoothstep(.3,.65,thickness));
  float foam=contact*smoothstep(.40,.66,noise(world.xz*7.0+vec2(TIME*.12,-TIME*.1)));
  vec3 scene_normal=normalize(cross(dFdx(scene_view.xyz),dFdy(scene_view.xyz)));
  float shoreline_slope=smoothstep(.05,.4,1.0-abs(dot(scene_normal,NORMAL)));
  float vertical_depth=max(0.0,world.y-(INV_VIEW_MATRIX*vec4(scene_view.xyz,1.0)).y);
  // Beveled paving below floodwater is not a shoreline. Reject deeper contact
  // even where a tile edge has a steep normal, avoiding an underwater foam grid.
  foam*=shoreline_slope*(1.0-smoothstep(.08,.22,vertical_depth));
  ALBEDO=mix(ALBEDO,foam_color.rgb,foam*.60);
  ALBEDO*=1.0-flood_amount*.08;
  float normal_variance=dot(dFdx(NORMAL),dFdx(NORMAL))+dot(dFdy(NORMAL),dFdy(NORMAL));
  ROUGHNESS=clamp(mix(surface_roughness,.58,foam)+normal_variance*2.0,.18,.65);
  METALLIC=0.0;SPECULAR=.55;
  // Enter the transparent pass so the copied depth contains the opaque shoreline,
  // rather than the water itself. Full opacity avoids sorting-dependent see-through.
  ALPHA=1.0;
}
""";
}

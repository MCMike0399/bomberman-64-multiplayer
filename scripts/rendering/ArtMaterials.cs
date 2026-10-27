using Godot;
namespace BlastCircuit.Rendering;

/// <summary>One palette and material library for the original arcade-style art.</summary>
public sealed partial class ArtMaterials
{
    public static readonly Color[] PlayerColors = [new("f6f0df"),new("30314a"),new("ec3d50"),new("356be0"),new("56b76b"),new("ffba37"),new("9d56ce"),new("31c9c5")];
    private readonly Dictionary<string, StandardMaterial3D> _cache = [];
    private static Shader? _greedyTrapShader;
    private static Shader? _stoneShader,_waterShader,_clearWaterShader,_reflectiveWaterShader,_gardenShader,_frozenShader;
    /// <summary>
    /// Characters, bombs and pickups read as lacquered miniature figures: a thin glossy
    /// clear coat over the painted color, plus a soft light-driven rim at the silhouette.
    /// </summary>
    public StandardMaterial3D Toy(Color color, float roughness=.72f, bool glow=false)=>Color(color,roughness,glow,toy:true);
    public StandardMaterial3D Color(Color color, float roughness=.72f, bool glow=false, bool toy=false)
    {
        string key=$"{color.ToHtml()}:{roughness}:{glow}:{toy}";
        if(_cache.TryGetValue(key,out var material))return material;
        material=new StandardMaterial3D {AlbedoColor=color,Roughness=roughness};
        if(toy)
        {
            material.ClearcoatEnabled=true;material.Clearcoat=.55f;material.ClearcoatRoughness=.16f;
            material.RimEnabled=true;material.Rim=.3f;material.RimTint=.45f;
        }
        if(glow){material.EmissionEnabled=true;material.Emission=color;material.EmissionEnergyMultiplier=.35f;}
        if(color.A<1) {material.Transparency=BaseMaterial3D.TransparencyEnum.Alpha; material.DepthDrawMode=BaseMaterial3D.DepthDrawModeEnum.Disabled;}
        _cache[key]=material;return material;
    }
    public ShaderMaterial Stone(bool cobble=false)=>StoneMaterial(cobble?1:0);
    public ShaderMaterial Masonry()=>StoneMaterial(2);
    public ShaderMaterial Bedrock()=>StoneMaterial(3);
    private static ShaderMaterial StoneMaterial(int style)
    {
        var material=new ShaderMaterial {Shader=_stoneShader??=new Shader {Code=StoneShader}};
        material.SetShaderParameter("stone_style",style);
        material.SetShaderParameter("base_color",style==1?new Color("6e6653"):style==3?new Color("77745a"):new Color("c7ad79"));
        material.SetShaderParameter("mortar_color",style==1?new Color("302b21"):style==3?new Color("454b3d"):new Color("75613f"));
        return material;
    }
    // The sea samples opaque scene depth, then draws before transparent combat effects.
    // Default distance sorting uses the large plane's center and can paint water
    // over a rear-lane fireball even though that fireball is above the water.
    public ShaderMaterial Water()=>new() { Shader=WaterShaderFor(GraphicsQuality.Current),RenderPriority=-16 };
    /// <summary>
    /// Low keeps opaque water; Medium adds refraction, caustics and glints (one screen copy);
    /// High also mirrors the opaque scene in the surface with a short screen-space march.
    /// </summary>
    public static Shader WaterShaderFor(GraphicsPreset preset)=>preset switch
    {
        GraphicsPreset.Low=>_waterShader??=new Shader {Code=WaterShader},
        GraphicsPreset.High=>_reflectiveWaterShader??=new Shader {Code=WaterShader.Replace("shader_type spatial;","shader_type spatial;\n#define CLEAR_WATER\n#define SCENE_REFLECTIONS",StringComparison.Ordinal)},
        _=>_clearWaterShader??=new Shader {Code=WaterShader.Replace("shader_type spatial;","shader_type spatial;\n#define CLEAR_WATER",StringComparison.Ordinal)}
    };
    private const string StoneShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform vec4 base_color : source_color = vec4(.85,.71,.36,1);
uniform vec4 mortar_color : source_color = vec4(.5,.38,.18,1);
uniform int stone_style=0;
uniform float water_height=-0.53;
uniform float quality_detail=1.0;
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
float filtered_noise(vec2 p){
  float weight=quality_detail*(1.0-smoothstep(.35,1.1,length(fwidth(p))));
  // At battle distance these octaves cannot be resolved. Skip their hash work
  // and converge on the mean instead of changing the material's average color.
  if(weight<.01)return .5;
  return mix(.5,noise(p),weight);
}
void fragment(){
  // World coordinates keep courses continuous across the authority's individual cells.
  bool top=abs(world_normal.y)>.6;
  vec2 p=top?world.xz:vec2(abs(world_normal.x)>.6?world.z:world.x,world.y);
  // Fade subpixel grain instead of letting it sparkle as the camera moves.
  float grain=(filtered_noise(p*84.0)-.5)*.025;
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
    float aa=max(fwidth(edge)*.5,.001);
    float bevel=smoothstep(.025-aa,.15+aa,edge);
    vec3 stone=base_color.rgb*(.76+hash(owner)*.33+grain);
    ALBEDO=mix(mortar_color.rgb,stone*(.76+bevel*.24),smoothstep(.018-aa,.075+aa,edge));
    relief=bevel*.065;
    dry_roughness=.93;
  }else{
    vec2 q=top?p*vec2(.78,1.0):vec2(p.x/1.45,p.y/.52);
    q.x+=mod(floor(q.y),2.0)*.5;
    if(stone_style==3)q+=vec2(noise(p*2.0)*.35,noise(p*1.1)*.3);
    vec2 f=fract(q),cell=floor(q);
    float mineral=noise(p*5.1)*.6+filtered_noise(p*16.7)*.3+filtered_noise(p*48.0)*.1;
    float edge=min(min(f.x,1.0-f.x),min(f.y,1.0-f.y));
    // Uneven chamfers and occasional pits stay readable as stone at the game camera.
    float chippedEdge=edge-(filtered_noise(p*24.0)-.5)*.014;
    float aa=max(length(fwidth(q))*.35,.001);
    float seam=smoothstep(.008-aa,.027+aa,chippedEdge);
    float bevel=smoothstep(.015-aa,.09+aa,chippedEdge);
    // filtered_noise stays at or below .5+weight/2, so pits are exactly zero
    // until this octave is at least 43% resolved; skip the hashes until then.
    float pit_weight=quality_detail*(1.0-smoothstep(.35,1.1,length(fwidth(p*31.0))));
    float pits=pit_weight>.43?smoothstep(.72,.85,mix(.5,noise(p*31.0),pit_weight)):0.0;
    float tone=.84+hash(cell)*.19+(mineral-.5)*.15;
    vec3 stone=base_color.rgb*tone*(.85+bevel*.15);
    // Painterly per-block warmth keeps large courses from reading as one printed tile.
    stone*=mix(vec3(1.0),vec3(1.05,.99,.9),hash(cell+5.1)*.7);
    stone=mix(stone,stone*vec3(.83,.85,.79),pits*.3);
    // A restrained ochre vein and mineral clouding break uniform plastic-looking slabs.
    float vein=1.0-smoothstep(.025,.075,abs(noise(p*2.1)-.47));
    stone=mix(stone,stone*vec3(1.07,.98,.85),vein*.26);
    ALBEDO=mix(mortar_color.rgb,stone,seam);
    relief=seam*(bevel*.12+mineral*.035-pits*.012);
  }
  // Broad warm/cool drift across the stage, like sun-bleached and shaded weathering.
  float macro=noise(p*.17)*.7+noise(p*.43)*.3;
  ALBEDO*=mix(vec3(.95,.97,1.02),vec3(1.04,1.0,.93),macro);
  // Risers deepen toward their submerged roots so tiers read as a grounded diorama.
  float vertical=1.0-abs(world_normal.y);
  ALBEDO*=mix(1.0,mix(.8,1.0,smoothstep(water_height,water_height+2.6,world.y)),vertical);
  vec3 dpdx=dFdx(VERTEX),dpdy=dFdy(VERTEX);
  vec3 r1=cross(dpdy,NORMAL),r2=cross(NORMAL,dpdx);
  float determinant=dot(dpdx,r1);
  vec3 gradient=(r1*dFdx(relief)+r2*dFdy(relief))*sign(determinant)/max(abs(determinant),.00001);
  NORMAL=normalize(NORMAL-gradient*.48);
  float wet_edge=water_height+(noise(world.xz*3.5)-.5)*.08;
  float wet=1.0-smoothstep(wet_edge-.12,wet_edge+.18,world.y);
  ALBEDO=mix(ALBEDO,ALBEDO*vec3(.57,.67,.62),wet*.7);
  // A soft algae fringe hugs the waterline on vertical stone.
  float fringe=smoothstep(wet_edge-.35,wet_edge-.05,world.y)*(1.0-smoothstep(wet_edge+.02,wet_edge+.2,world.y));
  fringe*=vertical*smoothstep(.3,.65,noise(vec2(world.x+world.z,world.y)*vec2(3.1,7.0)));
  ALBEDO=mix(ALBEDO,vec3(.2,.32,.2),fringe*.55);
  float normal_variance=dot(dFdx(NORMAL),dFdx(NORMAL))+dot(dFdy(NORMAL),dFdy(NORMAL));
  ROUGHNESS=clamp(mix(dry_roughness,.38,wet)+normal_variance*.5,.38,1.0);
  SPECULAR=mix(.18,.42,wet);
}
""";
    private const string WaterShader="""
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, depth_draw_always;
uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;
#ifdef CLEAR_WATER
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear_mipmap;
#endif
uniform float flood_amount=0.0;
uniform float wind_strength=1.0;
uniform float surface_roughness=.31;
uniform float quality_detail=1.0;
uniform vec4 deep_color : source_color = vec4(.055,.25,.32,1);
uniform vec4 surface_color : source_color = vec4(.12,.49,.52,1);
uniform vec4 foam_color : source_color = vec4(.60,.79,.72,1);
// World-space direction toward the key light and its color times energy, from StageLighting.
uniform vec3 sun_direction=vec3(.3,.85,.42);
uniform vec4 light_tint : source_color = vec4(1.0);
uniform float clarity=.82;
// TempleStageView places an unseen opaque seabed here so depth-based post effects see
// a real surface under the open sea. Anything at that depth is treated as open sea.
uniform float seabed_height=-3.0;
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
// View-space scene position behind the water. w is 1 for real geometry and 0 where
// only sky lies behind the surface (the open sea), whose far-plane depth is degenerate.
vec4 view_position(vec2 uv,mat4 inv_projection){
  vec3 ndc=vec3(uv*2.0-1.0,textureLod(depth_texture,uv,0.0).r);
  #if CURRENT_RENDERER == RENDERER_COMPATIBILITY
    ndc.z=ndc.z*2.0-1.0;
  #endif
  vec4 position=inv_projection*vec4(ndc,1.0);
  if(position.w<=.0001)return vec4(0.0,0.0,-1e4,0.0);
  vec3 scene=position.xyz/position.w;
  return vec4(scene,-scene.z<150.0?1.0:0.0);
}
#ifdef CLEAR_WATER
// Drifting Voronoi borders read as sunlit caustic webs on the submerged stone.
float caustic_web(vec2 p,float speed){
  vec2 cell=floor(p),f=fract(p);
  float nearest=9.0,second=9.0;
  for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){
    vec2 offset=vec2(float(x),float(y));
    float h=hash(cell+offset);
    vec2 seed=offset+.5+.36*vec2(sin(TIME*speed+h*6.283),cos(TIME*speed*.83+h*11.0));
    float distance=length(seed-f);
    if(distance<nearest){second=nearest;nearest=distance;}else second=min(second,distance);
  }
  float edge=second-nearest;
  return 1.0-smoothstep(0.0,.11+fwidth(edge),edge);
}
#endif
void fragment(){
  vec3 w=waves(world.xz);
  // Fine capillary ripples add glints without more vertices; fade them in the
  // distance to avoid crawling highlights at the normal battle camera.
  float detail=quality_detail*(1.0-smoothstep(.35,1.2,length(fwidth(world.xz*12.0))));
#ifdef SCENE_REFLECTIONS
  // The mirror follows the broad swell only; per-pixel capillary ripples would tear it.
  vec2 swell=w.yz;
#endif
  if(detail>.01){
    w.yz+=detail*(wave(world.xz,normalize(vec2(.9,-.6)),12.0,.0025,2.0).yz
                  +wave(world.xz,normalize(vec2(.2,1)),17.0,.0015,1.7).yz);
  }
  w.yz*=wind_strength*mix(1.0,.2,smoothstep(0.0,.2,flood_amount));
  // Write a view-space geometric normal, as required by the spatial shader API.
  vec3 normal_world=normalize(vec3(-w.y,1.0,-w.z));
  NORMAL=normalize((VIEW_MATRIX*vec4(normal_world,0.0)).xyz);
  vec4 scene=view_position(SCREEN_UV,INV_PROJECTION_MATRIX);
  vec3 scene_view=scene.xyz;
  bool open_sea=scene.w<.5||(INV_VIEW_MATRIX*vec4(scene_view,1.0)).y<seabed_height+.4;
  float thickness=open_sea?0.0:max(0.0,-scene_view.z+VERTEX.z);
  float shallow=exp(-thickness*.72);
  // The open sea keeps its bright lagoon tone near the island and deepens gently
  // toward the horizon, giving the diorama a readable edge.
  float cloud=noise(world.xz*.65+vec2(TIME*.014,-TIME*.01));
  // Wave-correlated mottling: troughs read deeper and swells catch the lagoon tone.
  if(open_sea)shallow=clamp(.8+w.x*2.2+(cloud-.5)*.2,0.0,1.0)*mix(1.0,.6,smoothstep(16.0,48.0,distance(world.xz,NODE_POSITION_WORLD.xz)));
  vec3 body=mix(deep_color.rgb,surface_color.rgb,shallow*.65+cloud*.18);
  // Fresnel and sun/moon glints now come from the real sky radiance and lights,
  // not a fixed daytime tint painted into ALBEDO.
  // A crisp contact line plus soft bands that travel away from the shore follow
  // the actual submerged terrain, also during flooding. Foam cannot invent a
  // safe floor or change authority geometry.
  float contact=smoothstep(.03,.07,thickness)*(1.0-smoothstep(.13,.2,thickness));
  float band=0.0;
  if(thickness>.16&&thickness<.5){
    band=smoothstep(.7,.92,sin(thickness*9.0-TIME*1.6)*.5+.5);
    band*=smoothstep(.16,.24,thickness)*(1.0-smoothstep(.3,.5,thickness));
    band*=smoothstep(.34,.6,noise(world.xz*5.0+vec2(TIME*.12,-TIME*.1)));
  }
  float foam=max(contact,band*.55);
  vec3 scene_cross=cross(dFdx(scene_view),dFdy(scene_view));
  float cross_length=length(scene_cross);
  vec3 scene_normal=cross_length>1e-8?scene_cross/cross_length:NORMAL;
  float shoreline_slope=smoothstep(.05,.4,1.0-abs(dot(scene_normal,NORMAL)));
  float vertical_depth=max(0.0,world.y-(INV_VIEW_MATRIX*vec4(scene_view,1.0)).y);
  // Beveled paving below floodwater is not a shoreline. Reject deeper contact
  // even where a tile edge has a steep normal, avoiding an underwater foam grid.
  foam*=shoreline_slope*(1.0-smoothstep(.08,.22,vertical_depth));
  ALBEDO=mix(body,foam_color.rgb,foam*.72);
#ifdef CLEAR_WATER
  // Clear shallows: refract the opaque scene copied before this transparent pass.
  // Reject offsets that land on geometry above the surface so shore edges stay clean.
  vec2 refract_uv=SCREEN_UV+normal_world.xz*.028*smoothstep(0.0,.5,vertical_depth);
  vec4 refracted=view_position(refract_uv,INV_PROJECTION_MATRIX);
  if(refracted.w<.5||refracted.z>VERTEX.z){refract_uv=SCREEN_UV;refracted=scene;}
  vec3 bed=(INV_VIEW_MATRIX*vec4(refracted.xyz,1.0)).xyz;
  float bed_depth=max(0.0,world.y-bed.y);
  // Only real submerged geometry is revealed; open sea keeps its authored color.
  float transmit=refracted.w*step(seabed_height+.4,bed.y)*clarity*exp(-bed_depth*1.25)*(1.0-foam);
  vec3 seen=textureLod(screen_texture,refract_uv,0.0).rgb;
  float caustic_detail=quality_detail*(1.0-smoothstep(.25,.9,length(fwidth(bed.xz*1.5))));
  if(caustic_detail>.01&&transmit>.02){
    float web=max(caustic_web(bed.xz*1.5,.9),caustic_web(bed.xz*2.3+3.7,1.2)*.7);
    float reach=smoothstep(.02,.12,bed_depth)*exp(-bed_depth*.9);
    seen+=seen*web*reach*caustic_detail*1.4+light_tint.rgb*web*reach*caustic_detail*.05;
  }
  // The floor takes on the water's hue before deeper absorption hides it.
  seen*=mix(vec3(1.0),surface_color.rgb*2.1,.5);
  ALBEDO*=1.0-transmit;
  EMISSION=seen*transmit;
#ifdef SCENE_REFLECTIONS
  // High: mirror the opaque scene copy in the surface. A short screen-space march
  // along the reflected view ray against the opaque depth finds walls, columns,
  // figures and bombs beyond each water pixel; the wave normal makes the mirror
  // image wobble. Misses keep the sky radiance reflection, so nothing new appears
  // where the scene cannot be seen. Fresnel is deliberately stronger than physical:
  // the steep battle camera would otherwise hide the effect, and the lacquered
  // diorama reads better with a visible mirror. Shallow clear water still shows
  // its floor through the remaining transmission.
  {
    swell*=wind_strength*mix(1.0,.2,smoothstep(0.0,.2,flood_amount))*.6;
    vec3 mirror_normal=normalize((VIEW_MATRIX*vec4(normalize(vec3(-swell.x,1.0,-swell.y)),0.0)).xyz);
    vec3 ray=reflect(-VIEW,mirror_normal);
    float mirror=0.0;vec3 mirrored=vec3(0.0);
    float facing=clamp(dot(VIEW,mirror_normal),0.0,1.0);
    float fresnel=mix(.24,.85,pow(1.0-facing,3.0))*(1.0-foam);
    // Reach grows with distance; a ray turning toward the camera plane stops short of it.
    float reach=clamp(-VERTEX.z*.8,2.0,14.0);
    if(ray.z>0.0)reach=min(reach,(-VERTEX.z-.5)/ray.z);
    if(fresnel>.01&&reach>.25){
      // Interleaved-gradient jitter per pixel turns step banding into fine
      // water sparkle instead of staircase edges on reflected decks.
      float jitter=fract(52.9829189*fract(dot(FRAGCOORD.xy,vec2(.06711056,.00583715))));
      vec3 previous=VERTEX;float t_previous=0.0;
      for(int i=1;i<=16;i++){
        float f=(float(i)-jitter*.9)/16.0;float t=reach*(f*f*.75+f*.25);
        vec3 p=VERTEX+ray*t;
        vec4 clip=PROJECTION_MATRIX*vec4(p,1.0);
        vec2 uv=clip.xy/clip.w*.5+.5;
        if(any(lessThan(uv,vec2(0.0)))||any(greaterThan(uv,vec2(1.0))))break;
        float gap=view_position(uv,INV_PROJECTION_MATRIX).z-p.z;
        if(gap>0.0&&gap<max(.5,(t-t_previous)*1.5)){
          // Bisect between the last miss and this hit for a stable contact.
          vec3 a=previous,b=p;
          for(int j=0;j<4;j++){
            vec3 m=(a+b)*.5;vec4 mc=PROJECTION_MATRIX*vec4(m,1.0);vec2 muv=mc.xy/mc.w*.5+.5;
            if(view_position(muv,INV_PROJECTION_MATRIX).z>m.z)b=m;else a=m;
          }
          vec4 hc=PROJECTION_MATRIX*vec4(b,1.0);vec2 huv=hc.xy/hc.w*.5+.5;
          vec2 border=smoothstep(vec2(0.0),vec2(.08),huv)*smoothstep(vec2(0.0),vec2(.08),1.0-huv);
          mirror=fresnel*border.x*border.y*(1.0-smoothstep(.6,1.0,f));
          mirrored=textureLod(screen_texture,huv,0.0).rgb;
          break;
        }
        previous=p;t_previous=t;
      }
    }
    ALBEDO*=1.0-mirror;
    EMISSION=EMISSION*(1.0-mirror)+mirrored*mirror;
  }
#endif
  // Sun-path glints: sparse twinkling points only where the ripples mirror the light.
  if(detail>.01){
    vec2 g=world.xz*1.7+vec2(TIME*.05,0.0);
    float aa=fwidth(g.x)*1.2;
    vec2 cell=floor(g);
    float h=hash(cell+3.1);
    // Unlit cells and the dark part of each twinkle add exactly zero.
    float twinkle=h<.45?0.0:pow(max(sin(TIME*(1.3+h*2.2)+h*40.0),0.0),10.0);
    if(twinkle>0.0){
      vec3 view_world=normalize((INV_VIEW_MATRIX*vec4(VIEW,0.0)).xyz);
      float path=pow(max(dot(reflect(-view_world,normal_world),normalize(sun_direction)),0.0),14.0);
      vec2 centre=vec2(hash(cell+11.0),hash(cell+23.0))*.6+.2;
      float point=1.0-smoothstep(.07-aa,.07+aa,length(fract(g)-centre));
      EMISSION+=light_tint.rgb*point*twinkle*path*step(.45,h)*detail*(1.0-flood_amount)*3.0;
    }
  }
#endif
  ALBEDO*=1.0-flood_amount*.08;
  float normal_variance=dot(dFdx(NORMAL),dFdx(NORMAL))+dot(dFdy(NORMAL),dFdy(NORMAL));
  ROUGHNESS=clamp(mix(surface_roughness,.58,foam)+normal_variance*2.0,.18,.65);
  METALLIC=0.0;SPECULAR=.55;
  // Stay in the transparent pass so the sampled scene copy contains the opaque
  // shoreline. Early priority plus depth_draw_always then occludes submerged
  // effects without painting water over fire and sparks above the surface.
  ALPHA=1.0;
}
""";
}

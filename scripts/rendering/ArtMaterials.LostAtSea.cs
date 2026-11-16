using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    private static Shader? _observationGlassShader,_observationClearGlassShader;
    public ShaderMaterial ObservationGlass()
    {
        var material=new ShaderMaterial{Shader=ObservationGlassShaderFor(GraphicsQuality.Current),RenderPriority=-8};
        material.SetShaderParameter("centre_color",new Color("2a2170"));
        material.SetShaderParameter("edge_color",new Color("3d35a0"));
        GraphicsQuality.ApplyShaderDetail(material);return material;
    }
    public static Shader ObservationGlassShaderFor(GraphicsPreset preset)=>preset==GraphicsPreset.Low
        ?_observationGlassShader??=new Shader{Code=ObservationGlassShader}
        :_observationClearGlassShader??=new Shader{Code=ObservationGlassShader.Replace("shader_type spatial;","shader_type spatial;\n#define GLASS_DEPTH",StringComparison.Ordinal)};

    private const string ObservationGlassShader="""
shader_type spatial;
render_mode diffuse_burley, depth_draw_always;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
uniform float ambient_clock=0.0;
uniform float surface_sheen=1.0;
uniform bool depth_enabled=true;
uniform vec4 centre_color : source_color=vec4(.165,.129,.439,1);
uniform vec4 edge_color : source_color=vec4(.239,.208,.627,1);
uniform vec4 glass_bounds=vec4(3,3,18,18);
#ifdef GLASS_DEPTH
uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
#endif
varying vec3 world;
varying vec2 arena;
void vertex(){arena=VERTEX.xz;world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
#ifdef GLASS_DEPTH
vec4 glass_scene(vec2 uv,mat4 inv_projection){
 vec3 ndc=vec3(uv*2.0-1.0,textureLod(depth_texture,uv,0.0).r);
 #if CURRENT_RENDERER == RENDERER_COMPATIBILITY
 ndc.z=ndc.z*2.0-1.0;
 #endif
 vec4 p=inv_projection*vec4(ndc,1.0);
 if(p.w<=.0001)return vec4(0,0,-1e4,0);
 vec3 position=p.xyz/p.w;
 return vec4(position,-position.z<150.0?1.0:0.0);
}
vec3 glass_soft_color(vec2 uv,vec2 pixel,float radius){
 vec2 offset=pixel*radius;
 return textureLod(screen_texture,uv,0.0).rgb*.4+
  (textureLod(screen_texture,uv+vec2(offset.x,0),0.0).rgb+
   textureLod(screen_texture,uv-vec2(offset.x,0),0.0).rgb+
   textureLod(screen_texture,uv+vec2(0,offset.y),0.0).rgb+
   textureLod(screen_texture,uv-vec2(0,offset.y),0.0).rgb)*.15;
}
#endif
void fragment(){
 float edge=min(min(arena.x-glass_bounds.x,glass_bounds.z-arena.x),min(arena.y-glass_bounds.y,glass_bounds.w-arena.y));
 float depth=smoothstep(.0,4.0,edge);
 vec3 body=mix(edge_color.rgb,centre_color.rgb,depth);
 // Approximately three broad 1.6-2.5-cell sheen bands, on every preset.
 float diagonal=dot(arena,normalize(vec2(1,.78)))-ambient_clock*.045;
 float distance_to_band=abs(mod(diagonal+4.0,8.0)-4.0);
 float sheen=1.0-smoothstep(.8,1.25,distance_to_band);
 // Low has no scene reflection or bloom. Paint a broad, soft sky reflection
 // into the opaque pane instead of asking a specular pinprick to describe it.
 // The narrow bright strips flank the real flush mullions, never cover them.
 vec2 pane_size=(glass_bounds.zw-glass_bounds.xy)/3.0;
 vec2 pane_point=mod(arena-glass_bounds.xy,pane_size);
 vec2 pane_edge=min(pane_point,pane_size-pane_point);
 float bevel=1.0-smoothstep(.065,.19,min(pane_edge.x,pane_edge.y));
 #ifdef GLASS_DEPTH
 float sheen_weight=.13;
 float bevel_weight=.13;
 #else
 float sheen_weight=.32;
 float bevel_weight=.30;
 // Low's opaque pane keeps one restrained subsurface cue. Medium/High use
 // the real wreck and manta through their existing depth/refraction pass.
 vec2 wreck=vec2(arena.y-7.9,5.2-arena.x);
 float rib=1.0-smoothstep(.035,.09,abs(mod(wreck.x+.5,1.0)-.5));
 rib*=1.0-smoothstep(.64,.80,abs(wreck.y));
 rib*=1.0-smoothstep(2.05,2.40,abs(wreck.x));
 float keel=(1.0-smoothstep(.05,.13,abs(wreck.y)))*(1.0-smoothstep(2.25,2.50,abs(wreck.x)));
 float phase=ambient_clock*6.28318530718/84.0;
 vec2 manta=arena-vec2(10.5+sin(phase)*3.2,10.5+cos(phase)*2.6);
 float wing=max(0.0,1.0-abs(manta.x)/2.6);
 float ray=(1.0-smoothstep(wing*.85,wing*.85+.10,abs(manta.y+.10)))*step(abs(manta.x),2.6);
 body*=1.0-max(max(rib,keel)*.18,ray*.13);
 #endif
 vec3 reflected_sky=vec3(.48,.57,.85);
 ALBEDO=mix(body,reflected_sky,sheen*sheen_weight*surface_sheen);
 ALBEDO=mix(ALBEDO,vec3(.56,.66,.90),bevel*bevel_weight);
 EMISSION=reflected_sky*(sheen*.045*surface_sheen+bevel*.025);
 #ifdef GLASS_DEPTH
 vec2 pixel=1.0/VIEWPORT_SIZE;
 vec4 below=glass_scene(SCREEN_UV,INV_PROJECTION_MATRIX);
 vec3 below_world=(INV_VIEW_MATRIX*vec4(below.xyz,1.0)).xyz;
 if(depth_enabled&&below.w>.5&&below_world.y<world.y-.05){
   float thickness=world.y-below_world.y;
   float feature=1.0-smoothstep(1.1,4.2,thickness);
   float transmission=.12+feature*.38;
   vec3 seen=glass_soft_color(SCREEN_UV,pixel,2.0+thickness*.35);
   float luminance=dot(seen,vec3(.2126,.7152,.0722));
   seen=mix(seen,vec3(luminance),.38)*vec3(.58,.64,.95);
   ALBEDO*=1.0-transmission;
   EMISSION+=seen*transmission;
 }
 // Real opaque-scene reflections, using the same bounded depth-march principle
 // as the shared premium sea. Flat glass needs no wave jitter or extra scene.
 vec3 mirror_normal=normalize((VIEW_MATRIX*vec4(0,1,0,0)).xyz);
 vec3 ray=reflect(-VIEW,mirror_normal);
 float reach=clamp(-VERTEX.z*.8,2.0,14.0);
 if(ray.z>0.0)reach=min(reach,(-VERTEX.z-.5)/ray.z);
 vec3 previous=VERTEX;float previous_t=0.0;
 for(int i=1;i<=16;i++){
   float f=float(i)/16.0;float t=reach*(f*f*.75+f*.25);
   vec3 p=VERTEX+ray*t;vec4 clip=PROJECTION_MATRIX*vec4(p,1);
   vec2 uv=clip.xy/clip.w*.5+.5;
   if(any(lessThan(uv,vec2(0)))||any(greaterThan(uv,vec2(1))))break;
   vec4 scene=glass_scene(uv,INV_PROJECTION_MATRIX);float gap=scene.z-p.z;
   if(gap>0.0&&gap<max(.4,(t-previous_t)*1.4)){
     vec3 a=previous,b=p;
     for(int j=0;j<4;j++){
       vec3 m=(a+b)*.5;vec4 mc=PROJECTION_MATRIX*vec4(m,1);vec2 mu=mc.xy/mc.w*.5+.5;
       if(glass_scene(mu,INV_PROJECTION_MATRIX).z>m.z)b=m;else a=m;
     }
     vec4 hc=PROJECTION_MATRIX*vec4(b,1);vec2 hu=hc.xy/hc.w*.5+.5;
     vec4 hit=glass_scene(hu,INV_PROJECTION_MATRIX);
     float error=abs(hit.z-b.z);
     vec3 hit_world=(INV_VIEW_MATRIX*vec4(hit.xyz,1)).xyz;
     float confidence=(1.0-smoothstep(.04,.14,error))*step(world.y+.05,hit_world.y);
     vec2 border=smoothstep(vec2(0),vec2(.05),hu)*smoothstep(vec2(0),vec2(.05),1.0-hu);
     float reflection=.25*confidence*border.x*border.y;
     vec3 reflected=glass_soft_color(hu,pixel,1.4);
     ALBEDO*=1.0-reflection;EMISSION=EMISSION*(1.0-reflection)+reflected*reflection;
     break;
   }
   previous=p;previous_t=t;
 }
 #endif
 // Keep the surface in the early transparent pass, but compose the visible
 // submerged geometry explicitly so a pale backing cannot wash out the glass.
 ALPHA=1.0;
 METALLIC=0.0;SPECULAR=.24;ROUGHNESS=.27;
 CLEARCOAT=.14;CLEARCOAT_ROUGHNESS=.42;
}
""";
}

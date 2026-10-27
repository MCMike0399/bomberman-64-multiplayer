using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    public ShaderMaterial FrozenSurface(int style)
    {
        var material=new ShaderMaterial{Shader=_frozenShader??=new Shader{Code=FrozenShader}};
        material.SetShaderParameter("surface_style",style);
        GraphicsQuality.ApplyShaderDetail(material);
        return material;
    }

    // Original world-space textures: layered blue ice, frosted violet facets,
    // and soft wind-carved snow. Opaque ice writes depth on every graphics preset.
    private const string FrozenShader="""

shader_type spatial;
render_mode diffuse_burley;
uniform int surface_style=0;
uniform float quality_detail=1.0;
// High only: thick layered ice with trapped bubbles, wind-carved drifts and sun glitter.
uniform float quality_premium=0.0;
uniform float weather_clock=0.0;
uniform vec2 wind=vec2(0.0);
// World-space direction toward the key light and its color times energy, from StageLighting.
uniform vec3 sun_direction=vec3(.3,.85,.42);
uniform vec4 light_tint : source_color = vec4(1.0);
varying vec3 world;
varying vec3 model;
varying vec3 world_normal;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
float noise(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),f.x),f.y);}
// A restrained cellular fracture network; 1 on a fracture line, 0 inside a plate.
float fracture(vec2 q,float lo,float hi){
  vec2 i=floor(q),f=fract(q);float first=5.0,second=5.0;
  for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++){
    vec2 g=vec2(float(x),float(z));vec2 cell=i+g;
    vec2 seed=vec2(hash(cell),hash(cell+17.2))*.7+.15;
    float d=length(g+seed-f);
    if(d<first){second=first;first=d;}else second=min(second,d);
  }
  return 1.0-smoothstep(lo,hi,second-first);
}
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1)).xyz;model=VERTEX;world_normal=normalize(MODEL_NORMAL_MATRIX*NORMAL);}
void fragment(){
  vec2 p=world.xz;
  float broad=noise(p*.7),grain=noise(p*38.0);
  float detail=quality_detail;
  bool premium=quality_premium>.5;
  vec3 view_world=normalize((INV_VIEW_MATRIX*vec4(VIEW,0.0)).xyz);
  if(surface_style==0){
    // The fracture network lives below the frosty surface.
    float crack=fracture(p*.58,.012,.036)*.17;
    float veins=noise(p*2.8+vec2(broad*3.0));
    vec3 ice=mix(vec3(.19,.49,.64),vec3(.49,.79,.83),broad*.65+veins*.35);
    if(premium){
      // Thick ice: a deeper, softer fracture layer and trapped bubbles sit below the
      // surface along the view ray, so the sheet reads as a solid block, not paint.
      float slant=max(abs(view_world.y),.25);
      vec2 deep_p=p-view_world.xz*(.2/slant);
      float deep=fracture(deep_p*.41+vec2(31.0,17.0),.02,.12);
      ice=mix(ice,vec3(.36,.70,.86),deep*.32);
      vec2 bp=p*4.5-view_world.xz*(.45/slant);
      vec2 bcell=floor(bp);float bh=hash(bcell+5.5);
      float bfade=1.0-smoothstep(.3,.9,length(fwidth(bp)));
      float baa=fwidth(bp.x);
      if(bh>.9&&bfade>.01){
        vec2 centre=vec2(hash(bcell+1.9),hash(bcell+7.4))*.6+.2;
        float d=length(fract(bp)-centre),r=.05+fract(bh*10.0)*.035;
        ice=mix(ice,vec3(.86,.95,.98),(1.0-smoothstep(r-baa,r+baa,d))*bfade*.55);
      }
    }
    ice=mix(ice,vec3(.67,.89,.91),crack);
    float frost=smoothstep(.57,.85,noise(p*.36+4.0));
    ice=mix(ice,vec3(.72,.87,.88),frost*.48);
    float flow=noise(p*.8-wind*weather_clock*.32);
    float wisp=pow(max(sin(dot(p,vec2(-wind.y,wind.x))*2.2+flow*3.0),0.0),12.0);
    ice=mix(ice,vec3(.84,.94,.97),wisp*.07*min(length(wind),1.0));
    ALBEDO=ice+(grain-.5)*.035*detail;
    ROUGHNESS=.29+frost*.3;SPECULAR=.55;CLEARCOAT=.38;CLEARCOAT_ROUGHNESS=.3;
    // Polished on High: a light metallic term lifts the mirror above a dielectric's
    // few percent so the opaque screen-space reflections show walls, figures and bombs.
    if(premium){ROUGHNESS=.17+frost*.2;METALLIC=.3;SPECULAR=.7;}
  }else if(surface_style==1){
    float strata=sin(model.y*75.0+model.x*22.0+noise(p*11.0)*2.0)*.5+.5;
    float frost=smoothstep(.52,.94,noise(world.xy*16.0))*detail;
    ALBEDO=mix(vec3(.22,.23,.61),vec3(.43,.49,.84),clamp(model.y*.7+strata*.13,0.0,1.0));
    ALBEDO=mix(ALBEDO,vec3(.67,.82,.94),frost*.24);
    ROUGHNESS=.25+frost*.22;SPECULAR=.62;CLEARCOAT=.5;CLEARCOAT_ROUGHNESS=.15;
    RIM=.22;RIM_TINT=.35;
  }else{
    float ridges=sin(world.y*22.0+noise(p*.8)*5.0)*.5+.5;
    float depth=smoothstep(-.6,2.0,world.y);
    ALBEDO=mix(vec3(.37,.64,.74),vec3(.83,.91,.94),depth*.6+.27);
    ALBEDO+=(broad-.5)*.065+(grain-.5)*.028*detail;
    if(surface_style==3)ALBEDO=mix(ALBEDO,vec3(.55,.77,.85),ridges*.16);
    if(premium){
      // Wind-carved drifts: a soft undulation tilts the normal so the sun models the
      // snow, and the hollows take the sky's blue like real shaded snow.
      float drift=noise(p*1.4+vec2(broad*2.0))*.6+noise(p*3.3+vec2(11.0))*.4;
      vec3 dpdx=dFdx(VERTEX),dpdy=dFdy(VERTEX);
      vec3 r1=cross(dpdy,NORMAL),r2=cross(NORMAL,dpdx);
      float determinant=dot(dpdx,r1);
      vec3 gradient=(r1*dFdx(drift)+r2*dFdy(drift))*sign(determinant)/max(abs(determinant),.00001);
      NORMAL=normalize(NORMAL-gradient*.22);
      ALBEDO*=mix(vec3(.74,.84,1.0),vec3(1.0),smoothstep(.3,.7,drift));
    }
    ROUGHNESS=.84;SPECULAR=.28;
  }
  if(detail>.5){
    float h=hash(floor(p*90.0));
    float sparkle=h<.996?0.0:step(.996,h)*pow(max(sin(weather_clock*1.6+h*40.0),0.0),12.0);
    EMISSION=vec3(.58,.74,.9)*sparkle*.12;
  }
  if(premium&&surface_style!=1){
    // Sun glitter: sparse ice crystals carry their own facet orientation and flash
    // only where that facet mirrors the key light toward the camera.
    vec2 gp=p*16.0,gcell=floor(gp);
    float gh=hash(gcell+9.1);
    float gfade=1.0-smoothstep(.35,1.0,length(fwidth(gp)));
    float gaa=fwidth(gp.x);
    if(gh>.955&&gfade>.01){
      vec3 facet=normalize(world_normal+(vec3(hash(gcell+2.3),hash(gcell+4.7),hash(gcell+6.1))-.5)*1.1);
      float path=pow(max(dot(reflect(-view_world,facet),normalize(sun_direction)),0.0),24.0);
      vec2 centre=vec2(hash(gcell+1.3),hash(gcell+3.9))*.5+.25;
      float point=1.0-smoothstep(.18-gaa,.18+gaa,length(fract(gp)-centre));
      float twinkle=.6+.4*sin(weather_clock*2.0+gh*60.0);
      EMISSION+=light_tint.rgb*point*path*gfade*twinkle*2.5;
    }
  }
}
""";
}

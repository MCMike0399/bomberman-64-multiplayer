using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    public ShaderMaterial FrozenSurface(int style)
    {
        var material=new ShaderMaterial{Shader=_frozenShader??=new Shader{Code=FrozenShader}};
        material.SetShaderParameter("surface_style",style);
        material.SetShaderParameter("quality_detail",GraphicsQuality.Current==GraphicsPreset.Low?0:1);
        return material;
    }

    // Original world-space textures: layered blue ice, frosted violet facets,
    // and soft wind-carved snow. Opaque ice writes depth on every graphics preset.
    private const string FrozenShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform int surface_style=0;
uniform float quality_detail=1.0;
uniform float weather_clock=0.0;
uniform vec2 wind=vec2(0.0);
varying vec3 world;
varying vec3 model;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
float noise(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1)),f.x),f.y);}
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1)).xyz;model=VERTEX;}
void fragment(){
  vec2 p=world.xz;
  float broad=noise(p*.7),grain=noise(p*38.0);
  float detail=quality_detail;
  if(surface_style==0){
    // A restrained cellular fracture network lives below the frosty surface.
    vec2 q=p*.58,i=floor(q),f=fract(q);float first=5.0,second=5.0;
    for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++){
      vec2 g=vec2(float(x),float(z));vec2 cell=i+g;
      vec2 seed=vec2(hash(cell),hash(cell+17.2))*.7+.15;
      float d=length(g+seed-f);
      if(d<first){second=first;first=d;}else second=min(second,d);
    }
    float crack=(1.0-smoothstep(.012,.036,second-first))*.17;
    float veins=noise(p*2.8+vec2(broad*3.0));
    vec3 ice=mix(vec3(.19,.49,.64),vec3(.49,.79,.83),broad*.65+veins*.35);
    ice=mix(ice,vec3(.67,.89,.91),crack);
    float frost=smoothstep(.57,.85,noise(p*.36+4.0));
    ice=mix(ice,vec3(.72,.87,.88),frost*.48);
    float flow=noise(p*.8-wind*weather_clock*.32);
    float wisp=pow(max(sin(dot(p,vec2(-wind.y,wind.x))*2.2+flow*3.0),0.0),12.0);
    ice=mix(ice,vec3(.84,.94,.97),wisp*.07*min(length(wind),1.0));
    ALBEDO=ice+(grain-.5)*.035*detail;
    ROUGHNESS=.29+frost*.3;SPECULAR=.55;CLEARCOAT=.38;CLEARCOAT_ROUGHNESS=.3;
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
    ROUGHNESS=.84;SPECULAR=.28;
  }
  if(detail>.5){
    float h=hash(floor(p*90.0));
    float sparkle=h<.996?0.0:step(.996,h)*pow(max(sin(weather_clock*1.6+h*40.0),0.0),12.0);
    EMISSION=vec3(.58,.74,.9)*sparkle*.12;
  }
}
""";
}

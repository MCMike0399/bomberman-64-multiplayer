using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    private static Shader? _shipTimberShader;
    private static readonly Dictionary<GraphicsPreset,Shader> ShipOceanShaders=[];
    public ShaderMaterial ShipTimber(Color paint,bool vertical=false,int style=0,bool sailShadow=false)
    {
        var material=new ShaderMaterial{Shader=_shipTimberShader??=new Shader{Code=ShipTimberShader}};
        material.SetShaderParameter("paint",paint);material.SetShaderParameter("vertical_grain",vertical);
        material.SetShaderParameter("surface_style",style);material.SetShaderParameter("sail_shadow",sailShadow);
        GraphicsQuality.ApplyShaderDetail(material);return material;
    }

    /// <summary>Retains the shared water tiers, adding only this ocean's quiet
    /// whitecaps and bow crescent. Other stages keep their original shader.</summary>
    public static Shader SeaWaterShaderFor(GraphicsPreset preset)
    {
        if(ShipOceanShaders.TryGetValue(preset,out var saved))return saved;
        string code=WaterShaderFor(preset).Code;
        const string anchor="  ALBEDO=mix(body,foam_color.rgb,foam*.72);";
        if(!code.Contains(anchor,StringComparison.Ordinal))throw new InvalidOperationException("Shared water accent anchor changed.");
        code=code.Replace("uniform float wind_strength=1.0;","uniform float wind_strength=1.0;\nuniform mat4 ship_inverse=mat4(1.0);\nuniform float ambient_clock=0.0;",StringComparison.Ordinal);
        code=code.Replace("float contact=smoothstep(.03,.07,thickness)*(1.0-smoothstep(.13,.2,thickness));",
            "float contact=smoothstep(.02,.05,thickness)*(1.0-smoothstep(.08,.13,thickness));",StringComparison.Ordinal)
            .Replace("float foam=max(contact,band*.55);","float foam=max(contact,band*.15);",StringComparison.Ordinal);
        code=code.Replace(anchor,"""
  // Sparse crest tips and a small open crescent ahead of the raised bow.
  vec3 ship=(ship_inverse*vec4(world,1.0)).xyz;
  float caps=smoothstep(.047,.070,w.x)*smoothstep(.76,.91,cloud)*.20;
  float radius=length(ship.xz-vec2(24.1,9.5));
  float crescent=(1.0-smoothstep(.08,.25,abs(radius-(2.0+sin(ambient_clock*.9)*.05))))
      *smoothstep(23.7,24.4,ship.x)*.32;
  foam=max(foam,max(caps,crescent));
  ALBEDO=mix(body,foam_color.rgb,foam*.72);
""",StringComparison.Ordinal);
        return ShipOceanShaders[preset]=new Shader{Code=code};
    }

    private const string ShipTimberShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform vec4 paint : source_color;
uniform bool vertical_grain=false;
uniform int surface_style=0;
uniform bool sail_shadow=false;
uniform mat4 ship_inverse=mat4(1.0);
uniform float ship_roll=0.0;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
varying vec3 ship_point;
varying float top_face;
void vertex(){
 ship_point=(ship_inverse*MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
 top_face=normalize(MODEL_NORMAL_MATRIX*NORMAL).y;
}
void fragment(){
 vec2 p=vertical_grain?UV.yx:UV;
 float curl=sin(p.x*6.28318+sin(p.y*3.14159)*.25)*.32;
 float grain=sin(p.y*43.0+curl*3.0)*.018+sin(p.y*19.0+curl)*.026;
 float end_wear=smoothstep(.03,.13,min(p.x,1.0-p.x));
 vec3 timber=paint.rgb*(.95+grain*quality_detail)*mix(.94,1.0,end_wear);
 if(surface_style==1){
   float fan=(ship_point.z-9.5)*5.0/max(.5,25.8-ship_point.x);
   float joint=min(fract(fan),1.0-fract(fan)),aa=max(fwidth(fan),.008);
   float joint_width=min(.24,max(.035,.075*5.0/max(.5,25.8-ship_point.x)));
   float seam=1.0-smoothstep(joint_width-aa,joint_width+aa,joint);
   seam*=1.0-smoothstep(.15,.45,fwidth(fan));
   timber*=1.0-seam*.24;
 }else if(surface_style==2){
   float boot=1.0-smoothstep(-1.80,-1.66,ship_point.y);
   timber=mix(paint.rgb,vec3(.24,.065,.052),boot);
   timber*=.97+sin(ship_point.x*.8+ship_point.y*3.0)*.018;
 }
 if(sail_shadow&&top_face>.75){
   vec2 q=ship_point.xz-vec2(11.0+ship_roll*40.0,9.5+ship_roll*20.0);
   float edge=1.0-(q.x+2.5)/9.0-(q.y+4.0)/10.5;
   float shade=smoothstep(0.0,.35,q.x+2.5)*smoothstep(0.0,.35,q.y+4.0)*smoothstep(0.0,.045,edge);
   timber*=1.0-shade*.115;
 }
 ALBEDO=timber;
 ROUGHNESS=.77-quality_premium*.055;SPECULAR=.3;
 CLEARCOAT=quality_premium*.18;CLEARCOAT_ROUGHNESS=.3;
}
""";
}

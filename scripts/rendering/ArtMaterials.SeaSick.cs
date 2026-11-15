using Godot;
namespace BlastCircuit.Rendering;

public sealed partial class ArtMaterials
{
    private static Shader? _shipTimberShader;
    public ShaderMaterial ShipTimber(Color paint,bool vertical=false)
    {
        var material=new ShaderMaterial{Shader=_shipTimberShader??=new Shader{Code=ShipTimberShader}};
        material.SetShaderParameter("paint",paint);material.SetShaderParameter("vertical_grain",vertical);GraphicsQuality.ApplyShaderDetail(material);return material;
    }
    private const string ShipTimberShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform vec4 paint : source_color;
uniform bool vertical_grain=false;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
void fragment(){
 vec2 p=vertical_grain?UV.yx:UV;
 float curl=sin(p.x*6.28318+sin(p.y*3.14159)*.25)*.32;
 float grain=sin(p.y*43.0+curl*3.0)*.018+sin(p.y*19.0+curl)*.026;
 float end_wear=smoothstep(.03,.13,min(p.x,1.0-p.x));
 ALBEDO=paint.rgb*(.95+grain*quality_detail)*mix(.94,1.0,end_wear);
 ROUGHNESS=.77-quality_premium*.04;SPECULAR=.3;
 CLEARCOAT=quality_premium*.18;CLEARCOAT_ROUGHNESS=.3;
}
""";
}

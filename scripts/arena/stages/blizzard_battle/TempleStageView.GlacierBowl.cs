using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private ShaderMaterial? _glacierPennantPaint;
    private OmniLight3D? _glacierWarmLight;
    private Node3D? _glacierLeeExtras;

    private void BuildGlacierBowl()
    {
        var paint=new ShaderMaterial{Shader=StageShader(GlacierBowlShader)};
        var ice=new ShaderMaterial{Shader=StageShader(GrottoIceShader)};_frozenMaterials.Add(ice);
        GraphicsQuality.ApplyShaderDetail(ice);
        var body=GlacierGrottoMesh(Width,Depth,paint,ice);
        AddChild(new MeshInstance3D{Name="GlacierBowlBody",Mesh=body});
        float crownZ=(GrottoBack+GrottoFront)/2,crown=CapturedGrottoRoof(body,GrottoCenterX,crownZ);
        AddChild(new MeshInstance3D{Name="GlacierGrottoCrown",Mesh=GrottoCrystalMesh(body,Width,Depth),
            Position=new(GrottoCenterX,crown,crownZ),MaterialOverride=_crystalMaterial});

        // The pond and penguins now share the lee of the same carved shelter.
        var lee=Prop(this,"GlacierLeePond",new(-4.2f,-.26f,11),0,.8f);
        Cylinder(lee,new(0,.01f,0),2.1f,.04f,_art.Color(new("9fd3ea"),.08f)).Name="FrozenPond";
        Cylinder(lee,Vector3.Zero,2.35f,.035f,_art.Color(new("dbe6f0"),.75f));
        Penguin(lee,new(-.6f,0,-2.6f),0);
        _glacierLeeExtras=Prop(lee,"PenguinCompanions",Vector3.Zero);
        Penguin(_glacierLeeExtras,new(.5f,0,-2.4f),1.3f);Penguin(_glacierLeeExtras,new(1.9f,0,1.9f),2.6f);

        var mast=Prop(this,"GlacierWindLantern",new(Width+1.35f,.48f,Depth-3.4f));
        var wood=_art.Color(new("786478"),.87f);var brass=_art.Color(new("bc995c"),.58f);
        RoundedBox(mast,new(0,1.2f,0),new(.16f,2.4f,.16f),wood,.5f);
        Sphere(mast,new(0,2.48f,0),Vector3.One*.27f,brass);
        _glacierPennantPaint=new ShaderMaterial{Shader=StageShader(GlacierPennantShader)};
        Plate(mast,new(0,2.15f,0),[new(0,0),new(1.25f,.04f),new(1.1f,-.49f),new(0,-.43f)],.035f,_glacierPennantPaint).Name="GlacierWindPennant";
        // The accepted-wind pennant stays. Only its lantern moves into the vault.
        var lantern=Prop(this,"GlacierVaultLantern",new(-4.40f,-.26f,GrottoFront-1.05f));
        RoundedBox(lantern,new(0,.05f,0),new(.7f,.1f,.56f),brass,.4f);
        RoundedBox(lantern,new(0,.43f,0),new(.56f,.69f,.48f),wood,.19f);
        Box(lantern,new(0,.44f,.255f),new(.41f,.49f,.035f),Glow(new("ffc279"),1.5f),.045f).Name="GlacierWarmWindow";
        RoundedBox(lantern,new(0,.82f,0),new(.68f,.15f,.6f),brass,.5f);
        _glacierWarmLight=new OmniLight3D{Name="GlacierWarmLight",Position=new(0,.55f,.4f),
            LightColor=new("ffcb8c"),LightEnergy=1.0f,OmniRange=5,ShadowEnabled=false,
            Visible=GraphicsQuality.Current!=GraphicsPreset.Low};lantern.AddChild(_glacierWarmLight);
    }

    private void ProcessGlacierBowl()
    {
        if(_glacierPennantPaint!=null)
        {
            _glacierPennantPaint.SetShaderParameter("accepted_wind",_blizzardWind);
            _glacierPennantPaint.SetShaderParameter("weather_clock",_blizzardClock);
        }
        if(_glacierWarmLight!=null)
        {_glacierWarmLight.Visible=GraphicsQuality.Current!=GraphicsPreset.Low;_glacierWarmLight.LightEnergy=_lighting?.Label=="Night"?1.2f:.05f;}
        if(_glacierLeeExtras!=null)_glacierLeeExtras.Visible=GraphicsQuality.Current!=GraphicsPreset.Low;
    }

    private const string GlacierBowlShader="""
shader_type spatial;
render_mode diffuse_burley;
varying vec3 world;
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
void fragment(){
    float powder=smoothstep(-.20,.29,world.y);
    float broad=.5+.5*sin(world.x*.24+world.z*.18);
    vec3 blue=mix(vec3(.43,.57,.65),vec3(.53,.70,.77),smoothstep(-.3,.15,world.y));
    ALBEDO=mix(blue,mix(vec3(.70,.82,.86),vec3(.88,.93,.94),broad),powder);
    ROUGHNESS=.88;SPECULAR=.19;
}
""";
    private const string GrottoIceShader="""
shader_type spatial;
render_mode diffuse_burley, depth_prepass_alpha;
uniform float quality_detail=1.0;
uniform float quality_premium=0.0;
uniform vec3 sun_direction=vec3(.3,.85,.42);
uniform vec4 light_tint:source_color=vec4(1.0);
varying vec3 world;
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
void fragment(){
    float band=.5+.5*sin(world.y*1.7+world.x*.28);
    ALBEDO=mix(vec3(.43,.67,.77),vec3(.66,.72,.86),band*.48);
    ROUGHNESS=mix(.43,.24,quality_premium);SPECULAR=.4;RIM=.18;RIM_TINT=.5;
    CLEARCOAT=.32;CLEARCOAT_ROUGHNESS=.24;
    ALPHA=mix(1.0,.94,quality_premium);
}
""";
    private const string GlacierPennantShader="""
shader_type spatial;
render_mode diffuse_burley;
uniform vec2 accepted_wind=vec2(0.0);
uniform float weather_clock=0.0;
void vertex(){
    float along=VERTEX.x;
    vec2 direction=length(accepted_wind)>.04?accepted_wind:vec2(.3,.04);
    VERTEX.x=direction.x*along*.78;
    VERTEX.z+=direction.y*along*.78;
    VERTEX.y-=along*.15/(1.0+length(accepted_wind));
    VERTEX.y+=sin(weather_clock*3.0-along*3.0)*.045*along*min(1.0,length(accepted_wind));
    NORMAL=normalize(vec3(-direction.y,0.0,direction.x));
}
void fragment(){ALBEDO=mix(vec3(.54,.08,.16),vec3(.90,.38,.30),UV.y);ROUGHNESS=.85;}
""";
}

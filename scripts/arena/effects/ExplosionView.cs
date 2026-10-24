using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Rendering;
namespace BlastCircuit.Arena.Effects;

/// <summary>A radius-faithful danger shell, then a purely cosmetic flash/billow/vapor sequence.</summary>
public partial class ExplosionView : Node3D
{
    private static readonly Shader CloudShader=new() {Code="""
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never;
uniform vec4 hot : source_color = vec4(1.0,.78,.22,1.0);
uniform float age=0.0;
uniform float duration=.7;
uniform bool shell=false;
uniform bool blue=false;
void fragment(){
 float p=clamp(age/duration,0.0,1.0);
 float rim=pow(1.0-max(dot(normalize(NORMAL),normalize(VIEW)),0.0),2.0);
 float swirl=sin(UV.x*26.0+age*12.0)*sin(UV.y*19.0-age*9.0)*.5+.5;
 vec3 flame=mix(hot.rgb*.65,(blue?vec3(.02,.42,1.0):vec3(1.0,.37,.015)),swirl*.6+.2);
 flame+=vec3(.13,.035,0.0)*max(NORMAL.y,0.0);
 float smoke=smoothstep(.34,.55,age);
 ALBEDO=mix(flame,vec3(.42,.34,.27),smoke);
 if(shell){ ALBEDO=blue?vec3(.12,.72,1.0):vec3(1.0,.63,.12); ALPHA=rim*.22*(1.0-smoothstep(.31,.4,age)); }
 else { ALPHA=(.48+swirl*.16)*(1.0-smoothstep(.22,1.0,p)); }
}
"""};
    private static readonly TorusMesh RingMesh=new() {InnerRadius=.96f,OuterRadius=1,Rings=48,RingSegments=8};
    private readonly ShaderMaterial _cloud,_boundary;
    private readonly MeshInstance3D _core,_ring,_shell;
    private readonly List<(MeshInstance3D Mesh,Vector3 Direction,float Offset)> _lobes=[];
    private readonly float _radius,_duration;
    private float _age;
    public string Tier {get;}
    public float Radius=>_radius;
    public float Age=>_age;
    public bool HazardActive=>_age<.4f;
    public string Phase=>_age<.12f?"flash":_age<.4f?"bloom":"vapor";
    public float VisualExtent {get;private set;}
    public int LobeCount=>_lobes.Count;
    public ExplosionView(ArtMaterials art,float radius,bool red,float initialAge=0,bool reducedDetail=false)
    {
        _radius=radius;_age=initialAge;
        Tier=radius<=1.6f?"small":radius<=2.7f?"medium":"large";
        _duration=Tier=="small"?.52f:Tier=="medium"?.74f:1.05f;
        _cloud=new() {Shader=CloudShader};_cloud.SetShaderParameter("hot",red?new Color("32afff"):new Color("ff6508"));_cloud.SetShaderParameter("duration",_duration);_cloud.SetShaderParameter("blue",red);
        _boundary=new() {Shader=CloudShader};_boundary.SetShaderParameter("shell",true);_boundary.SetShaderParameter("blue",red);_boundary.SetShaderParameter("hot",new Color("ffc44d"));
        // Full authoritative extent is readable from the first frame, before the center finishes blooming.
        _shell=Sphere(this,Vector3.Zero,Vector3.One*radius*2,_boundary);
        _core=Sphere(this,Vector3.Zero,Vector3.One,art.Color(red?new Color("d2f5ff"):new Color("fff6b0"),.8f,true));
        _ring=new MeshInstance3D {Mesh=RingMesh,MaterialOverride=art.Color(red?new Color(.2f,.8f,1,.75f):new Color(1,.86f,.47f,.75f),.9f,true)};AddChild(_ring);
        int count=reducedDetail?4:Tier=="small"?6:Tier=="medium"?10:14;
        for(int i=0;i<count;i++)
        {
            float angle=i*2.39996f,y=.15f+(i%4)*.19f;
            var direction=new Vector3(Mathf.Cos(angle),y,Mathf.Sin(angle)).Normalized();
            var lobe=Sphere(this,Vector3.Zero,Vector3.One,_cloud);
            _lobes.Add((lobe,direction,i*.63f));
        }
        foreach(var mesh in GetChildren().OfType<GeometryInstance3D>())mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        Scale=new(1,TempleStageView.HeightScale,1);
        Render();
    }
    public override void _Process(double delta)
    {
        _age+=(float)delta;
        if(_age>=_duration){QueueFree();return;}
        Render();
    }
    private void Render()
    {
        float growth=1-Mathf.Exp(-_age*30),tail=Mathf.Clamp((_age-.4f)/(_duration-.4f),0,1);
        _cloud.SetShaderParameter("age",_age);_boundary.SetShaderParameter("age",_age);
        _shell.Visible=HazardActive;
        _core.Visible=_age<.2f;_core.Scale=Vector3.One*_radius*(.15f+growth*.9f)*(1-Mathf.Clamp((_age-.08f)/.14f,0,1));
        _ring.Visible=HazardActive;_ring.Position=new(0,-.12f,0);_ring.Scale=new(_radius*(.6f+.4f*growth),.22f,_radius*(.6f+.4f*growth));
        _ring.Transparency=Mathf.Clamp((_age-.12f)/.28f,0,1);
        VisualExtent=_radius*(.55f+.45f*growth);
        foreach(var (mesh,dir,offset) in _lobes)
        {
            float wobble=1+Mathf.Sin(_age*18+offset)*.055f;
            mesh.Position=dir*_radius*(.32f+.18f*growth)+Vector3.Up*tail*_radius*.22f;
            mesh.Scale=Vector3.One*_radius*(.45f+.47f*growth+tail*.15f)*wobble;
        }
    }
}

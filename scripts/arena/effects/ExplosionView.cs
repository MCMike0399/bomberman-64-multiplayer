using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Rendering;
namespace BlastCircuit.Arena.Effects;

/// <summary>One expanding fireball, brief charged flares and a distinct power wave.
/// The faint mapped danger shell retains authority extent; the central fireball stays round in world space.</summary>
public partial class ExplosionView : Node3D
{
    private static readonly Shader FireShader=new() {Code="""
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never;
uniform float age=0.0;
uniform float duration=.7;
uniform bool shell=false;
uniform bool blue=false;
varying vec3 fire_normal;
void vertex(){fire_normal=NORMAL;}
void fragment(){
 float rim=pow(1.0-max(dot(normalize(NORMAL),normalize(VIEW)),0.0),1.6);
 vec3 n=normalize(fire_normal);
 float cells=sin(n.x*9.0+n.y*4.0+age*17.0)*sin(n.z*8.0-n.y*7.0-age*13.0);
 float heat=smoothstep(-.25,.65,cells)*(1.0-smoothstep(.20,.46,age));
 vec3 edge=blue?vec3(.02,.74,.76):vec3(1.0,.23,.015);
 vec3 center=blue?vec3(.78,1.0,.96):vec3(1.0,.96,.53);
 ALBEDO=mix(edge,center,clamp(heat*.8+(1.0-rim)*.3,0.0,1.0));
 if(shell){ALBEDO=edge;ALPHA=rim*.10*(1.0-smoothstep(.32,.4,age));}
 else {ALPHA=(.83+heat*.12)*(1.0-smoothstep(.28,duration,age));}
}
"""};
    private static readonly SphereMesh BodyMesh=new() {Radius=.5f,Height=1,RadialSegments=24,Rings=12};
    private static readonly TorusMesh WaveMesh=new() {InnerRadius=.88f,OuterRadius=1,Rings=48,RingSegments=8};
    private static readonly CylinderMesh FlareMesh=new() {TopRadius=0,BottomRadius=.19f,Height=1,RadialSegments=3,Rings=1};
    private readonly ShaderMaterial _fire,_boundary;
    private readonly MeshInstance3D _body,_core,_wave,_shell;
    private readonly List<(MeshInstance3D Mesh,Vector3 Direction)> _flares=[];
    private readonly float _radius,_duration;
    private readonly bool _power;
    private float _age;
    public string Tier {get;}
    public float Radius=>_radius;
    public float Age=>_age;
    public bool HazardActive=>_age<.4f;
    public string Phase=>_age<.12f?"flash":_age<.4f?"bloom":"vapor";
    public float VisualExtent {get;private set;}
    public int BodyCount=>1;
    public int FlareCount=>_flares.Count;
    public bool PowerWaveVisible=>_wave.Visible;
    public ExplosionView(ArtMaterials art,float radius,bool red,float initialAge=0,bool reducedDetail=false)
    {
        _radius=radius;_age=initialAge;_power=red;
        Tier=radius<=1.6f?"small":radius<=2.7f?"medium":"large";
        _duration=Tier=="small"?.52f:Tier=="medium"?.68f:.82f;
        _fire=new() {Shader=FireShader};_fire.SetShaderParameter("duration",_duration);_fire.SetShaderParameter("blue",red);
        _boundary=new() {Shader=FireShader};_boundary.SetShaderParameter("shell",true);_boundary.SetShaderParameter("blue",red);
        _shell=Sphere(this,Vector3.Zero,Vector3.One*radius*2,_boundary);
        // Terrain maps authority Y to render Y. Compensate only the cosmetic body,
        // retaining that mapping on the full-radius danger shell and effect origin.
        var fireball=new Node3D {Scale=new(1,1/TempleStageView.HeightScale,1)};AddChild(fireball);
        _body=new MeshInstance3D {Mesh=BodyMesh,MaterialOverride=_fire};fireball.AddChild(_body);
        _core=Sphere(fireball,Vector3.Zero,Vector3.One,art.Color(red?new Color("eafffa"):new Color("fff9d8"),.8f,true));
        _wave=new MeshInstance3D {Mesh=WaveMesh,MaterialOverride=art.Color(new Color(.62f,1,.94f,.8f),.9f,true)};fireball.AddChild(_wave);
        int count=Tier=="small"?0:reducedDetail?4:Tier=="medium"?6:10;
        for(int i=0;i<count;i++)
        {
            float angle=i*Mathf.Tau/count;
            var direction=new Vector3(Mathf.Cos(angle),.15f+(i%3)*.26f,Mathf.Sin(angle)).Normalized();
            var flare=new MeshInstance3D {Mesh=FlareMesh,MaterialOverride=_fire,Quaternion=new Quaternion(Vector3.Up,direction)};
            fireball.AddChild(flare);_flares.Add((flare,direction));
        }
        _shell.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        foreach(var mesh in fireball.GetChildren().OfType<GeometryInstance3D>())mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
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
        _fire.SetShaderParameter("age",_age);_boundary.SetShaderParameter("age",_age);
        _shell.Visible=HazardActive;
        VisualExtent=_radius*(.5f+.5f*growth);
        _body.Scale=Vector3.One*2*VisualExtent*(1+tail*.08f);
        _core.Visible=_age<.18f;_core.Scale=Vector3.One*_radius*(.15f+growth*1.15f)*(1-Mathf.Clamp((_age-.07f)/.12f,0,1));
        _wave.Visible=_power&&_age<.30f;
        _wave.Position=new(0,.025f,0);_wave.Scale=new(_radius*(.35f+growth),.08f,_radius*(.35f+growth));
        _wave.Transparency=Mathf.Clamp((_age-.07f)/.23f,0,1);
        float flareGrowth=Mathf.Sin(Mathf.Clamp(_age/.28f,0,1)*Mathf.Pi);
        foreach(var (mesh,direction) in _flares)
        {
            mesh.Visible=_age<.28f;
            mesh.Position=direction*_radius*(.68f+.18f*growth);
            mesh.Scale=new Vector3(_radius*.8f,_radius*.75f,_radius*.8f)*Math.Max(.001f,flareGrowth);
        }
    }
}

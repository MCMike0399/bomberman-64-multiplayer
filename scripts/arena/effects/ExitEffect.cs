using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Rendering;
namespace BlastCircuit.Arena.Effects;

/// <summary>Brief water plume/ripples or star puff at an accepted elimination point.</summary>
public partial class ExitEffect : Node3D
{
    private readonly List<(MeshInstance3D Mesh,Vector3 Velocity)> _particles=[];
    private readonly MeshInstance3D? _ring;
    private readonly bool _water;
    private float _age;
    public ExitEffect(ArtMaterials art,bool water)
    {
        _water=water;
        if(water)
        {
            _ring=new() {Mesh=new TorusMesh {InnerRadius=.9f,OuterRadius=1,Rings=40,RingSegments=8},MaterialOverride=art.Color(new Color(.82f,.98f,1,.7f),.8f,true)};AddChild(_ring);
        }
        for(int i=0;i<(water?12:8);i++)
        {
            float angle=i*Mathf.Tau/(water?12:8);
            var node=Sphere(this,Vector3.Zero,Vector3.One*(water?.14f:.18f),art.Color(water?new Color("b9f4ff"):new Color("ffe06c"),.6f,true));
            node.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
            _particles.Add((node,new(Mathf.Cos(angle)*(water?1.1f:.8f),1.5f+(i%3)*.3f,Mathf.Sin(angle)*(water?1.1f:.8f))));
        }
    }
    public override void _Process(double delta)
    {
        _age+=(float)delta;
        if(_age>.8f){QueueFree();return;}
        foreach(var (p,v) in _particles){p.Position=v*_age+Vector3.Down*(_age*_age*2.4f);p.Transparency=Mathf.Clamp((_age-.25f)/.55f,0,1);}
        if(_ring!=null){_ring.Scale=new(.2f+_age*1.8f,.1f,.2f+_age*1.8f);_ring.Transparency=_age/.8f;}
    }
}

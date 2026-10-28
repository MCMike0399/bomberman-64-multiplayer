using Godot;
using BlastCircuit.Arena.Models;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena.Effects;

/// <summary>A comic paper-flat wall victim; only the stand-in follows the rendered lethal face.</summary>
public partial class CrushEffect : Node3D
{
    public const float PuffAt=1.25f,Lifetime=1.95f;
    private readonly Node3D _paper=new();
    private readonly BomberModel _puppet;
    private readonly List<(MeshInstance3D Mesh,Vector3 Velocity)> _puffs=[];
    private readonly Func<float>? _face;
    private readonly float _inward;
    private float _age;
    public float Age=>_age;
    public BomberModel Puppet=>_puppet;
    public Vector3 PaperScale=>_paper.Scale;
    public CrushEffect(ArtMaterials art,Color suit,bool left,Func<float>? face=null)
    {
        _face=face;_inward=left?1:-1;
        AddChild(_paper);_puppet=new BomberModel(art,suit,"");_paper.AddChild(_puppet);
        for(int i=0;i<9;i++)
        {
            float angle=i*Mathf.Tau/9;
            var puff=Sphere(this,Vector3.Zero,Vector3.One,art.Color(new Color("e8dfca"),.94f));
            puff.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;puff.Visible=false;
            _puffs.Add((puff,new(Mathf.Cos(angle),.5f+i%3*.2f,Mathf.Sin(angle))));
        }
        Pose();
    }
    public override void _Process(double delta)
    {
        _age+=(float)delta;
        if(_age>=Lifetime){QueueFree();return;}
        Pose();
    }
    private void Pose()
    {
        // At contact the flat plane lies against the X-facing wall, then swivels to reveal the gag.
        float press=Mathf.SmoothStep(0,.16f,_age),turn=Mathf.SmoothStep(.18f,.55f,_age);
        float wobble=Mathf.Sin((_age-.18f)*19)*Mathf.Exp(-Mathf.Max(0,_age-.18f)*3)*turn;
        _paper.Scale=new(1+.18f*press,1+.1f*press,1-.88f*press);
        _paper.Rotation=new(wobble*.16f,_inward*Mathf.Pi*.5f*(1-turn),wobble*.18f);
        _paper.Position=new(0,Mathf.Sin(Mathf.Clamp((_age-.16f)/.6f,0,1)*Mathf.Pi)*.32f,0);
        if(_face!=null)Position=new(_face()+_inward*(.16f+turn*.65f),Position.Y,Position.Z);
        _puppet.AnimateCrush(_age);_paper.Visible=_age<PuffAt+.12f;
        float poof=_age-PuffAt;
        foreach(var (mesh,velocity) in _puffs)
        {
            mesh.Visible=poof>=0;if(poof<0)continue;
            float spread=1-Mathf.Exp(-poof*6);
            mesh.Position=new Vector3(0,.8f,0)+velocity*spread*.65f;
            mesh.Scale=Vector3.One*(.24f+.3f*spread);
            mesh.Transparency=Mathf.Clamp((poof-.12f)/.5f,0,1);
        }
    }
}

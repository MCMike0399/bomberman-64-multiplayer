using Godot;
using Bomber.Game.Simulation;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena.Effects;

/// <summary>A server-published fixed impact target with a falling fireball.</summary>
public partial class MeteorView : Node3D
{
    private readonly Node3D _ball=new();
    private readonly MeshInstance3D _warning;
    private float _remaining;
    public MeteorView(ArtMaterials art)
    {
        _warning=new MeshInstance3D{Mesh=new TorusMesh{InnerRadius=1.18f,OuterRadius=1.3f,Rings=24,RingSegments=8},
            MaterialOverride=art.Color(new("ff922f"),.7f,true),Position=new(0,.035f,0),Scale=new(1,.15f,1)};
        AddChild(_warning);
        var shadow=art.Color(new Color(.13f,.07f,.025f,.45f));
        Cylinder(this,new(0,.012f,0),.5f,.015f,shadow);
        AddChild(_ball);
        Sphere(_ball,Vector3.Zero,Vector3.One*.95f,art.Color(new("f99b28"),.5f,true));
        Sphere(_ball,new(0,.12f,.05f),Vector3.One*.63f,art.Color(new("ffe7a1"),.4f,true));
        for(int i=0;i<3;i++)Sphere(_ball,new(.07f*Mathf.Sin(i*3),.55f+i*.35f,0),new(.65f-i*.16f,.9f,.65f-i*.16f),art.Color(new("e76b20"),.8f,true));
    }
    public void SetRemaining(float value){_remaining=value;Pose();}
    public override void _Process(double delta){_remaining=Math.Max(0,_remaining-(float)delta);Pose();}
    private void Pose()
    {
        float t=1-Mathf.Clamp(_remaining/BlastStages.MeteorWarningSeconds,0,1);
        _ball.Position=new(0,(1-t*t)*11+.3f,0);
        float pulse=1+Mathf.Sin(t*Mathf.Tau*4)*.035f;_warning.Scale=new(pulse,.15f,pulse);
    }
}

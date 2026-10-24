using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Rendering;
namespace BlastCircuit.Arena.Effects;

/// <summary>Short original contact flash; purely visual, with no collision or damage.</summary>
public partial class KickBurst : Node3D
{
    private readonly List<(MeshInstance3D Mesh,Vector3 Velocity)> _rays=[];
    private float _age;
    public KickBurst(ArtMaterials art,Vector3 direction)
    {
        var side=new Vector3(direction.Z,0,-direction.X);
        for(int i=0;i<7;i++)
        {
            float spread=(i-3)*.23f;
            var velocity=(direction*(1.6f+Mathf.Abs(spread))+side*spread+Vector3.Up*(.6f+(i%3)*.3f));
            var ray=Sphere(this,Vector3.Zero,new(.06f,.06f,.17f),art.Color(new("ffe4a1"),.65f,true));
            ray.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;_rays.Add((ray,velocity));
        }
    }
    public override void _Process(double delta)
    {
        _age+=(float)delta;if(_age>.22f){QueueFree();return;}
        foreach(var (ray,velocity) in _rays){ray.Position=velocity*_age;ray.Transparency=_age/.22f;ray.Scale=new Vector3(.06f,.06f,.17f)*(1+_age*2);}
    }
}

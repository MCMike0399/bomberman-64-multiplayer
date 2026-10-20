using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Rendering;

/// <summary>Distinct silhouettes communicate the reward before the player touches it.</summary>
public partial class PowerupModel : Node3D
{
    private readonly Node3D _icon=new();
    private float _time;
    public string Kind {get;}
    public PowerupModel(ArtMaterials art,string kind)
    {
        Kind=kind;AddChild(_icon);
        var white=art.Color(new("fff9dc"),.4f);var ink=art.Color(new("253453"),.3f);var gold=art.Color(new("ffd653"),.4f,true);
        Cylinder(this,new(0,-.3f,0),.3f,.05f,gold);
        switch(kind)
        {
            case "fire":
                Sphere(_icon,new(0,-.025f,0),new(.34f,.48f,.27f),art.Color(new("ff7438"),.45f,true));
                Sphere(_icon,new(.06f,.2f,0),new(.13f,.28f,.12f),art.Color(new("ffb93c"),.4f,true));
                Sphere(_icon,new(-.04f,-.1f,.1f),new(.15f,.25f,.12f),gold);break;
            case "heart":
                var pink=art.Color(new("f65391"),.4f);
                Sphere(_icon,new(-.105f,.08f,0),new(.25f,.25f,.18f),pink);Sphere(_icon,new(.105f,.08f,0),new(.25f,.25f,.18f),pink);
                var tip=Box(_icon,new(0,-.05f,0),new(.23f,.23f,.17f),pink,.05f);tip.RotationDegrees=new(0,0,45);break;
            case "remote":
                Box(_icon,new(0,0,0),new(.3f,.4f,.17f),white,.045f);
                Cylinder(_icon,new(.07f,.28f,0),.025f,.23f,ink);
                Sphere(_icon,new(.07f,.41f,0),Vector3.One*.07f,art.Color(new("eb4567")));
                Sphere(_icon,new(0,.08f,.11f),Vector3.One*.09f,art.Color(new("ed4b55")));
                Box(_icon,new(0,-.08f,.1f),new(.16f,.055f,.035f),ink,.01f);break;
            default:
                Sphere(_icon,Vector3.Zero,Vector3.One*.4f,kind=="red"?art.Color(new("e8384e"),.25f):ink);
                Cylinder(_icon,new(0,.24f,0),.035f,.14f,gold);
                if(kind=="bomb")
                {
                    Box(_icon,new(.19f,.01f,.2f),new(.22f,.065f,.04f),white,.015f);
                    Box(_icon,new(.19f,.01f,.2f),new(.065f,.22f,.04f),white,.015f);
                }
                break;
        }
    }
    public override void _Process(double delta)
    {
        _time+=(float)delta;_icon.Position=new(0,Mathf.Sin(_time*3)*.075f,0);_icon.Rotation=new(0,_time*1.1f,0);
    }
}

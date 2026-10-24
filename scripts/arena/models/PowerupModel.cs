using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Rendering;
namespace BlastCircuit.Arena.Models;

/// <summary>Distinct silhouettes communicate the reward before the player touches it.</summary>
public partial class PowerupModel : Node3D
{
    public const float VisualScale=1.20f;
    public const float IconScale=1.30f;
    private const float IconLift=.16f;
    private readonly Node3D _icon=new();
    private float _time;
    public string Kind {get;}
    public PowerupModel(ArtMaterials art,string kind)
    {
        Kind=kind;Scale=Vector3.One*VisualScale;
        _icon.Name="Icon";_icon.Scale=Vector3.One*IconScale;_icon.Position=new(0,IconLift,0);AddChild(_icon);
        var white=art.Color(new("fff9dc"),.4f);var ink=art.Color(new("253453"),.3f);var gold=art.Color(new("ffd653"),.4f,true);
        Cylinder(this,new(0,-.3f,0),.3f,.05f,gold);
        switch(kind)
        {
            case "tiny":case "big":
                var size=kind=="tiny"?.22f:.42f;
                var tint=art.Color(new(kind=="tiny"?"70e6ff":"f6a156"),.35f,true);
                Sphere(_icon,new(0,.08f,0),Vector3.One*size,white);
                Box(_icon,new(0,-.17f,0),new(size*.7f,.2f,size*.65f),tint,.04f);
                foreach(float side in new[]{-1f,1f})
                    Box(_icon,new(side*.24f,kind=="tiny"?-.04f:.18f,0),new(.07f,.23f,.07f),tint,.02f);
                break;
            case "haste":case "slow":
                var boot=art.Color(new(kind=="haste"?"ffdc56":"9b92e7"),.35f,true);
                Box(_icon,new(0,-.04f,.04f),new(.4f,.19f,.26f),boot,.06f);
                Box(_icon,new(-.1f,.12f,0),new(.18f,.25f,.22f),boot,.04f);
                for(int i=0;i<(kind=="haste"?3:1);i++)Box(_icon,new(-.29f,.18f-i*.12f,0),new(.17f,.035f,.05f),white,.01f);
                break;
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
            case "bounce":
                var purple=art.Color(new("a365ea"),.35f);
                Sphere(_icon,new(0,.14f,0),Vector3.One*.3f,purple);
                for(int i=0;i<3;i++)Cylinder(_icon,new(0,-.22f+i*.09f,0),.16f-i*.025f,.035f,white);
                break;
            case "fast":
                var mint=art.Color(new("43d9a1"),.35f,true);
                for(int i=0;i<2;i++)
                {
                    var upper=Box(_icon,new(-.12f+i*.22f,.09f,0),new(.26f,.075f,.1f),mint,.02f);upper.RotationDegrees=new(0,0,-40);
                    var lower=Box(_icon,new(-.12f+i*.22f,-.09f,0),new(.26f,.075f,.1f),mint,.02f);lower.RotationDegrees=new(0,0,40);
                }
                break;
            case "focus":
                var cyan=art.Color(new("70e6ff"),.35f,true);
                foreach(float side in new[]{-1f,1f})
                {
                    Box(_icon,new(side*.22f,0,0),new(.07f,.18f,.08f),cyan,.01f);
                    Box(_icon,new(0,side*.22f,0),new(.18f,.07f,.08f),cyan,.01f);
                }
                Sphere(_icon,Vector3.Zero,Vector3.One*.13f,white);break;
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
        _time+=(float)delta;_icon.Position=new(0,IconLift+Mathf.Sin(_time*3)*.075f,0);_icon.Rotation=new(0,_time*1.1f,0);
    }
}

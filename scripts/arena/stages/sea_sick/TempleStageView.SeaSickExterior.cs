using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildSeaSickExterior()
    {
        var scenery=_scenery!;
        // Sea life remains in fixed world space while the playable ship rocks.
        scenery.TopLevel=true;
        var boat=Sailboat(scenery,new(-7,-2.1f,5),.4f,new("914d3d"),new("e8d9b7"),1);
        boat.Name="PortEscort";boat.Scale=Vector3.One*1.65f;
        var cutter=Sailboat(scenery,new(Width+8,-2.1f,Depth-5),-.7f,new("3c706b"),new("eee2c7"),3);
        cutter.Name="StarboardEscort";cutter.Scale=Vector3.One*1.35f;
        foreach(var (x,z,phase) in new[]{(-5f,-4f,0f),(Width+5f,4f,2f),(-6f,Depth+3f,4f)})
            Gull(scenery,new(x,1.1f,z),2.0f,.16f,phase);
        Buoy(scenery,new(-5,-2.1f,Depth+2),new("bc6846"),.5f);
        Buoy(scenery,new(Width+6,-2.1f,-3),new("3c8d81"),1.5f);
        // A small distant harbor cue, never an apparent route off the deck.
        var islet=Prop(scenery,"HarborIslet",new(Width+15,-2.9f,-9));
        Sphere(islet,new(0,.15f,0),new(8,1.5f,5),_art.Color(new("9f9b79"),.95f));
        Sphere(islet,new(0,.48f,0),new(6.8f,.7f,4),_art.Color(new("698d61"),.95f));
        Box(islet,new(0,.9f,0),new(2,.9f,1.6f),_art.Color(new("e1c393"),.9f),.12f);
        var roof=Box(islet,new(0,1.5f,0),new(2.3f,.22f,2),_art.Color(new("5e788b"),.85f),.07f);
        roof.Basis=BlastCircuit.Rendering.PresentationMath.Rotation(new(0,0,.08f));
    }
}

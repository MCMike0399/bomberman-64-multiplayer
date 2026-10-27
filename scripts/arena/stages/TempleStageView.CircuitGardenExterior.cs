using Godot;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    /// <summary>The training academy's harbor: a palm islet, a mooring jetty with a
    /// sailboat, channel buoys, a floating target raft and gulls. Kept to the rear
    /// and sides so the tutorial's instruction space stays clear.</summary>
    private void BuildCircuitGardenExterior()
    {
        var scenery=_scenery!;
        float sea=-.53f;
        var sand=_art.Color(new("e6cf96"),.95f);var turf=_art.Color(new("6fa456"),.95f);
        var plank=_art.Color(new("9a7650"),.85f);var post=_art.Color(new("6b4d33"),.85f);

        var islet=new Vector3(-4.6f,sea,-1.8f);
        Sphere(scenery,islet+new Vector3(0,-.35f,0),new(5,1.1f,3.8f),sand).Name="Islet";
        Sphere(scenery,islet+new Vector3(-.3f,-.2f,-.2f),new(3,.6f,2.2f),turf).Name="IsletTurf";
        PalmTree(scenery,islet+new Vector3(-.5f,.08f,-.3f),2.6f,.25f,.6f,0);
        PalmTree(scenery,islet+new Vector3(1,.04f,.4f),2,-.2f,2.8f,2);

        // A jetty off the west shore with a moored sailboat.
        var jetty=Prop(scenery,"Jetty",new(-.08f,sea,8.5f),-Mathf.Pi/2);
        for(int i=0;i<8;i++)Box(jetty,new(0,.32f,i*.42f),new(1,.08f,.38f),plank,.015f);
        foreach(int side in new[]{-1,1})for(int p=0;p<4;p++)Cylinder(jetty,new(side*.46f,0,p*1.05f),.07f,1.2f,post);
        Sailboat(scenery,new(-2.4f,sea,10.1f),Mathf.Pi/2,new("d0603f"),new("f7f1e3"),.5f);

        // A floating target raft echoes the range's markings.
        var raft=Prop(scenery,"TargetRaft",new(Width+3.6f,sea,1.4f),-.3f);
        Box(raft,new(0,.06f,0),new(1.8f,.16f,1.8f),plank,.03f);
        var board=Prop(raft,"TargetBoard",new(0,.95f,0));
        foreach(var (radius,color) in new[]{(.62f,"f4e8b5"),(.46f,"258d8f"),(.3f,"f4e8b5"),(.14f,"efc65b")})
            Cylinder(board,new(0,0,.02f*(1-radius)),radius,.05f,_art.Color(new(color),.7f)).Rotation=new(Mathf.Pi/2,0,0);
        Cylinder(raft,new(0,.42f,-.05f),.05f,.8f,post);
        Float(raft,.02f,1.1f,.05f);

        foreach(var (x,z,phase) in new[]{(-2f,2.5f,0f),(-2.2f,15.5f,1.2f),(Width+2f,6.5f,2.1f),(Width+2.3f,16.5f,.6f)})
            Buoy(scenery,new(x,sea,z),phase>1?new Color("3f9b4f"):new Color("d8433b"),phase);

        Gull(scenery,new(-5,6.4f,-1.5f),2.4f,.55f,0);
        Gull(scenery,new(Width+5,6.9f,2),2.8f,-.5f,1.7f);
        Hill(scenery,new(Width*.4f,-1,-28),new(34,6,9),new("6b9a63"));
    }

    /// <summary>Greedy Trap already has its reservoir town; add the life on its water.</summary>
    private void BuildGreedyLife()
    {
        var scenery=_scenery!;
        float water=GreedyMoatHeight;
        Rowboat(scenery,new(-2.6f,water,4.2f),.3f,new("c2573f"),0).Name="GreedyWestRowboat";
        Rowboat(scenery,new(Width+2.6f,water,Depth-4.5f),-2.6f,new("3e6f8e"),1.8f).Name="GreedyEastRowboat";
        Sailboat(scenery,new(Width*.7f,water,-2.8f),Mathf.Pi/2+.2f,new("f2f0e6"),new("4c8990"),.9f);
        Gull(scenery,new(-6.4f,5.6f,Depth/2f),2.6f,.5f,0);
        Gull(scenery,new(Width+6.4f,6.1f,4),2.2f,-.6f,2);
    }
}

using Godot;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    /// <summary>A Blue Resort-informed coast around the suspended piers: a lighthouse
    /// on a rocky islet with a jetty, grass-capped sea stacks, channel buoys, a
    /// sailboat and gulls. All of it stands in open sea outside the arena.</summary>
    private void BuildUpAndDownExterior()
    {
        var scenery=_scenery!;
        float sea=-.53f;
        var cliff=_art.Color(new("8c8f8a"),.95f);var cliffDark=_art.Color(new("6d726f"),.95f);
        var grass=_art.Color(new("74a65a"),.95f);var plank=_art.Color(new("9a7650"),.85f);

        // Lighthouse islet behind the west pier.
        var islet=new Vector3(-5,sea,-1.6f);
        Rock(scenery,islet+new Vector3(0,-.55f,0),new(4.6f,1.9f,4.2f),cliff,0,.3f);
        Rock(scenery,islet+new Vector3(1.7f,-.5f,1.3f),new(2.2f,1.2f,2),cliffDark,2,1.1f);
        Rock(scenery,islet+new Vector3(-1.6f,-.5f,1),new(1.8f,1,1.6f),cliffDark,3,2.3f);
        Sphere(scenery,islet+new Vector3(-.1f,.68f,-.1f),new(2.6f,.36f,2.4f),grass).Name="IsletTurf";
        Lighthouse(scenery,islet+new Vector3(-.2f,.75f,-.3f),4.6f,0);
        var jetty=Prop(scenery,"Jetty",islet+new Vector3(2.2f,0,2.4f),.55f);
        for(int i=0;i<6;i++)Box(jetty,new(0,.28f,i*.42f),new(.9f,.08f,.38f),plank,.015f);
        foreach(int side in new[]{-1,1})for(int post=0;post<3;post++)
            Cylinder(jetty,new(side*.42f,-.05f,post*1.05f),.07f,1.1f,_art.Color(new("6b4d33"),.85f));
        Rowboat(scenery,islet+new Vector3(3.6f,0,4.1f),1.2f,new("2f7fb8"),.6f);

        // Sea stacks: tall rock pillars with turf crowns, behind and beside the east pier.
        void Stack(Vector3 at,float height,float width,int variant,bool tree)
        {
            var stack=Prop(scenery,"SeaStack",at,variant*1.3f);
            Taper(stack,new(0,height/2-1,0),width*.42f,width*.32f,height+2,variant%2==0?cliff:cliffDark,7);
            Rock(stack,new(.2f,-.3f,.3f),new(width*.9f,1.1f,width*.8f),cliffDark,variant,.8f);
            // The column top is at `height`; the turf crown sits on it.
            Taper(stack,new(0,height+.02f,0),width*.33f,width*.36f,.1f,grass,7);
            Sphere(stack,new(0,height+.06f,0),new(width*.68f,.28f,width*.68f),grass);
            if(tree)RoundTree(stack,new(.1f,height+.1f,0),.7f,new("5b9a4b"),variant);
        }
        Stack(new(Width+3.6f,sea,-1.5f),5.2f,2.4f,0,true);
        Stack(new(Width+6.2f,sea,1.4f),3.2f,1.8f,1,false);
        Stack(new(Width+3.3f,sea,12.5f),2.1f,1.6f,2,false);
        Stack(new(-3.4f,sea,9.5f),1.4f,1.4f,3,false);

        Buoy(scenery,new(-1.8f,sea,5),new("d8433b"),0);
        Buoy(scenery,new(Width+1.8f,sea,7),new("3f9b4f"),.8f);
        Buoy(scenery,new(-2.2f,sea,Depth-.5f),new("d8433b"),1.6f);
        Buoy(scenery,new(Width+2.2f,sea,Depth+.2f),new("3f9b4f"),2.3f);
        Sailboat(scenery,new(-4.8f,sea,17),-.4f,new("f2f0e6"),new("f4b23e"),.9f);

        Gull(scenery,new(-5,7.8f,-1.6f),2.4f,.6f,0);
        Gull(scenery,new(-5,8.3f,-1.6f),3.1f,.5f,2.2f);
        Gull(scenery,new(Width+4.5f,8.6f,0),2.6f,-.55f,1);

        // Far coastline: a low headland with a scattering of resort rooftops.
        Hill(scenery,new(Width*.4f,-1.4f,-30),new(40,7,10),new("7c8b7d"));
        Hill(scenery,new(Width*.4f,-.4f,-29),new(30,6,8),new("6c9b5e"));
        for(int i=0;i<6;i++)
        {
            var house=Prop(scenery,"DistantHouse",new(Width*.4f-9+i*3.4f,1.6f+(i%3)*.4f,-26.8f+(i%2)*.8f),i*.4f);
            Box(house,Vector3.Zero,new(1.6f,1.2f,1.4f),_art.Color(new("ece3cf"),.9f));
            Cone(house,new(0,.95f,0),1.25f,.75f,_art.Color(new(i%2==0?"3e6f8e":"c2573f"),.7f),4).Rotation=new(0,Mathf.Pi/4,0);
        }
    }
}

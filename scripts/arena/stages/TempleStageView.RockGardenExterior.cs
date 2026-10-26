using Godot;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    /// <summary>A Green Garden-informed meadow around the stone enclosure: a brook
    /// with a timber bridge, a windmill, ruined columns, lanterns, flower beds,
    /// rounded trees and butterflies. Purely decorative, below and beyond the walls.</summary>
    private void BuildRockGardenExterior()
    {
        var scenery=_scenery!;
        const float ground=-.24f;
        var stone=_art.Color(new("b9b6a2"),.95f);var mossy=_art.Color(new("8f9a76"),.95f);
        var timber=_art.Color(new("8a6542"),.85f);var rail=_art.Color(new("b38a5a"),.8f);
        var pebble=_art.Color(new("9d9a8c"),.95f);

        // A brook behind the rear wall, with pebble banks and a small arched bridge.
        var brook=Box(scenery,new(Width/2f,ground,-3.4f),new(90,.04f,1.5f),_art.Color(new("4f9ec0"),.12f));brook.Name="Brook";
        Box(scenery,new(Width/2f,ground-.005f,-3.4f),new(90,.03f,2.1f),_art.Color(new("8fa58a"),.95f));
        for(int i=0;i<16;i++)
        {
            float x=-6+i*2.1f+(i%3)*.4f;
            Rock(scenery,new(x,ground-.05f,i%2==0?-2.45f:-4.35f),new Vector3(.32f,.18f,.28f)*(1+(i%4)*.2f),pebble,i,i*1.3f);
        }
        var bridge=Prop(scenery,"Footbridge",new(Width*.68f,ground,-3.4f));
        for(int plank=0;plank<9;plank++)
        {
            float z=-1.2f+plank*.3f,lift=.38f*Mathf.Cos((z/1.2f)*Mathf.Pi/2);
            Box(bridge,new(0,.06f+lift,z),new(1.15f,.07f,.27f),timber,.015f);
        }
        foreach(int side in new[]{-1,1})
        {
            for(int post=0;post<3;post++)
            {
                float z=-1.05f+post*1.05f,lift=.38f*Mathf.Cos((z/1.2f)*Mathf.Pi/2);
                Box(bridge,new(side*.55f,.32f+lift,z),new(.07f,.45f,.07f),timber);
            }
            Box(bridge,new(side*.55f,.62f,0),new(.06f,.06f,2.2f),rail,.01f);
        }

        Windmill(scenery,new(-5.2f,ground,-6.4f),.35f);
        RoundTree(scenery,new(-3.2f,ground,-1.4f),1.25f,new("5b9a4b"),.3f);
        RoundTree(scenery,new(-4.4f,ground,4.5f),1.05f,new("6aa457"),1.9f);
        RoundTree(scenery,new(-3.4f,ground,11.5f),1.2f,new("4f8f46"),3.3f);
        RoundTree(scenery,new(Width+3.3f,ground,-1.8f),1.35f,new("4f8f46"),.9f);
        RoundTree(scenery,new(Width+4.3f,ground,6.5f),1.1f,new("6aa457"),2.6f);
        RoundTree(scenery,new(Width+3.6f,ground,13.8f),1.2f,new("5b9a4b"),4.1f);
        RoundTree(scenery,new(Width*.32f,ground,-6.8f),1.4f,new("5b9a4b"),5.2f);
        RoundTree(scenery,new(Width*.9f,ground,-7.6f),1.15f,new("6aa457"),.7f);

        // Old garden ruins beyond the north-east corner.
        var ruin=Prop(scenery,"GardenRuin",new(Width+3.2f,ground,-4.6f),-.35f);
        foreach(var (x,height) in new[]{(-1.1f,2.4f),(1.1f,1.5f)})
        {
            Cylinder(ruin,new(x,height/2,0),.32f,height,stone);
            Cylinder(ruin,new(x,height+.05f,0),.4f,.1f,mossy);
        }
        Box(ruin,new(-.35f,2.5f,0),new(1.9f,.28f,.6f),stone,.03f).Rotation=new(0,0,-.05f);
        Box(ruin,new(1.8f,.15f,.6f),new(.9f,.3f,.55f),mossy,.05f).Rotation=new(0,.5f,.08f);

        StoneLantern(scenery,new(-1.5f,ground,-1.4f),.4f);
        StoneLantern(scenery,new(Width+1.5f,ground,-1.3f),-.3f);
        StoneLantern(scenery,new(-1.6f,ground,Depth+.9f),.1f);
        StoneLantern(scenery,new(Width+1.6f,ground,Depth+.9f),-.2f);

        // Beds and hedges hug the outside of the walls without rising above them.
        FlowerBed(scenery,new(-1.75f,ground,5.5f),.75f,11,7);
        FlowerBed(scenery,new(-1.75f,ground,13),.7f,10,9);
        FlowerBed(scenery,new(Width+1.75f,ground,4),.7f,10,11);
        FlowerBed(scenery,new(Width+1.75f,ground,11.5f),.75f,11,13);
        FlowerBed(scenery,new(Width*.3f,ground,Depth+1.6f),.8f,12,17);
        FlowerBed(scenery,new(Width*.7f,ground,Depth+1.6f),.8f,12,19);
        foreach(float z in new[]{2.2f,8.8f,16.4f})
        {
            Bush(scenery,new(-1.5f,ground,z),.9f,new("4c8a42"));
            Bush(scenery,new(Width+1.5f,ground,z+.5f),.9f,new("4c8a42"));
        }
        foreach(float x in new[]{3.5f,9.5f,15.5f})Bush(scenery,new(x,ground,Depth+1.3f),.8f,new("558f47"));
        Rock(scenery,new(-2.6f,ground,8.2f),new(.8f,.6f,.7f),pebble,3,.6f);
        Rock(scenery,new(Width+2.5f,ground,9.6f),new(.7f,.55f,.7f),pebble,1,2.2f);

        Butterfly(scenery,new(-1.75f,ground,5.5f),.9f,new("ffd34d"),0);
        Butterfly(scenery,new(Width+1.75f,ground,11.5f),.8f,new("f58fb4"),2.1f);
        Butterfly(scenery,new(Width*.3f,ground,Depth+1.6f),1,new("8fd0ff"),4.2f);
        Butterfly(scenery,new(Width*.7f,ground,Depth+1.6f),.9f,new("ffffff"),1.2f);

        // Soft rolling hills fill the horizon instead of an endless flat lawn.
        Hill(scenery,new(Width/2f,-1.5f,-24),new(46,9,14),new("6f9f58"));
        Hill(scenery,new(-16,-1.2f,-13),new(16,7,20),new("5d914f"));
        Hill(scenery,new(Width+16,-1.2f,-15),new(18,8,20),new("6a9a55"));
        Hill(scenery,new(Width*.2f,-1.6f,-31),new(30,12,12),new("7aa866"));
    }
}

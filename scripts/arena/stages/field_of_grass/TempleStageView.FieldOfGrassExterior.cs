using Godot;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildFieldOfGrassExterior()
    {
        var scenery=_scenery!;
        const float ground=-.28f;
        var wood=_art.Toy(new("bd9468"),.85f);
        var cream=_art.Toy(new("e6d9ac"),.9f);
        // A honey-coloured country mill and an orchard wrap the two earth banks.
        Windmill(scenery,new(-5,ground,-2),.6f);
        for(int side=0;side<2;side++)for(int row=0;row<4;row++)
        {
            float x=side==0?-3.6f-row%2:Width+3.6f+row%2,z=-2+row*6.2f;
            RoundTree(scenery,new(x,ground,z),1.15f+(row%2)*.15f,new(row%2==0?"63894a":"799b51"),row*1.3f+side);
            FlowerBed(scenery,new(side==0?-1.6f:Width+1.6f,ground,z+2),.75f,8,side*10+row);
            Butterfly(scenery,new(side==0?-2:Width+2,ground,z+2),.75f,new(row%2==0?"f6d17b":"e9a5b8"),row+side*3);
        }
        var barn=Prop(scenery,"MeadowSeedBarn",new(Width*.7f,ground,-7),.12f);
        Box(barn,new(0,.8f,0),new(3.2f,1.6f,2),cream,.12f);
        var roof=Box(barn,new(0,1.85f,0),new(3.6f,.3f,2.6f),_art.Toy(new("a8664c"),.85f),.1f);roof.RotationDegrees=new(0,0,5);
        Box(barn,new(0,.57f,1.025f),new(.8f,1.1f,.05f),wood,.04f);
        foreach(float x in new[]{-.95f,.95f})Box(barn,new(x,.94f,1.025f),new(.55f,.55f,.04f),_art.Toy(new("6c9394"),.5f),.04f);
        var cart=Prop(scenery,"MeadowHayCart",new(-2.7f,ground,Depth+1.8f),-.22f);
        Box(cart,new(0,.5f,0),new(1.7f,.18f,1.1f),wood,.04f);
        foreach(float x in new[]{-.65f,.65f})foreach(float z in new[]{-.58f,.58f})
        {
            Bead(cart,new(x,.28f,z),new(.56f,.56f,.1f),wood);
            Box(cart,new(x,.72f,z*.85f),new(.07f,.5f,.07f),wood,.01f);
        }
        for(int i=0;i<3;i++)
            RoundedBox(cart,new((i-1)*.48f,.83f,0),new(.45f,.53f,.75f),_art.Toy(new("d6ba70"),.94f));
        // A small original kite bobs over the rear meadow, well outside play space.
        var kite=Prop(scenery,"MeadowKite",new(Width*.28f,5,-7));
        var sail=Box(kite,Vector3.Zero,new(.8f,.045f,.8f),_art.Toy(new("db846c"),.85f),.05f);sail.RotationDegrees=new(60,0,45);
        _sceneryMotion.Add(t=>{kite.Position=new(Width*.28f+Mathf.Sin(t*.25f)*.7f,5+Mathf.Sin(t*.7f)*.3f,-7);kite.Rotation=new(.15f,0,Mathf.Sin(t*.8f)*.12f);});
        Hill(scenery,new(Width*.4f,-2,-23),new(43,10,16),new("7f9b57"));
        Hill(scenery,new(-18,-2,-8),new(20,8,22),new("6c8d4d"));
        Hill(scenery,new(Width+18,-2,-12),new(20,11,22),new("77954f"));
    }
}

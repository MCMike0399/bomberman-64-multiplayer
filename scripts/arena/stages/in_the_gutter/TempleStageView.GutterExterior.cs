using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildGutterExterior()
    {
        var masonry=_art.Toy(new Color("b19b76"),.86f);
        var brick=_art.Toy(new Color("89584a"),.83f);
        var bronze=_art.Toy(new Color("bb9556"),.58f);
        var water=_art.Toy(new Color("377e83"),.48f);
        var leaf=_art.Toy(new Color("65927b"),.86f);
        // A raised lock on a canal. Everything beyond the enclosure is
        // decoration; the actual court remains the only supported surface.
        Box(_scenery!,new(Width/2f,-1.14f,Depth/2f),new(Width+18,.24f,Depth+18),water,.08f).Name="GutterCanal";
        foreach(float x in new[]{-2.7f,Width+2.7f})
        {
            Box(_scenery!,new(x,-.15f,Depth/2f),new(2.4f,1.5f,Depth+8),masonry,.12f).Name="GutterCanalQuay";
            for(int n=-1;n<=4;n++)
                Box(_scenery!,new(x,.63f,n*5),new(2.15f,.12f,4.9f),_art.Color(new Color("d9c9a3")),.04f);
            var wheel=Prop(_scenery!,"GutterSluiceWheel",new(x,.85f,-3));
            for(int spoke=0;spoke<8;spoke++)
            {
                var timber=Box(wheel,Vector3.Zero,new(2.5f,.14f,.16f),bronze,.025f);
                timber.Basis=PresentationMath.Rotation(new(0,0,spoke*Mathf.Pi/8));
            }
            Box(wheel,new(0,0,.12f),new(.42f,.42f,.22f),brick,.06f);
            _sceneryMotion.Add(time=>wheel.Basis=PresentationMath.Rotation(new(0,0,time*.19f)));
            for(int n=0;n<3;n++)
            {
                var shrub=Prop(_scenery!,"GutterQuayShrub",new(x,.85f,5+n*5));
                Sphere(shrub,new(0,.3f,0),new(1.3f,.95f,1.3f),leaf);
            }
        }
        for(int n=0;n<4;n++)
        {
            var house=Prop(_scenery!,"GutterLockHouse",new(-5+n*10,-.2f,-5));
            Box(house,new(0,1.1f,0),new(3,2.2f,2.6f),masonry,.12f);
            Box(house,new(0,2.4f,0),new(3.6f,.45f,3.2f),_art.Toy(new Color("3e777d"),.8f),.15f);
            Box(house,new(0,1.15f,1.32f),new(.65f,.85f,.08f),_art.Toy(new Color("eec778"),.6f),.05f);
        }
    }
}

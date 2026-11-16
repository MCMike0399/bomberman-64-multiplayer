using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    // Large forms only: one wreck, slow manta and distant bell buoy replace the
    // radial scatter of coral pads. Fine finishing waits for shape-study review.
    private void BuildLostAtSeaExterior()
    {
        var wreck=new Node3D{Name="SunkenWreck",Position=new(10.7f,-2.4f,11.5f),Basis=PresentationMath.Rotation(new(0,-.38f,0))};_scenery!.AddChild(wreck);
        var timber=_art.Toy(new("526379"),.87f);var ribs=_art.Toy(new("718793"),.85f);
        var hull=new SurfaceTool();hull.Begin(Mesh.PrimitiveType.Triangles);
        Vector2[] outline=[new(-3.5f,-1),new(-3.8f,0),new(-3.5f,1),new(2.5f,1),new(3.8f,.1f),new(2.5f,-1)];
        for(int i=0;i<outline.Length;i++)
        {
            int j=(i+1)%outline.Length;var a=outline[i];var b=outline[j];
            Vector3 P(Vector2 v,float y,float width)=>new(v.X*width,y,v.Y*width);
            var outward=new Vector3(a.X+b.X,0,a.Y+b.Y);
            BuoyQuad(hull,P(a,-.4f,.68f),P(b,-.4f,.68f),P(b,.2f,1),P(a,.2f,1),outward);
            BuoyTriangle(hull,new(0,.2f,0),P(a,.2f,1),P(b,.2f,1),Vector3.Up);
            BuoyTriangle(hull,new(0,-.4f,0),P(b,-.4f,.68f),P(a,-.4f,.68f),Vector3.Down);
        }
        hull.Index();wreck.AddChild(new MeshInstance3D{Name="GlassBuoyWreckHull",Mesh=hull.Commit(),MaterialOverride=timber});
        foreach(float x in new[]{-2f,0f,2f})foreach(float side in new[]{-.85f,.85f})
            RoundedBox(wreck,new(x,.52f,side),new(.22f,.65f,.22f),ribs,.7f);
        var mast=RoundedBox(wreck,new(-.7f,.45f,0),new(.27f,.55f,.27f),timber,.65f);mast.Basis=PresentationMath.Rotation(new(0,0,-.35f));
        RoundedBox(wreck,new(-.65f,.72f,0),new(1.5f,.16f,.2f),ribs,.65f);
        RoundedBox(wreck,new(2.5f,.34f,0),new(.8f,.22f,1.2f),timber,.65f);

        var manta=new Node3D{Name="MantaRay",Position=new(10.5f,-1.28f,10.5f)};_scenery.AddChild(manta);
        var dark=_art.Toy(new("0b1d38"),.9f);
        var wing=new SurfaceTool();wing.Begin(Mesh.PrimitiveType.Triangles);
        Vector2[] shape=[new(0,-1.1f),new(.75f,-.7f),new(2.6f,-.35f),new(2.1f,.65f),new(.4f,1),new(0,.7f),new(-.4f,1),new(-2.1f,.65f),new(-2.6f,-.35f),new(-.75f,-.7f)];
        for(int i=0;i<shape.Length;i++)
        {
            int j=(i+1)%shape.Length;var a=shape[i];var b=shape[j];
            Vector3 P(Vector2 v,float y)=>new(v.X,y,v.Y);
            BuoyTriangle(wing,new(0,.1f,0),P(a,0),P(b,0),Vector3.Up);
            BuoyTriangle(wing,new(0,-.08f,0),P(b,-.06f),P(a,-.06f),Vector3.Down);
            BuoyQuad(wing,P(a,0),P(a,-.06f),P(b,-.06f),P(b,0),new(a.X+b.X,0,a.Y+b.Y));
        }
        wing.Index();manta.AddChild(new MeshInstance3D{Name="GlassBuoyMantaSilhouette",Mesh=wing.Commit(),MaterialOverride=dark});
        RoundedBox(manta,new(0,-.015f,1.7f),new(.12f,.10f,2.3f),dark,.6f);
        _sceneryMotion.Add(t=>
        {
            float phase=t*Mathf.Tau/84;
            manta.Position=new(Width/2f+PresentationMath.Sin(phase)*3.2f,-1.28f+PresentationMath.Sin(phase*2)*.06f,Depth/2f+PresentationMath.SinCos(phase).Cos*2.6f);
            manta.Basis=PresentationMath.Rotation(new(0,PresentationMath.Sin(phase)*.2f,0));
        });

        var buoy=new Node3D{Name="BellBuoy",Position=new(-3.5f,-.45f,12.2f)};_scenery.AddChild(buoy);
        var white=_art.Toy(new("dde7df"),.64f);var orange=_art.Toy(new("d8743e"),.65f);var rubber=_art.Toy(new("25354b"),.9f);var brass=_art.Toy(new("d2b36c"),.55f);
        Bead(buoy,Vector3.Zero,new(1.45f,.55f,1.45f),white);
        Bead(buoy,new(0,.08f,0),new(1.48f,.22f,1.48f),rubber);
        RoundedBox(buoy,new(0,.34f,0),new(.72f,.45f,.72f),orange,.75f);
        foreach(float x in new[]{-.24f,.24f})RoundedBox(buoy,new(x,.93f,0),new(.14f,1,.14f),white,.7f);
        RoundedBox(buoy,new(0,1.42f,0),new(.65f,.18f,.35f),white,.7f);
        Bead(buoy,new(0,1.02f,0),new(.42f,.5f,.42f),brass);
        Bead(buoy,new(0,1.57f,0),new(.24f,.18f,.24f),_art.Toy(new("edba66"),.45f,true));
        var buoyLight=new OmniLight3D{Name="BellBuoyWarmLight",Position=new(0,1.6f,0),LightColor=new("ffb45d"),
            LightEnergy=.05f,OmniRange=2.8f,OmniAttenuation=2.0f,ShadowEnabled=false};
        buoy.AddChild(buoyLight);_buoyLights.Add(buoyLight);
        _sceneryMotion.Add(t=>
        {
            buoy.Position=new(-3.5f,SceneryWaterHeight+.17f+PresentationMath.Sin(t*.8f)*.05f,12.2f);
            buoy.Basis=PresentationMath.Rotation(new(0,0,PresentationMath.Sin(t*.65f)*.055f));
        });
    }
}

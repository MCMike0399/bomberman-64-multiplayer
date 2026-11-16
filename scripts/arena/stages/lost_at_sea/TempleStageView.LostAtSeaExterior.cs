using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private Node3D? _observationBuoy;
    // Large forms only: one wreck, slow manta and distant bell buoy replace the
    // radial scatter of coral pads. Fine finishing waits for shape-study review.
    private void BuildLostAtSeaExterior()
    {
        var wreck=new Node3D{Name="SunkenWreck",Position=new(5.2f,-1.25f,7.9f),Basis=PresentationMath.Rotation(new(0,Mathf.Pi/2,0))};_scenery!.AddChild(wreck);
        // A broad, faint moonlit shape remains legible through the panes from
        // the opening; movement is reserved for the manta, not the wreck.
        var timber=new StandardMaterial3D{AlbedoColor=new("708b98"),Roughness=.87f,
            EmissionEnabled=true,Emission=new("526b82"),EmissionEnergyMultiplier=.12f};
        var ribs=new StandardMaterial3D{AlbedoColor=new("91a8af"),Roughness=.9f,
            EmissionEnabled=true,Emission=new("526e83"),EmissionEnergyMultiplier=.10f};
        BuildObservatoryWreckRibs(wreck,ribs);
        RoundedBox(wreck,new(0,-.14f,0),new(4.9f,.15f,.20f),timber,.35f);
        var plank=RoundedBox(wreck,new(-.8f,-.05f,.55f),new(1.4f,.10f,.25f),timber,.4f);
        plank.Basis=PresentationMath.Rotation(new(0,.3f,0));

        var manta=new Node3D{Name="MantaRay",Position=new(10.5f,-.92f,13.1f)};_scenery.AddChild(manta);
        var dark=new StandardMaterial3D{AlbedoColor=new("688e9b"),Roughness=.93f,
            EmissionEnabled=true,Emission=new("35576b"),EmissionEnergyMultiplier=.04f};
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
        _sceneryMotion.Add(_=>
        {
            float phase=_seaClock*Mathf.Tau/84;
            manta.Position=new(Width/2f+PresentationMath.Sin(phase)*3.2f,-.92f+PresentationMath.Sin(phase*2)*.06f,Depth/2f+PresentationMath.SinCos(phase).Cos*2.6f);
            manta.Basis=PresentationMath.Rotation(new(0,PresentationMath.Sin(phase)*.2f,0));
        });

        var buoy=new Node3D{Name="BellBuoy",Position=new(-3.5f,-.45f,12.2f)};AddChild(buoy);_observationBuoy=buoy;
        var white=_art.Toy(new("dde7df"),.64f);var orange=_art.Toy(new("d8743e"),.65f);var rubber=_art.Toy(new("25354b"),.9f);var brass=_art.Toy(new("d2b36c"),.55f);
        Bead(buoy,Vector3.Zero,new(1.45f,.55f,1.45f),white);
        Bead(buoy,new(0,.08f,0),new(1.48f,.22f,1.48f),rubber);
        RoundedBox(buoy,new(0,.34f,0),new(.72f,.45f,.72f),orange,.75f);
        foreach(float x in new[]{-.24f,.24f})RoundedBox(buoy,new(x,.93f,0),new(.14f,1,.14f),white,.7f);
        RoundedBox(buoy,new(0,1.42f,0),new(.65f,.18f,.35f),white,.7f);
        Bead(buoy,new(0,1.02f,0),new(.42f,.5f,.42f),brass);
        var signal=new StandardMaterial3D{AlbedoColor=new("edba66"),EmissionEnabled=true,Emission=new("ffb959"),
            EmissionEnergyMultiplier=.08f,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,DisableFog=true};
        _buoyBeaconPaint.Add(signal);
        Bead(buoy,new(0,1.57f,0),new(.24f,.18f,.24f),signal).Name="BellBuoyBeaconLens";
        var buoyLight=new OmniLight3D{Name="BellBuoyWarmLight",Position=new(0,1.6f,0),LightColor=new("ffb45d"),
            LightEnergy=.05f,OmniRange=2.8f,OmniAttenuation=2.0f,ShadowEnabled=false};
        buoy.AddChild(buoyLight);_buoyLights.Add(buoyLight);
    }

    private void ProcessObservatoryBuoy()
    {
        if(_observationBuoy is null)return;
        _observationBuoy.Position=new(-3.5f,SceneryWaterHeight+.17f+PresentationMath.Sin(_seaClock*.8f)*.05f,12.2f);
        _observationBuoy.Basis=PresentationMath.Rotation(new(0,0,.10f+PresentationMath.Sin(_seaClock*.65f)*.045f));
    }

    private void BuildObservatoryWreckRibs(Node3D wreck,Material paint)
    {
        var mesh=new SurfaceTool();mesh.Begin(Mesh.PrimitiveType.Triangles);mesh.SetSmoothGroup(0);
        const int sections=16,sides=8;
        foreach(float x in new[]{-2f,-1f,0f,1f,2f})
        {
            Vector3 Center(int row){float t=row/(float)sections*2-1;return new(x,-.18f+.60f*t*t,t*.74f);}
            Vector3 Point(int row,int side)
            {
                float t=row/(float)sections*2-1;var tangent=new Vector3(0,2.4f*t,1.48f).Normalized();
                var up=tangent.Cross(Vector3.Right).Normalized();var(s,c)=PresentationMath.SinCos(side*Mathf.Tau/sides);
                return Center(row)+(Vector3.Right*c+up*s)*.085f;
            }
            for(int row=0;row<sections;row++)for(int side=0;side<sides;side++)
                BuoyQuad(mesh,Point(row,side),Point(row,side+1),Point(row+1,side+1),Point(row+1,side),Point(row,side)-Center(row));
            for(int side=0;side<sides;side++)
            {
                BuoyTriangle(mesh,Center(0),Point(0,side+1),Point(0,side),new(0,1,-1));
                BuoyTriangle(mesh,Center(sections),Point(sections,side),Point(sections,side+1),new(0,1,1));
            }
        }
        mesh.GenerateNormals();mesh.Index();wreck.AddChild(new MeshInstance3D{Name="GlassBuoyWreckRibs",Mesh=mesh.Commit(),MaterialOverride=paint});
    }
}

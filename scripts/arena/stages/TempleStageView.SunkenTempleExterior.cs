using Godot;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    /// <summary>Green Garden-informed lagoon: palm islets, half-sunken temple ruins,
    /// a moored sailboat and wheeling gulls. Everything sits in open sea beyond the
    /// pyramid's foundation; rising water submerges the ruins and lifts the boats.</summary>
    private void BuildSunkenTempleExterior()
    {
        var scenery=_scenery!;
        var sand=_art.Color(new("e6cf96"),.95f);var wetSand=_art.Color(new("c9ae74"),.95f);
        var turf=_art.Color(new("6fa456"),.95f);
        var sandstone=_art.Color(new("caa66a"),.9f);var pale=_art.Color(new("e7cf93"),.85f);
        var weathered=_art.Color(new("9c7d4a"),.95f);var moss=_art.Color(new("5f8a45"),.95f);
        var rock=_art.Color(new("8f8a7d"),.95f);
        float sea=-.53f;

        void Islet(Vector3 at,Vector3 size)
        {
            Sphere(scenery,at+new Vector3(0,-.48f,0),new(size.X*1.18f,size.Y,size.Z*1.18f),wetSand).Name="IsletShoal";
            Sphere(scenery,at+new Vector3(0,-.35f,0),size,sand).Name="Islet";
            Sphere(scenery,at+new Vector3(-size.X*.08f,-.18f,-size.Z*.06f),new(size.X*.6f,size.Y*.55f,size.Z*.55f),turf).Name="IsletTurf";
        }

        // Back-left: the larger palm islet.
        var west=new Vector3(-5.2f,sea,-2.6f);
        Islet(west,new(5.6f,1.2f,4.4f));
        PalmTree(scenery,west+new Vector3(-.4f,.1f,-.3f),2.9f,.22f,.4f,0);
        PalmTree(scenery,west+new Vector3(1.1f,.05f,.6f),2.2f,.3f,2.4f,1.7f);
        Rock(scenery,west+new Vector3(2.2f,-.2f,1.2f),new(.9f,.7f,.8f),rock,1,.5f);
        Rock(scenery,west+new Vector3(-2.3f,-.25f,.9f),new(.7f,.5f,.6f),rock,2,1.4f);

        // Back-right: a small islet keeps a sandstone obelisk from the old shrine.
        var east=new Vector3(Width+4.8f,sea,.4f);
        Islet(east,new(4.2f,1.1f,3.6f));
        var obelisk=Prop(scenery,"Obelisk",east+new Vector3(.5f,-.05f,-.3f),.3f);
        Box(obelisk,new(0,.14f,0),new(.95f,.28f,.95f),pale,.04f);
        Taper(obelisk,new(0,1.6f,0),.36f,.26f,2.7f,sandstone,4).Rotation=new(0,Mathf.Pi/4,0);
        Cone(obelisk,new(0,3.12f,0),.27f,.34f,_art.Color(new("eac46a"),.35f),4).Rotation=new(0,Mathf.Pi/4,0);
        PalmTree(scenery,east+new Vector3(-1,.05f,.7f),2.4f,-.26f,-.6f,3.1f);

        // Half-sunken colonnade off the west walkway.
        var colonnade=Prop(scenery,"SunkenColonnade",new(-2.8f,sea,11.5f));
        foreach(var (z,height) in new[]{(-1.6f,2.3f),(0f,1.25f),(1.6f,.7f)})
        {
            Cylinder(colonnade,new(0,height/2-.4f,z),.34f,height+.8f,sandstone);
            Cylinder(colonnade,new(0,height-.38f,z),.4f,.12f,pale);
            Sphere(colonnade,new(.12f,-.05f,z+.25f),new(.55f,.12f,.4f),moss);
        }
        Box(colonnade,new(0,1.98f,-1.6f),new(.95f,.2f,.95f),pale,.03f);
        var drum=Cylinder(colonnade,new(-1.2f,-.05f,.9f),.34f,1.1f,weathered);drum.Rotation=new(Mathf.Pi/2,.6f,0);

        // A broken gateway on the east side, its lintel slipped and draped in vines.
        var gate=Prop(scenery,"SunkenGate",new(Width+2.9f,sea,17.4f),-.25f);
        foreach(float x in new[]{-.9f,.9f})Box(gate,new(x,.75f,0),new(.5f,2.6f,.55f),sandstone,.04f);
        var lintel=Box(gate,new(.15f,2.12f,0),new(2.5f,.36f,.62f),pale,.04f);lintel.Rotation=new(0,0,-.09f);
        for(int vine=0;vine<4;vine++)Box(gate,new(-.75f+vine*.48f,1.65f-vine%2*.2f,.32f),new(.07f,.75f+vine%2*.3f,.05f),moss);
        Box(gate,new(-1.7f,-.15f,.4f),new(.7f,.5f,.6f),weathered,.06f).Rotation=new(.2f,.4f,.15f);

        // Low rocks beside the walkways; the front stays clear of the HUD footer.
        Rock(scenery,new(-2.4f,sea-.25f,Depth-6),new(.9f,.6f,.8f),rock,3,.2f);
        Rock(scenery,new(Width+2.3f,sea-.25f,Depth-8),new(.8f,.55f,.8f),rock,0,2.1f);

        Sailboat(scenery,new(Width+5.6f,sea,8.5f),.35f,new("2f7fb8"),new("f7f1e3"),0);
        Rowboat(scenery,new(-4.6f,sea,19),-.5f,new("d0603f"),1.3f);

        Gull(scenery,new(-6.5f,6.2f,3),2.6f,.55f,0);
        Gull(scenery,new(-6.5f,6.9f,3),2.1f,.62f,2.4f);
        Gull(scenery,new(Width+6.5f,7.2f,-1.5f),3,-.5f,1.1f);

        // Distant jungle islands in the haze behind the pyramid.
        Hill(scenery,new(Width*.22f,-1.2f,-26),new(22,4.2f,9),new("d7c48f"));
        Hill(scenery,new(Width*.22f,-.6f,-26.5f),new(16,7,7),new("5f8f55"));
        Hill(scenery,new(Width*.95f,-.6f,-34),new(24,9,9),new("6b9a63"));
        for(int i=0;i<4;i++)PalmTree(scenery,new(Width*.22f-4.5f+i*3.1f,1.6f+(i%2)*.5f,-25+(i%2)*1.2f),2.8f,i%2==0?.25f:-.25f,i*1.3f,i);
    }
}

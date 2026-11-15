using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private void BuildGutterExterior()
    {
        var scenery=_scenery!;
        var masonry=_art.Masonry();masonry.SetShaderParameter("base_color",new Color("aea28b"));
        masonry.SetShaderParameter("mortar_color",new Color("746f5e"));masonry.SetShaderParameter("water_height",-.92f);
        var paving=_art.GardenSurface();paving.SetShaderParameter("stone_color",new Color("c2b79b"));
        var garden=_art.GardenSurface(true);garden.SetShaderParameter("grass_dark",new Color("466c54"));
        garden.SetShaderParameter("grass_light",new Color("719269"));
        _gutterMaterials.AddRange([masonry,paving,garden]);
        var coping=_art.Color(new Color("d8cba9"),.88f);
        var brick=_art.Toy(new Color("89584a"),.83f);
        var bronze=_art.Toy(new Color("bb9556"),.62f);
        var iron=_art.Toy(new Color("3b5957"),.72f);
        var timber=_art.Color(new Color("806347"),.87f);
        var leaf=_art.Color(new Color("61866c"),.9f);

        // A finite miniature lock basin: narrow water channels, masonry banks
        // and planted ground replace the former single flat cyan rectangle.
        Box(scenery,new(Width/2f,-1.65f,Depth/2f),new(Width+13,.7f,Depth+12),masonry,.4f).Name="GutterDioramaFoot";
        foreach(int side in new[]{-1,1})
        {
            float quayX=side<0?-2.7f:Width+2.7f;
            float bankX=side<0?-5f:Width+5f;
            Box(scenery,new(bankX,-.7f,Depth/2f),new(2.3f,1.15f,Depth+9.5f),garden,.18f).Name="GutterPlantedBank";
            Box(scenery,new(quayX,-.16f,Depth/2f),new(2.4f,1.5f,Depth+8),masonry,.12f).Name="GutterCanalQuay";
            Box(scenery,new(quayX,.63f,Depth/2f),new(2.5f,.1f,Depth+8.1f),paving,.035f);
            float left=side<0?-1.45f:Width+.36f,right=side<0?-.36f:Width+1.45f;
            scenery.AddChild(new MeshInstance3D{Name="GutterExteriorChannel",
                Mesh=GutterPlane(left,right,-3.7f,Depth+3.7f),Position=new(0,-.92f,0),
                MaterialOverride=_gutterWater,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
            // Individual coping courses frame the channel and break long blank sidewalks.
            for(int n=0;n<14;n++)
            {
                float z=-3.4f+n*2.1f;
                Box(scenery,new(quayX-side*1.12f,.73f,z),new(.18f,.14f,2.03f),coping,.025f);
                if(n%3==0)Box(scenery,new(quayX+side*.96f,.76f,z),new(.21f,.19f,.22f),bronze,.035f);
            }
            GutterSluiceHouse(scenery,new(quayX,.69f,-2.1f),bronze,timber,iron,masonry);
            GutterKeeperHouse(scenery,new(quayX,.69f,-5.8f),side<0?new Color("467e83"):new Color("536e8a"));
            foreach(int n in new[]{0,1})
            {
                float z=5.2f+n*8.3f;
                GutterQuayLamp(scenery,new(quayX-side*.68f,.69f,z-1.9f),bronze,iron,n+side);
                var planter=Prop(scenery,"GutterQuayPlanter",new(quayX+side*.23f,.69f,z));
                Box(planter,new(0,.16f,0),new(1.12f,.32f,1.5f),brick,.075f);
                Box(planter,new(0,.31f,0),new(.94f,.07f,1.31f),_art.Color(new Color("405f4b")),.025f);
                var leaves=Prop(planter,"GutterWillowLeaves",new(0,.56f,0));
                Sphere(leaves,new(-.22f,.03f,-.28f),new(.85f,.63f,.91f),leaf);
                Sphere(leaves,new(.21f,.13f,.2f),new(.82f,.79f,.82f),_art.Color(new Color("79946c")));
                Sphere(leaves,new(-.19f,.3f,.17f),new(.61f,.69f,.62f),leaf);
                float phase=z+quayX;
                _sceneryMotion.Add(time=>leaves.Basis=PresentationMath.Rotation(new(0,0,PresentationMath.Sin(time*.7f+phase)*.026f)));
                // A few warm blossoms soften the industrial palette.
                for(int f=0;f<3;f++)
                    Bead(planter,new(-.3f+f*.28f,.77f,-.39f),Vector3.One*.11f,_art.Toy(new Color("e5bd73"),.74f));
            }
            // Two low bank trees frame the rear; no new tall foreground mass.
            var tree=Prop(scenery,"GutterBankTree",new(bankX,-.08f,2.6f));
            Box(tree,new(0,.55f,0),new(.22f,1.1f,.22f),timber,.05f);
            var crown=Prop(tree,"GutterTreeCrown",new(0,1.43f,0));
            Sphere(crown,Vector3.Zero,new(1.7f,1.55f,1.6f),leaf);
            Sphere(crown,new(-.4f,.38f,.16f),new(1.08f,.99f,1.14f),_art.Color(new Color("78956d")));
            _sceneryMotion.Add(time=>crown.Basis=PresentationMath.Rotation(new(0,0,PresentationMath.Sin(time*.65f+bankX)*.025f)));
            // A small bobbing gauge float lives inside each exterior channel.
            var gauge=Prop(scenery,"GutterFlowFloat",new((left+right)/2,-.85f,Depth-2));
            Box(gauge,Vector3.Zero,new(.4f,.17f,.7f),side<0?brick:iron,.07f);
            Box(gauge,new(0,.15f,0),new(.17f,.16f,.38f),coping,.025f);
            _sceneryMotion.Add(time=>gauge.Position=new(gauge.Position.X,-.85f+PresentationMath.Sin(time*1.1f+quayX)*.035f,gauge.Position.Z));
        }
        // Back service bridge and a low original pumping cabinet leave the
        // playable court's central silhouette unobstructed.
        Box(scenery,new(Width/2f,-.17f,-2.6f),new(Width+3.1f,1.48f,1.5f),masonry,.1f);
        Box(scenery,new(Width/2f,.62f,-2.6f),new(Width+3.2f,.1f,1.57f),paving,.03f);
        var pump=Prop(scenery,"GutterPumpCabinet",new(Width/2f,.69f,-3.4f));
        Box(pump,new(0,.55f,0),new(2.4f,1.1f,1.0f),iron,.1f);
        Box(pump,new(0,1.14f,0),new(2.65f,.12f,1.22f),bronze,.045f);
        Box(pump,new(-.63f,.62f,.52f),new(.64f,.64f,.08f),coping,.065f);
        var needle=Box(pump,new(-.63f,.62f,.575f),new(.04f,.46f,.025f),iron,.01f);
        _sceneryMotion.Add(time=>needle.Basis=PresentationMath.Rotation(new(0,0,PresentationMath.Sin(time*.45f)*.18f)));
        for(int n=0;n<4;n++)Box(pump,new(.15f+n*.3f,.58f,.515f),new(.1f,.67f,.05f),bronze,.02f);
    }

    private void GutterKeeperHouse(Node3D parent,Vector3 position,Color roofColor)
    {
        var house=Prop(parent,"GutterLockHouse",position);
        var plaster=_art.Toy(new Color("c6b596"),.88f);
        var trim=_art.Color(new Color("e7d7b1"),.83f);
        var dark=_art.Color(new Color("35514e"),.88f);
        var roof=_art.Toy(roofColor,.84f);
        Box(house,new(0,1.05f,0),new(2.8f,2.1f,2.6f),plaster,.08f);
        foreach(int side in new[]{-1,1})
        {
            Box(house,new(side*1.34f,1.05f,1.32f),new(.16f,2.1f,.1f),trim,.015f);
            Box(house,new(side*.85f,1.16f,1.325f),new(.64f,.75f,.08f),dark,.035f);
            Box(house,new(side*.85f,1.16f,1.375f),new(.49f,.59f,.025f),Glow(new Color("e9bd72"),.65f),.02f);
            Box(house,new(side*.85f,1.16f,1.395f),new(.045f,.62f,.025f),trim);
            Box(house,new(side*.85f,1.16f,1.4f),new(.5f,.04f,.025f),trim);
            Box(house,new(side*.85f,.76f,1.4f),new(.78f,.1f,.17f),trim,.02f);
        }
        Box(house,new(0,.63f,1.34f),new(.61f,1.26f,.1f),dark,.035f);
        Box(house,new(.17f,.59f,1.415f),new(.08f,.08f,.045f),_art.Toy(new Color("c7a65d"),.6f),.025f);
        Box(house,new(0,.08f,1.58f),new(.84f,.16f,.45f),trim,.035f);
        Plate(house,new(0,2.08f,0),[new(-1.63f,0),new(1.63f,0),new(0,1.12f)],3.05f,roof);
        Box(house,new(0,3.21f,0),new(.17f,.13f,3.15f),trim,.035f);
        Box(house,new(-.75f,3.03f,-.55f),new(.39f,1.0f,.42f),plaster,.035f);
        Box(house,new(-.75f,3.56f,-.55f),new(.52f,.12f,.56f),dark,.035f);
        // Narrow roof battens suggest painted shingles without noisy tiling.
        for(int side=-1;side<=1;side+=2)for(int n=0;n<4;n++)
        {
            var batten=Box(house,new(side*.84f,2.65f,-1.22f+n*.8f),new(1.93f,.055f,.055f),_art.Color(roofColor.Darkened(.12f),.88f));
            batten.Basis=PresentationMath.Rotation(new(0,0,-side*.6f));
        }
    }

    private void GutterSluiceHouse(Node3D parent,Vector3 position,Material brass,Material timber,Material iron,Material stone)
    {
        var mount=Prop(parent,"GutterSluiceHousing",position);
        foreach(int side in new[]{-1,1})
            Box(mount,new(side*1.06f,.95f,0),new(.24f,1.9f,.52f),stone,.055f);
        Box(mount,new(0,1.97f,0),new(2.5f,.25f,.65f),iron,.055f);
        var rotor=Prop(mount,"GutterSluiceWheel",new(0,1.05f,.44f));
        for(int segment=0;segment<12;segment++)
        {
            float angle=segment*Mathf.Tau/12;
            var (sin,cos)=PresentationMath.SinCos(angle);
            var rim=Box(rotor,new(cos*.89f,sin*.89f,0),new(.5f,.13f,.16f),brass,.035f);
            rim.Basis=PresentationMath.Rotation(new(0,0,angle+Mathf.Pi/2));
            if(segment%3==0)
            {
                var spoke=Box(rotor,Vector3.Zero,new(1.72f,.1f,.12f),timber,.015f);
                spoke.Basis=PresentationMath.Rotation(new(0,0,angle));
            }
        }
        Box(rotor,Vector3.Zero,new(.34f,.34f,.3f),iron,.08f);
        _sceneryMotion.Add(time=>rotor.Basis=PresentationMath.Rotation(new(0,0,time*.19f)));
        // Fixed gate slats and rivets make the turning wheel read as working machinery.
        for(int slat=0;slat<5;slat++)Box(mount,new(-.68f+slat*.34f,.61f,-.29f),new(.27f,1.1f,.12f),timber,.025f);
        foreach(float x in new[]{-.94f,.94f})foreach(float y in new[]{.35f,1.38f})
            Sphere(mount,new(x,y,.285f),Vector3.One*.12f,brass);
    }

    private void GutterQuayLamp(Node3D parent,Vector3 position,Material brass,Material iron,float phase)
    {
        var lamp=Prop(parent,"GutterBrassWorkLamp",position);
        Box(lamp,new(0,.78f,0),new(.1f,1.56f,.1f),iron,.025f);
        Box(lamp,new(.15f,1.56f,0),new(.4f,.09f,.11f),brass,.025f);
        Box(lamp,new(.32f,1.29f,0),new(.25f,.45f,.25f),brass,.035f);
        var light=Box(lamp,new(.32f,1.3f,.14f),new(.14f,.28f,.025f),Glow(new Color("efc67e"),1.25f),.015f);
        Box(lamp,new(.32f,1.55f,0),new(.36f,.09f,.36f),iron,.03f);
        _sceneryMotion.Add(time=>light.Scale=Vector3.One*(.98f+PresentationMath.Sin(time*1.3f+phase)*.02f));
    }
}

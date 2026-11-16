using Godot;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

public partial class TempleStageView
{
    private readonly List<StandardMaterial3D> _buoyBeaconPaint=[];
    // Shape study: the dry authority plane stays at zero. The pontoon's belly
    // and four rounded floats sit below it; coamings occupy the real wall cells.
    private void BuildGlassBuoy()
    {
        var enamel=_art.Toy(new("e0e8e4"),.62f);
        var rubber=_art.Toy(new("202c3c"),.88f);
        var steel=new StandardMaterial3D{AlbedoColor=new("343b4a"),Roughness=.82f,Metallic=.35f,MetallicSpecular=.24f};
        var orange=_art.Toy(new("d86c35"),.66f);
        _seaGlass=_art.ObservationGlass();
        _seaGlass.SetShaderParameter("glass_bounds",new Vector4(3,3,Width-3,Depth-3));
        _seaGlass.SetShaderParameter("depth_enabled",IncludeScenery);

        var inner=BuoyInnerContour();
        var outer=BuoyOuterContour(-.23f,Width+.23f,-.23f,Depth+.23f,1.5f);
        var shoulder=BuoyOuterContour(-.12f,Width+.12f,-.12f,Depth+.12f,1.45f);
        var deckOutline=BuoyOuterContour(0,Width,0,Depth,1.25f);
        var keel=BuoyOuterContour(.35f,Width-.35f,.35f,Depth-.35f,1.2f);
        var body=new SurfaceTool();body.Begin(Mesh.PrimitiveType.Triangles);
        var deck=new SurfaceTool();deck.Begin(Mesh.PrimitiveType.Triangles);
        var center=new Vector3(Width/2f,0,Depth/2f);
        for(int i=0;i<8;i++)
        {
            int j=(i+1)%8;
            Vector3 P(Vector2 p,float y)=>new(p.X,y,p.Y);
            var outward=new Vector3((outer[i].X+outer[j].X)/2-center.X,0,(outer[i].Y+outer[j].Y)/2-center.Z);
            BuoyQuad(body,P(keel[i],-1.55f),P(keel[j],-1.55f),P(outer[j],-.67f),P(outer[i],-.67f),outward);
            BuoyQuad(body,P(outer[i],-.67f),P(outer[j],-.67f),P(shoulder[j],-.14f),P(shoulder[i],-.14f),outward);
            BuoyQuad(body,P(shoulder[i],-.14f),P(shoulder[j],-.14f),P(deckOutline[j],0),P(deckOutline[i],0),outward);
            BuoyQuad(body,P(inner[i],-1.55f),P(inner[i],0),P(inner[j],0),P(inner[j],-1.55f),-outward);
            BuoyQuad(body,P(keel[i],-1.55f),P(inner[i],-1.55f),P(inner[j],-1.55f),P(keel[j],-1.55f),Vector3.Down);
            BuoyQuad(deck,P(deckOutline[i],0),P(deckOutline[j],0),P(inner[j],0),P(inner[i],0),Vector3.Up);
        }
        body.Index();deck.Index();
        AddChild(new MeshInstance3D{Name="GlassBuoyPontoonBody",Mesh=body.Commit(),MaterialOverride=enamel});
        AddChild(new MeshInstance3D{Name="GlassBuoyRimDeck",Mesh=deck.Commit(),MaterialOverride=enamel});

        foreach(float x in new[]{1.1f,Width-1.1f})foreach(float z in new[]{1.1f,Depth-1.1f})
        {
            var root=new Node3D{Name=$"GlassBuoyCornerFloat{x}_{z}"};AddChild(root);
            RoundedBox(root,new(x,-.96f,z),new(4,1.65f,4),enamel,.9f);
            RoundedBox(root,new(x,-.58f,z),new(4.06f,.30f,4.06f),rubber,.95f);
        }

        // Broad rubber footing is continuous across the nonslip rim, without a
        // fine checkerboard. All tread tops are flush with the authority floor.
        foreach(float x in new[]{2f,Width-2f})
            Box(this,new(x,-.029f,Depth/2f),new(1.4f,.07f,Depth-2.8f),rubber,.04f).Name="GlassBuoyRubberTread";
        foreach(float z in new[]{2f,Depth-2f})
            Box(this,new(Width/2f,-.029f,z),new(Width-6,.07f,1.4f),rubber,.04f).Name="GlassBuoyRubberTread";

        // Enamel coamings stay inside the real outer wall cells, with their top
        // matching the authored crown. They do not invent a raised walking route.
        float wall=Layout.At(0,1).ObstacleHeight!.Value*HeightScale;
        foreach(float side in new[]{.5f,Width-.5f})
        {
            Box(this,new(side,wall/2,Depth/2f),new(.82f,wall,Depth-1),enamel,.12f).Name="GlassBuoyCoaming";
            Box(this,new(Width/2f,wall/2,side),new(Width-1,wall,.82f),enamel,.12f).Name="GlassBuoyCoaming";
        }
        foreach(float x in new[]{3.0f,Width-3.0f})
            Box(this,new(x+(x<Width/2f?-.075f:.075f),-.055f,Depth/2f),new(.15f,.11f,Depth-6),enamel,.055f).Name="GlassBuoyGlassBevel";
        foreach(float z in new[]{3.0f,Depth-3.0f})
            Box(this,new(Width/2f,-.055f,z+(z<Depth/2f?-.075f:.075f)),new(Width-6,.11f,.15f),enamel,.055f).Name="GlassBuoyGlassBevel";
        for(int pane=1;pane<=2;pane++)
        {
            float x=3+(Width-6)*pane/3f,z=3+(Depth-6)*pane/3f;
            Box(this,new(x,-.019f,Depth/2f),new(.12f,.05f,Depth-6),steel,.018f).Name="GlassBuoyFlushMullion";
            Box(this,new(Width/2f,-.019f,z),new(Width-6,.05f,.12f),steel,.018f).Name="GlassBuoyFlushMullion";
        }
        for(int i=0;i<7;i++)foreach(float side in new[]{1.15f,Width-1.15f})
        {
            float along=3+i*(Width-6)/6f;
            Bead(this,new(side,.025f,along),new(.19f,.09f,.19f),steel);
            Bead(this,new(along,.025f,side),new(.19f,.09f,.19f),steel);
        }

        var glass=new SurfaceTool();glass.Begin(Mesh.PrimitiveType.Triangles);
        BuoyQuad(glass,new(3,0,3),new(Width-3,0,3),new(Width-3,0,Depth-3),new(3,0,Depth-3),Vector3.Up);
        glass.Index();AddChild(new MeshInstance3D{Name="SlipperyVioletGlass",Mesh=glass.Commit(),MaterialOverride=_seaGlass});
        for(int z=1;z<Depth-1;z++)for(int x=1;x<Width-1;x++)
            if(Layout.At(x,z).Kind==Bomber.Game.Simulation.Stages.CellKind.Pillar)
                BuildBuoyBeacon(x,z,enamel,rubber,orange);

        // Menu miniatures have their own plinth. The world ocean is terrain,
        // including on Low, but never spreads across the menu theatre.
        if(IncludeScenery)BuildBuoyOcean();
    }

    private Vector2[] BuoyInnerContour()=>
        [new(3,3),new(3,3),new(Width-3,3),new(Width-3,3),
            new(Width-3,Depth-3),new(Width-3,Depth-3),new(3,Depth-3),new(3,Depth-3)];

    private static Vector2[] BuoyOuterContour(float left,float right,float rear,float front,float chamfer)=>
        [new(left,rear+chamfer),new(left+chamfer,rear),new(right-chamfer,rear),new(right,rear+chamfer),
            new(right,front-chamfer),new(right-chamfer,front),new(left+chamfer,front),new(left,front-chamfer)];

    private static void BuoyQuad(SurfaceTool surface,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
    {BuoyTriangle(surface,a,b,c,outward);BuoyTriangle(surface,a,c,d,outward);}

    private static void BuoyTriangle(SurfaceTool surface,Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
    {
        var normal=(c-a).Cross(b-a);if(normal.LengthSquared()<1e-12f)return;
        normal=normal.Normalized();
        if(normal.Dot(outward)<0){(b,c)=(c,b);normal=-normal;}
        foreach(var point in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(point.X,point.Z));surface.AddVertex(point);}
    }

    private void BuildBuoyOcean()
    {
        var ocean=new SurfaceTool();ocean.Begin(Mesh.PrimitiveType.Triangles);
        for(int z=-80;z<80;z++)for(int x=-80;x<80;x++)
        {
            // The sealed glass window replaces the free water surface inside
            // the ring, so its depth view does not blend two transparent skins.
            if(x>=3&&x<Width-3&&z>=3&&z<Depth-3)continue;
            float cx=Width/2f,cz=Depth/2f;
            BuoyQuad(ocean,new(x-cx,0,z-cz),new(x+1-cx,0,z-cz),new(x+1-cx,0,z+1-cz),new(x-cx,0,z+1-cz),Vector3.Up);
        }
        ocean.Index();
        _water=new MeshInstance3D{Name="ObservationSea",Mesh=ocean.Commit(),MaterialOverride=_art.Water(),
            Position=new(Width/2f,-.53f,Depth/2f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,ExtraCullMargin=.08f};AddChild(_water);
        Box(this,new(Width/2f,SeabedHeight-.025f,Depth/2f),new(160,.05f,160),_art.Color(new("1a3652"),.96f)).Name="Seabed";
        TuneBuoyWater();
    }

    private void TuneBuoyWater()
    {
        if(_water?.MaterialOverride is not ShaderMaterial water)return;
        bool night=_lighting?.Label=="Night";
        water.SetShaderParameter("deep_color",new Color(night?"08172e":"123859"));
        water.SetShaderParameter("surface_color",new Color(night?"183c5b":"3d7194"));
        foreach(var paint in _buoyBeaconPaint)
        {
            paint.AlbedoColor=new Color(night?"ffd38b":"e9ad50");
            paint.EmissionEnergyMultiplier=night?1.25f:.08f;
        }
        // The lenses carry the signal on Low, which has no local lights. At
        // higher presets one fore beacon and the distant buoy supply two soft pools.
        int pools=0;
        foreach(var light in _buoyLights)
            light.LightEnergy=night&&GraphicsQuality.Current!=GraphicsPreset.Low&&pools++<2
                ?(light.Name=="BellBuoyWarmLight"?1.1f:.85f):0;
    }

    private void ProcessGlassBuoy(float delta)
    {
        if(WeatherActive)_seaClock+=delta;
        _seaGlass?.SetShaderParameter("ambient_clock",_seaClock);
        if(_graphicsRevision==GraphicsQuality.Revision)return;
        if(_seaGlass is not null)
        {
            _seaGlass.Shader=ArtMaterials.ObservationGlassShaderFor(GraphicsQuality.Current);
            GraphicsQuality.ApplyShaderDetail(_seaGlass);
        }
        if(_water?.MaterialOverride is ShaderMaterial water)
        {
            water.Shader=ArtMaterials.WaterShaderFor(GraphicsQuality.Current);
            if(_lighting is not null)_lighting.ApplyWater(water);
            GraphicsQuality.ApplyShaderDetail(water);TuneBuoyWater();
        }
        _graphicsRevision=GraphicsQuality.Revision;
    }

    private void BuildBuoyBeacon(int x,int z,Material enamel,Material rubber,Material orange)
    {
        float height=Layout.At(x,z).ObstacleHeight!.Value*HeightScale;
        var root=new Node3D{Name=$"CoralOrangeTower{x}_{z}",Position=new(x+.5f,0,z+.5f)};AddChild(root);
        Box(root,new(0,.11f,0),new(.99f,.22f,.99f),enamel,.08f);
        RoundedBox(root,new(0,(height-.5f)/2,0),new(.77f,height-.5f,.77f),orange,.65f);
        Box(root,new(0,height-.56f,0),new(.86f,.18f,.86f),rubber,.06f);
        var lens=new StandardMaterial3D{AlbedoColor=new("e9ad50"),Roughness=.42f,
            ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,DisableFog=true,
            EmissionEnabled=true,Emission=new("ffb959"),EmissionEnergyMultiplier=.08f};
        _buoyBeaconPaint.Add(lens);
        Bead(root,new(0,height-.32f,0),new(.66f,.45f,.66f),lens).Name="BeaconLampLens";
        Box(root,new(0,height-.085f,0),new(.91f,.17f,.91f),enamel,.065f);
        foreach(float side in new[]{-.3f,.3f})
            Box(root,new(side,height-.32f,.3f),new(.12f,.43f,.12f),rubber,.025f);
        Bead(root,new(0,height-.034f,0),new(.44f,.08f,.44f),lens).Name="BeaconCapLens";
        if(IncludeScenery&&x==Width/2&&z<Depth/2)
        {
            var light=new OmniLight3D{Name="BeaconWarmLight",Position=new(0,height+.12f,0),LightColor=new("ffb65a"),
                LightEnergy=.05f,OmniRange=4.2f,OmniAttenuation=2.0f,ShadowEnabled=false};
            root.AddChild(light);_buoyLights.Add(light);
        }
    }
}

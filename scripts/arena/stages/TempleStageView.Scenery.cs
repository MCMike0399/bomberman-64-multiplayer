using Godot;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;

namespace BlastCircuit.Arena.Stages;

/// <summary>Decorative life around each arena: original procedural props beyond the
/// authority's cells, with no collision bodies. One root per arena lets the menu
/// miniature hide it and lets QA audit its placement.</summary>
public partial class TempleStageView
{
    public const string SceneryNodeName="StageScenery";
    private Node3D? _scenery;
    private readonly List<Action<float>> _sceneryMotion=[];
    private readonly Dictionary<string,StandardMaterial3D> _sceneryMaterials=[];
    private readonly Dictionary<string,ShaderMaterial> _foliageMaterials=[];
    private readonly List<ShaderMaterial> _ambientMaterials=[];
    private static readonly Dictionary<string,Mesh> SceneryMeshes=[];
    private float _sceneryClock;
    public Node3D? Scenery=>_scenery;
    /// <summary>Menu miniatures skip scenery: it would be trimmed anyway and costs build time.</summary>
    public bool IncludeScenery {get;init;}=true;
    /// <summary>Animated props: boats, birds, flags, flames, beams and blades.</summary>
    public int SceneryMotionCount=>_sceneryMotion.Count;

    private void BuildScenery()
    {
        if(!IncludeScenery)return;
        Action? build=Layout.Id switch
        {
            BlastStages.SunkenTemple=>BuildSunkenTempleExterior,
            BlastStages.RockGarden=>BuildRockGardenExterior,
            BlastStages.UpAndDown=>BuildUpAndDownExterior,
            BlastStages.TopRules=>BuildTopRulesExterior,
            BlastStages.BlizzardBattle=>BuildBlizzardExterior,
            BlastStages.GreedyTrap=>BuildGreedyLife,
            BlastStages.CircuitGarden=>BuildCircuitGardenExterior,
            _=>null
        };
        // Props are authored around the real arena footprint. Older or reduced packets
        // keep only the terrain they actually received.
        var authored=build==null?null:BlastStages.Get(Layout.Id);
        if(build==null||authored==null||authored.Width!=Width||authored.Height!=Depth)return;
        _scenery=new Node3D{Name=SceneryNodeName,Visible=GraphicsQuality.Current!=GraphicsPreset.Low};AddChild(_scenery);
        build();
    }
    /// <summary>Low hides every decoration; the arena, its water and the Greedy reservoir town remain.</summary>
    public void SetSceneryVisible(bool visible){if(_scenery!=null)_scenery.Visible=visible;}

    private void AnimateScenery(float delta)
    {
        if(_scenery==null||!_scenery.Visible||!WeatherActive)return;
        _sceneryClock+=delta;
        foreach(var motion in _sceneryMotion)motion(_sceneryClock);
        foreach(var material in _ambientMaterials)material.SetShaderParameter("ambient_clock",_sceneryClock);
    }

    /// <summary>The visible surface floating props ride, including a rising flood.</summary>
    private float SceneryWaterHeight=>_greedyMoat?.Position.Y??_water?.Position.Y??-.53f;

    private Node3D Prop(Node3D parent,string name,Vector3 position,float yaw=0,float scale=1)
    {
        var node=new Node3D{Name=name,Position=position,Rotation=new(0,yaw,0),Scale=Vector3.One*scale};
        parent.AddChild(node);return node;
    }

    /// <summary>Unshaded emissive paint for flames, lamps and stars; bloom does the rest.</summary>
    private StandardMaterial3D Glow(Color color,float energy=1.6f,float alpha=1)
    {
        string key=$"glow:{color.ToHtml()}:{energy}:{alpha}";
        if(_sceneryMaterials.TryGetValue(key,out var material))return material;
        material=new StandardMaterial3D{AlbedoColor=new(color,alpha),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled=true,Emission=color,EmissionEnergyMultiplier=energy,DisableFog=true};
        if(alpha<1){material.Transparency=BaseMaterial3D.TransparencyEnum.Alpha;material.DepthDrawMode=BaseMaterial3D.DepthDrawModeEnum.Disabled;material.CullMode=BaseMaterial3D.CullModeEnum.Disabled;}
        return _sceneryMaterials[key]=material;
    }

    /// <summary>Cloth needs both faces: sails and pennants turn in the wind.</summary>
    private StandardMaterial3D Cloth(Color color)
    {
        string key="cloth:"+color.ToHtml();
        if(_sceneryMaterials.TryGetValue(key,out var material))return material;
        material=new StandardMaterial3D{AlbedoColor=color,Roughness=.85f,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
        return _sceneryMaterials[key]=material;
    }

    private static MeshInstance3D Taper(Node parent,Vector3 position,float bottom,float top,float height,Material material,int sides=20)
    {
        string key=$"taper:{bottom}:{top}:{height}:{sides}";
        if(!SceneryMeshes.TryGetValue(key,out var mesh))
            SceneryMeshes[key]=mesh=PrimitiveTemplates.Get("CylinderMesh",("bottom_radius",bottom),("top_radius",top),("height",height),("radial_segments",sides),("rings",1));
        var node=new MeshInstance3D{Mesh=mesh,Position=position,MaterialOverride=material};parent.AddChild(node);return node;
    }

    /// <summary>A low-poly sphere for small props (petals, eyes, buttons, coconuts).</summary>
    private static MeshInstance3D Bead(Node parent,Vector3 position,Vector3 scale,Material material)
    {
        if(!SceneryMeshes.TryGetValue("bead",out var mesh))
            SceneryMeshes["bead"]=mesh=PrimitiveTemplates.Get("SphereMesh",("radius",.5f),("height",1),("radial_segments",10),("rings",5));
        var node=new MeshInstance3D{Mesh=mesh,Position=position,Scale=scale,MaterialOverride=material};parent.AddChild(node);return node;
    }

    private static MeshInstance3D Cone(Node parent,Vector3 position,float radius,float height,Material material,int sides=20)=>
        Taper(parent,position,radius,0,height,material,sides);

    /// <summary>A closed, convex outline extruded along Z (sails, flags, wings).</summary>
    private static MeshInstance3D Plate(Node parent,Vector3 position,Vector2[] outline,float thickness,Material material)
    {
        string key="plate:"+string.Join(";",outline.Select(p=>p.ToString()))+":"+thickness;
        if(!SceneryMeshes.TryGetValue(key,out var mesh))
        {
            var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
            var center=new Vector3(outline.Average(p=>p.X),outline.Average(p=>p.Y),0);
            void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {
                // Godot's clockwise front faces, oriented away from the solid's center.
                var normal=(c-a).Cross(b-a).Normalized();
                if(normal.Dot((a+b+c)/3-center)<0){(b,c)=(c,b);normal=-normal;}
                foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.X,p.Y));surface.AddVertex(p);}
            }
            Vector3 V(Vector2 p,float z)=>new(p.X,p.Y,z);
            float h=thickness/2;
            for(int i=1;i<outline.Length-1;i++)
            {
                Triangle(V(outline[0],h),V(outline[i],h),V(outline[i+1],h));
                Triangle(V(outline[0],-h),V(outline[i+1],-h),V(outline[i],-h));
            }
            for(int i=0;i<outline.Length;i++)
            {
                var a=outline[i];var b=outline[(i+1)%outline.Length];
                Triangle(V(a,h),V(b,h),V(b,-h));Triangle(V(a,h),V(b,-h),V(a,-h));
            }
            SceneryMeshes[key]=mesh=surface.Commit();
        }
        var node=new MeshInstance3D{Mesh=mesh,Position=position,MaterialOverride=material};parent.AddChild(node);return node;
    }

    private Mesh SceneryRock(int variant)
    {
        string key="rock:"+variant;
        if(!SceneryMeshes.TryGetValue(key,out var mesh))SceneryMeshes[key]=mesh=GardenRockMesh(variant);
        return mesh;
    }

    private MeshInstance3D Rock(Node3D parent,Vector3 position,Vector3 scale,Material material,int variant,float yaw=0)
    {
        var rock=new MeshInstance3D{Name="Rock",Mesh=SceneryRock(variant%4),MaterialOverride=material,Position=position,Basis=PresentationMath.Rotation(new(0,yaw,0),scale)};
        parent.AddChild(rock);return rock;
    }

    // ---------------------------------------------------------------- vegetation

    private Node3D PalmTree(Node3D parent,Vector3 position,float height,float lean,float yaw,float phase)
    {
        var root=Prop(parent,"PalmTree",position,yaw);
        var sway=new Node3D{Name="Sway"};root.AddChild(sway);
        var bark=_art.Color(new("9c7a52"),.92f);var ring=_art.Color(new("7a5a3a"),.92f);
        var frond=_art.Color(new("4e9b45"),.8f);var frondDark=_art.Color(new("367a3a"),.8f);
        const int segments=7;
        for(int i=0;i<segments;i++)
        {
            float t=(i+.5f)/segments;
            var trunk=Taper(sway,new(lean*t*t*height,t*height,0),.15f-.04f*t,.13f-.04f*t,height/segments*1.12f,i%2==0?bark:ring,10);
            trunk.Basis=PresentationMath.Rotation(new(0,0,-PresentationMath.Atan(2*lean*t)));
        }
        var crown=new Node3D{Name="Crown",Position=new(lean*height,height,0)};sway.AddChild(crown);
        for(int f=0;f<8;f++)
        {
            var pivot=new Node3D{Rotation=new(0,f*Mathf.Tau/8+.2f,0)};crown.AddChild(pivot);
            var leaf=Sphere(pivot,new(.6f,-.16f,0),new(1.3f,.07f,.32f),f%2==0?frond:frondDark);
            leaf.Rotation=new(0,0,-.42f-(f%3)*.08f);
        }
        var nut=_art.Color(new("6b4a2b"),.6f);
        for(int n=0;n<3;n++)Bead(crown,new(Mathf.Cos(n*2.1f)*.13f,-.13f,Mathf.Sin(n*2.1f)*.13f),Vector3.One*.17f,nut);
        _sceneryMotion.Add(t=>sway.Rotation=new(Mathf.Sin(t*.8f+phase)*.025f,0,Mathf.Sin(t*1.05f+phase)*.04f));
        return root;
    }

    private Node3D RoundTree(Node3D parent,Vector3 position,float scale,Color leaves,float phase)
    {
        var root=Prop(parent,"RoundTree",position,phase,scale);
        Taper(root,new(0,.45f,0),.17f,.12f,.9f,_art.Color(new("7a5f45"),.9f),10);
        var canopy=new Node3D{Name="Canopy",Position=new(0,1.35f,0)};root.AddChild(canopy);
        Sphere(canopy,Vector3.Zero,new(1.5f,1.35f,1.5f),WindPaint(leaves));
        Sphere(canopy,new(-.38f,.32f,.15f),new(.95f,.9f,.95f),WindPaint(leaves.Lightened(.14f)));
        Sphere(canopy,new(.4f,.12f,-.25f),new(.85f,.8f,.85f),WindPaint(leaves.Darkened(.1f)));
        // A few leaf clusters break the balloon silhouette and flutter locally.
        for(int i=0;i<(_garden?7:0);i++)
        {
            float a=i*Mathf.Tau/7;
            Bead(canopy,new(Mathf.Cos(a)*.65f,.2f+(i%3)*.18f,Mathf.Sin(a)*.65f),new(.42f,.28f,.38f),WindPaint(leaves.Lightened(.08f)));
        }
        if(_garden)_sceneryMotion.Add(t=>canopy.Rotation=new(Mathf.Sin(t*.9f+position.X*.63f+position.Z*.37f)*.055f,0,Mathf.Sin(t*1.3f+phase)*.065f));
        else _sceneryMotion.Add(t=>canopy.Rotation=new(Mathf.Sin(t*.7f+phase)*.03f,0,Mathf.Sin(t*.9f+phase)*.035f));
        return root;
    }

    private Node3D PineTree(Node3D parent,Vector3 position,float scale,bool snow,float phase)
    {
        var root=Prop(parent,"PineTree",position,phase,scale);
        Taper(root,new(0,.25f,0),.13f,.1f,.5f,_art.Color(new("6d5038"),.9f),10);
        var needles=_art.Color(new(snow?"2f5f55":"2f6b45"),.88f);var white=_art.Color(new("f2f7fb"),.55f);
        for(int tier=0;tier<3;tier++)
        {
            float y=.45f+tier*.55f,radius=.78f-tier*.2f;
            Cone(root,new(0,y+.42f,0),radius,.95f,needles,9);
            if(snow)Cone(root,new(0,y+.62f,0),radius*.68f,.55f,white,9);
        }
        return root;
    }

    private void Bush(Node3D parent,Vector3 position,float scale,Color color)
    {
        var root=Prop(parent,"Bush",position,position.X*1.7f,scale);
        Sphere(root,new(0,.22f,0),new(.9f,.55f,.8f),WindPaint(color));
        Sphere(root,new(.32f,.18f,.12f),new(.55f,.42f,.55f),WindPaint(color.Lightened(.12f)));
        Sphere(root,new(-.3f,.16f,-.1f),new(.5f,.38f,.5f),WindPaint(color.Darkened(.08f)));
    }

    private void FlowerBed(Node3D parent,Vector3 center,float radius,int count,int seed)
    {
        var root=Prop(parent,"FlowerBed",center);
        var random=new Random(seed);
        Color[] petals=[new("f26d8f"),new("ffd34d"),new("ffffff"),new("e8473f"),new("b07ae0"),new("ff9f45")];
        Cylinder(root,new(0,.02f,0),radius,.06f,_art.Color(new("5c4632"),.95f));
        for(int i=0;i<count;i++)
        {
            float angle=(float)random.NextDouble()*Mathf.Tau,distance=Mathf.Sqrt((float)random.NextDouble())*radius*.86f;
            var at=new Vector3(Mathf.Cos(angle)*distance,.1f,Mathf.Sin(angle)*distance);
            Bead(root,at,new(.2f,.14f,.2f),_art.Color(new("4c8a3c"),.9f));
            Bead(root,at+new Vector3(0,.12f,0),Vector3.One*.13f,_art.Color(petals[random.Next(petals.Length)],.7f));
        }
    }

    // ---------------------------------------------------------------- creatures

    /// <summary>A gull wheeling around a fixed center, never across the arena.</summary>
    private void Gull(Node3D parent,Vector3 center,float radius,float speed,float phase)
    {
        var pivot=Prop(parent,"Gull",center);
        var bird=new Node3D{Position=new(radius,0,0),Rotation=new(0,0,Math.Sign(speed)*.18f)};pivot.AddChild(bird);
        var white=_art.Color(new("f6f6f0"),.6f);var gray=_art.Color(new("8a949c"),.7f);
        Sphere(bird,Vector3.Zero,new(.17f,.15f,.46f),white);
        Sphere(bird,new(0,.05f,-.22f),Vector3.One*.15f,white);
        Cone(bird,new(0,.04f,-.33f),.03f,.12f,_art.Color(new("f2a531"),.6f),6).Rotation=new(-Mathf.Pi/2,0,0);
        var hinges=new Node3D[2];
        for(int side=0;side<2;side++)
        {
            float sign=side==0?-1:1;
            hinges[side]=new Node3D{Position=new(sign*.06f,.04f,0)};bird.AddChild(hinges[side]);
            Box(hinges[side],new(sign*.3f,0,0),new(.58f,.025f,.2f),white);
            Box(hinges[side],new(sign*.55f,0,.02f),new(.12f,.026f,.16f),gray);
        }
        var (left,right)=(hinges[0],hinges[1]);
        _sceneryMotion.Add(t=>
        {
            pivot.Rotation=new(0,phase+t*speed,0);
            bird.Position=new(radius,Mathf.Sin(t*.6f+phase)*.3f,0);
            // Flap in bursts, then glide.
            float flap=Mathf.Sin(t*9+phase)*.55f*Mathf.Clamp(Mathf.Sin(t*.9f+phase)*2,0,1)+.12f;
            left.Rotation=new(0,0,-flap);right.Rotation=new(0,0,flap);
        });
    }

    private void Butterfly(Node3D parent,Vector3 center,float radius,Color color,float phase)
    {
        var root=Prop(parent,"Butterfly",center);
        var body=new Node3D();root.AddChild(body);
        var paint=_art.Color(color,.6f);
        Bead(body,Vector3.Zero,new(.03f,.03f,.12f),_art.Color(new("2f2a2a")));
        var hinges=new Node3D[2];
        for(int side=0;side<2;side++)
        {
            float sign=side==0?-1:1;
            hinges[side]=new Node3D();body.AddChild(hinges[side]);
            Bead(hinges[side],new(sign*.08f,0,-.02f),new(.15f,.012f,.12f),paint);
            Bead(hinges[side],new(sign*.065f,0,.06f),new(.1f,.012f,.08f),paint);
        }
        _sceneryMotion.Add(t=>
        {
            float u=t*.55f+phase;
            var at=new Vector3(Mathf.Sin(u)*radius,.55f+Mathf.Sin(u*2.3f)*.18f,Mathf.Sin(u*1.7f)*radius*.7f);
            var ahead=new Vector3(Mathf.Cos(u)*radius,0,Mathf.Cos(u*1.7f)*radius*1.19f);
            body.Position=at;body.Rotation=new(0,Mathf.Atan2(-ahead.X,-ahead.Z),0);
            float flap=Mathf.Abs(Mathf.Sin(t*14+phase))*1.1f;
            hinges[0].Rotation=new(0,0,-flap);hinges[1].Rotation=new(0,0,flap);
        });
    }

    // ---------------------------------------------------------------- water life

    /// <summary>Floating props ride the actual water surface, including sudden-death floods.</summary>
    private void Float(Node3D node,float draft,float phase,float roll=.06f)
    {
        float yaw=node.Rotation.Y;
        _sceneryMotion.Add(t=>
        {
            node.Position=new(node.Position.X,SceneryWaterHeight-draft+Mathf.Sin(t*1.3f+phase)*.05f,node.Position.Z);
            node.Rotation=new(Mathf.Sin(t*1.1f+phase)*roll*.6f,yaw+Mathf.Sin(t*.25f+phase)*.08f,Mathf.Sin(t*.9f+phase*1.3f)*roll);
        });
    }

    private Node3D Sailboat(Node3D parent,Vector3 position,float yaw,Color hull,Color sail,float phase)
    {
        var boat=Prop(parent,"Sailboat",position,yaw);
        var trim=_art.Color(new("f3ead8"),.6f);
        Box(boat,new(0,.1f,0),new(.62f,.3f,1.5f),_art.Toy(hull,.5f),.12f);
        Box(boat,new(0,.27f,0),new(.66f,.06f,1.54f),trim,.03f);
        var bow=Box(boat,new(0,.12f,-.82f),new(.42f,.26f,.42f),_art.Toy(hull,.5f),.1f);bow.Rotation=new(0,Mathf.Pi/4,0);
        Box(boat,new(0,.33f,.38f),new(.44f,.16f,.42f),_art.Color(new("8a6342"),.8f),.04f);
        Cylinder(boat,new(0,1.05f,-.05f),.035f,1.55f,_art.Color(new("7a5a3a"),.8f));
        var sheet=Plate(boat,new(0,.38f,-.05f),[new(0,1.45f),new(0,0),new(.62f,0)],.02f,Cloth(sail));
        sheet.Rotation=new(0,Mathf.Pi/2,0);
        Plate(boat,new(0,1.86f,-.05f),[new(0,0),new(.28f,-.07f),new(0,-.14f)],.015f,Cloth(new("e8473f"))).Rotation=new(0,Mathf.Pi/2,0);
        Float(boat,.04f,phase);
        return boat;
    }

    private Node3D Rowboat(Node3D parent,Vector3 position,float yaw,Color hull,float phase)
    {
        var boat=Prop(parent,"Rowboat",position,yaw);
        Box(boat,new(0,.08f,0),new(.5f,.2f,1.1f),_art.Toy(hull,.55f),.09f);
        Box(boat,new(0,.12f,0),new(.4f,.14f,.92f),_art.Color(new("6b4d33"),.85f),.05f);
        Box(boat,new(0,.2f,.05f),new(.52f,.04f,.14f),_art.Color(new("a37d55"),.8f));
        foreach(int side in new[]{-1,1})
        {
            var oar=Box(boat,new(side*.34f,.2f,.1f),new(.04f,.04f,.9f),_art.Color(new("c9a26d"),.8f));oar.Rotation=new(0,side*.5f,side*-.2f);
        }
        Float(boat,.02f,phase,.04f);
        return boat;
    }

    private Node3D Buoy(Node3D parent,Vector3 position,Color color,float phase)
    {
        var buoy=Prop(parent,"Buoy",position);
        var paint=_art.Toy(color,.45f);var white=_art.Toy(new("f6f1e4"),.45f);
        Taper(buoy,new(0,.1f,0),.26f,.2f,.36f,paint,14);
        Taper(buoy,new(0,.33f,0),.2f,.16f,.12f,white,14);
        Taper(buoy,new(0,.46f,0),.16f,.05f,.18f,paint,14);
        Cylinder(buoy,new(0,.6f,0),.02f,.12f,_art.Color(new("444444")));
        var lamp=Sphere(buoy,new(0,.68f,0),Vector3.One*.08f,Glow(new("ffe28a"),2.2f));
        _sceneryMotion.Add(t=>lamp.Visible=Mathf.PosMod(t+phase,2.4f)<.5f);
        Float(buoy,.02f,phase,.12f);
        return buoy;
    }

    // ---------------------------------------------------------------- structures

    private Node3D Lighthouse(Node3D parent,Vector3 position,float height,float phase)
    {
        var root=Prop(parent,"Lighthouse",position);
        var red=_art.Toy(new("d8433b"),.5f);var white=_art.Toy(new("f4efe3"),.5f);var dark=_art.Color(new("3a3f47"),.6f);
        const int bands=5;float band=height/bands;
        for(int i=0;i<bands;i++)
        {
            float bottom=.62f-.2f*i/bands,top=.62f-.2f*(i+1)/bands;
            Taper(root,new(0,band*(i+.5f),0),bottom,top,band+.01f,i%2==0?white:red,24);
        }
        Cylinder(root,new(0,height+.04f,0),.58f,.08f,dark);
        for(int post=0;post<12;post++)
        {
            float a=post*Mathf.Tau/12;
            Box(root,new(Mathf.Cos(a)*.53f,height+.2f,Mathf.Sin(a)*.53f),new(.035f,.24f,.035f),dark);
        }
        Cylinder(root,new(0,height+.32f,0),.55f,.03f,dark);
        Cylinder(root,new(0,height+.38f,0),.3f,.62f,Glow(new("fff1b5"),1.2f));
        Cone(root,new(0,height+.92f,0),.42f,.46f,red,16);
        Sphere(root,new(0,height+1.2f,0),Vector3.One*.14f,dark);
        Box(root,new(0,.4f,.6f),new(.32f,.6f,.1f),_art.Color(new("4a3628"),.7f),.04f);
        var beam=new Node3D{Name="LighthouseBeam",Position=new(0,height+.4f,0)};root.AddChild(beam);
        var light=Glow(new("fff3c4"),1.3f,.3f);
        foreach(int side in new[]{-1,1})
        {
            var ray=Taper(beam,new(side*3.3f,0,0),.12f,.55f,6.2f,light,12);
            ray.Rotation=new(0,0,side*Mathf.Pi/2);ray.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        }
        _sceneryMotion.Add(t=>beam.Rotation=new(0,phase+t*.9f,0));
        return root;
    }

    private Node3D Windmill(Node3D parent,Vector3 position,float yaw)
    {
        var root=Prop(parent,"Windmill",position,yaw);
        var wall=_art.Color(new("efe3c7"),.85f);var roof=_art.Toy(new("b5533c"),.6f);var wood=_art.Color(new("7b5a3c"),.8f);
        Taper(root,new(0,1.3f,0),.95f,.68f,2.6f,wall,20);
        Cone(root,new(0,2.95f,0),.9f,.75f,roof,20);
        Box(root,new(0,.42f,.86f),new(.42f,.75f,.12f),wood,.05f);
        Box(root,new(0,1.7f,.73f),new(.3f,.3f,.08f),_art.Color(new("5c88a8"),.4f),.04f);
        var hub=new Node3D{Name="WindmillSails",Position=new(0,2.35f,.95f)};root.AddChild(hub);
        Cylinder(hub,Vector3.Zero,.13f,.22f,wood).Rotation=new(Mathf.Pi/2,0,0);
        var canvas=_art.Color(new("f6efe0"),.85f);
        for(int i=0;i<4;i++)
        {
            var arm=new Node3D{Rotation=new(0,0,i*Mathf.Pi/2)};hub.AddChild(arm);
            Box(arm,new(0,.95f,.06f),new(.07f,1.9f,.06f),wood);
            Box(arm,new(.2f,1.05f,.07f),new(.36f,1.45f,.025f),canvas,.01f);
        }
        _sceneryMotion.Add(t=>hub.Rotation=new(0,0,-t*.65f));
        return root;
    }

    private Node3D StoneLantern(Node3D parent,Vector3 position,float yaw)
    {
        var root=Prop(parent,"StoneLantern",position,yaw);
        var stone=_art.Color(new("a9a593"),.95f);var moss=_art.Color(new("6f8a52"),.95f);
        Box(root,new(0,.08f,0),new(.6f,.16f,.6f),stone,.03f);
        Cylinder(root,new(0,.48f,0),.11f,.66f,stone);
        Box(root,new(0,.84f,0),new(.5f,.08f,.5f),stone,.02f);
        Box(root,new(0,1.04f,0),new(.36f,.32f,.36f),stone,.03f);
        Box(root,new(0,1.04f,0),new(.38f,.16f,.2f),Glow(new("ffc66b"),1.4f),.02f);
        Box(root,new(0,1.04f,0),new(.2f,.16f,.38f),Glow(new("ffc66b"),1.4f),.02f);
        Cone(root,new(0,1.36f,0),.42f,.34f,stone,4).Rotation=new(0,Mathf.Pi/4,0);
        Sphere(root,new(0,1.56f,0),Vector3.One*.12f,stone);
        Sphere(root,new(.18f,.17f,.2f),new(.3f,.08f,.22f),moss);
        return root;
    }

    private void Flame(Node3D parent,Vector3 position,float scale,float phase)
    {
        var flame=new Node3D{Name="Flame",Position=position,Scale=Vector3.One*scale};parent.AddChild(flame);
        Sphere(flame,new(0,.16f,0),new(.32f,.5f,.32f),Glow(new("ff7a1f"),2.4f));
        Sphere(flame,new(0,.14f,0),new(.18f,.32f,.18f),Glow(new("ffe27a"),2.8f));
        _sceneryMotion.Add(t=>
        {
            float flicker=1+.14f*Mathf.Sin(t*13+phase)+.08f*Mathf.Sin(t*29+phase*2);
            flame.Scale=new Vector3(scale*(2-flicker)*.5f+scale*.5f,scale*flicker,scale*(2-flicker)*.5f+scale*.5f);
        });
    }

    private void Pennant(Node3D parent,Vector3 position,float pole,Color color,float phase)
    {
        Cylinder(parent,position+new Vector3(0,pole/2,0),.03f,pole,_art.Color(new("6a5640"),.7f));
        Sphere(parent,position+new Vector3(0,pole+.04f,0),Vector3.One*.09f,_art.Color(new("e2b64f"),.35f));
        var hinge=new Node3D{Name="Pennant",Position=position+new Vector3(0,pole-.08f,0)};parent.AddChild(hinge);
        Plate(hinge,Vector3.Zero,[new(0,0),new(.95f,-.17f),new(0,-.36f)],.02f,Cloth(color));
        _sceneryMotion.Add(t=>hinge.Basis=PresentationMath.Rotation(new(0,.6f+PresentationMath.Sin(t*1.7f+phase)*.35f+PresentationMath.Sin(t*4.3f+phase)*.08f,0)));
    }

    private void Cloud(Node3D parent,Vector3 position,float scale,float drift,float span,float phase)
    {
        var cloud=Prop(parent,"Cloud",position,0,scale);
        var white=_art.Color(new("f7f9fc"),1);
        Sphere(cloud,Vector3.Zero,new(2.4f,1.1f,1.6f),white);
        Sphere(cloud,new(-1,-.15f,.2f),new(1.6f,.9f,1.3f),white);
        Sphere(cloud,new(1.05f,-.1f,-.15f),new(1.8f,.95f,1.4f),white);
        Sphere(cloud,new(.25f,.4f,0),new(1.4f,1,1.2f),white);
        foreach(var child in cloud.GetChildren().OfType<GeometryInstance3D>())child.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        float x=position.X;
        _sceneryMotion.Add(t=>cloud.Position=new(x+Mathf.PosMod(t*drift+phase,span)-span/2,position.Y+Mathf.Sin(t*.3f+phase)*.15f,position.Z));
    }

    /// <summary>Distant, soft landform; depth of field and haze keep it in the background.</summary>
    private MeshInstance3D Hill(Node3D parent,Vector3 position,Vector3 scale,Color color)
    {
        var hill=Sphere(parent,position,scale,_art.Color(color,.95f));hill.Name="Hill";
        hill.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;return hill;
    }

    /// <summary>One MultiMesh draw of unshaded star points.</summary>
    private void Starfield(Node3D parent,Vector3 center,float spread,float depth,int count,int seed)
    {
        var random=new Random(seed);
        var multimesh=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,
            Mesh=PrimitiveTemplates.Get("SphereMesh",("radius",.5f),("height",1),("radial_segments",6),("rings",3)),InstanceCount=count};
        for(int i=0;i<count;i++)
        {
            float size=.08f+(float)random.NextDouble()*(.1f+(i%17==0?.18f:0));
            var at=new Vector3(((float)random.NextDouble()-.5f)*spread,-(float)random.NextDouble()*depth,((float)random.NextDouble()-.5f)*spread);
            multimesh.SetInstanceTransform(i,new Transform3D(Basis.Identity.Scaled(Vector3.One*size),at));
            multimesh.SetInstanceColor(i,i%5==0?new Color("ffe9b8"):i%7==0?new Color("b8d4ff"):new Color("ffffff"));
        }
        var material=new StandardMaterial3D{ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,VertexColorUseAsAlbedo=true,DisableFog=true};
        parent.AddChild(new MultiMeshInstance3D{Name="Starfield",Multimesh=multimesh,MaterialOverride=material,Position=center,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }
}

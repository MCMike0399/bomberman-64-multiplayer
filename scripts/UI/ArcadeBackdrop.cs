using Godot;
using Bomber.Client;
using BlastCircuit.Rendering;
namespace BlastCircuit.UI;

/// <summary>A lit miniature theatre behind the actual menu controls.</summary>
public partial class ArcadeBackdrop : SubViewportContainer
{
    public bool Quiet {get;set;}
    public bool Results {get;set;}
    public string Page {get;set;}="Modes";
    private readonly List<BomberModel> _heroes=[];
    private readonly List<BomberModel> _rosterModels=[];
    private readonly ArtMaterials _characterMaterials=new();
    private int _selectedSlot;
    private readonly int[] _renderedAppearances=new int[8];
    private LocalRoster? _roster;
    private SubViewport _viewport=null!;
    private Node3D _world=null!,_green=null!,_clouds=null!,_cabinet=null!;
    private Camera3D _camera=null!;
    private Godot.Environment _environment=null!;
    private Vector2 _parallax;
    private double _clock,_untilDraw;
    private bool _wasResults;
    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Ignore;Stretch=true;
        _viewport=new SubViewport {OwnWorld3D=true,Size=new(1440,900),Msaa3D=Viewport.Msaa.Msaa2X,RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};AddChild(_viewport);
        _world=new Node3D();_viewport.AddChild(_world);
        _environment=new Godot.Environment {BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("121129"),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("8f97c3"),AmbientLightEnergy=.48f,TonemapMode=Godot.Environment.ToneMapper.Filmic,ReflectedLightSource=Godot.Environment.ReflectionSource.Disabled};
        _world.AddChild(new WorldEnvironment {Environment=_environment});
        _world.AddChild(new DirectionalLight3D {RotationDegrees=new(-48,-35,0),LightColor=new Color("fff0d4"),LightEnergy=1.35f,ShadowEnabled=true,ShadowOpacity=.65f,DirectionalShadowMaxDistance=35,DirectionalShadowMode=DirectionalLight3D.ShadowMode.Parallel4Splits,ShadowBias=.08f});
        _world.AddChild(new DirectionalLight3D {RotationDegrees=new(-15,140,0),LightColor=new Color("98baff"),LightEnergy=.7f});
        _camera=new Camera3D {Position=new(0,4.5f,18.5f),Fov=43,Current=true};_world.AddChild(_camera);_camera.LookAt(new(0,1.6f,-1));
        _cabinet=new Node3D();_world.AddChild(_cabinet);
        var navy=Surface("202d62",.34f,.23f);var purple=Surface("332d64",.36f,.15f);var blue=Surface("3267ac",.24f,.5f);var gold=Surface("deb85e",.25f,.72f);
        Box(_cabinet,new(0,-.95f,-1),new(34,1.4f,21),navy);
        ChamferBox(_cabinet,new(0,-.17f,-1.3f),new(23,.24f,10),gold,.09f);
        Box(_cabinet,new(0,.02f,-1.3f),new(22.7f,.25f,9.7f),purple);
        Box(_cabinet,new(0,4,-6.9f),new(33,12,.7f),Surface("191c3e",.55f));
        // Recessed panels and railings create overlapping planes rather than a printed border.
        foreach(float x in new[]{-10.5f,10.5f})
        {
            Cylinder(_cabinet,new(x,3,-3.6f),.7f,6.1f,blue);
            foreach(float y in new[]{.1f,.45f,5.6f,5.95f})Cylinder(_cabinet,new(x,y,-3.6f),.87f,.18f,gold);
            Sphere(_cabinet,new(x,6.3f,-3.6f),new(.55f,.55f,.55f),Surface("e9a7d3",.25f,.25f));
            Box(_cabinet,new(x,-.2f,-3.6f),new(2,.5f,2),navy);
        }
        foreach(var (radius,color) in new[]{(10.1f,"784e9c"),(9.7f,"b782b7"),(9.3f,"395ab2")})
        {
            var rail=new MeshInstance3D {Mesh=new TorusMesh {InnerRadius=radius-.09f,OuterRadius=radius+.09f,Rings=64,RingSegments=12},MaterialOverride=Surface(color,.26f,.38f),Position=new(0,.5f,-5.8f),RotationDegrees=new(90,0,0)};rail.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;_cabinet.AddChild(rail);
        }
        // Floating bomb sculptures: low relief in the rear alcoves, never wallpaper.
        foreach(float x in new[]{-7.7f,7.7f})
        {
            var bomb=Sphere(_cabinet,new(x,3,-5.6f),Vector3.One*1.35f,Surface("292b46",.2f,.5f));
            Cylinder(bomb,new(.45f,.75f,0),.09f,.45f,gold).RotationDegrees=new(0,0,-28);
            Sphere(bomb,new(.57f,.98f,0),Vector3.One*.13f,Surface("f0b96a",.3f));
        }
        _green=new Node3D();_world.AddChild(_green);
        ChamferBox(_green,new(0,.23f,-1),new(29,.35f,8.7f),gold,.10f);
        Box(_green,new(0,.45f,-1),new(28.8f,.22f,8.5f),Surface("247752",.42f,.05f));
        Box(_green,new(0,3.25f,-5.9f),new(32,5.6f,.5f),Surface("205c49",.65f));
        for(int i=-4;i<=4;i++)Box(_green,new(i*2.65f,3.3f,-5.55f),new(.025f,5.1f,.04f),Surface("6b9b72",.5f,.3f));
        for(int i=0;i<3;i++)
        {
            int suit=i==0?1:i==1?0:2;
            var pedestal=new Node3D {Position=new(-8.2f+i*2.7f,.17f,1.25f+(i==1?.1f:0)),Scale=Vector3.One*(i==1?2.25f:1.85f)};_world.AddChild(pedestal);
            var hero=new BomberModel(_characterMaterials,ArtMaterials.PlayerColors[suit],"");
            pedestal.AddChild(hero);hero.ShowName(false);_heroes.Add(hero);
        }
        for(int i=0;i<8;i++)
        {
            var pedestal=new Node3D {Position=new(-8.4f+i*2.4f,.58f,1.6f),Scale=Vector3.One*1.93f};_world.AddChild(pedestal);
            var player=new BomberModel(_characterMaterials,ArtMaterials.PlayerColors[i],"");
            pedestal.AddChild(player);player.ShowName(false);_rosterModels.Add(player);
        }
        _clouds=new Node3D();_world.AddChild(_clouds);
        var cloud=new ShaderMaterial {Shader=new Shader {Code="""
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back;
void fragment() {
    float facing=clamp(dot(normalize(NORMAL),normalize(VIEW)),0.0,1.0);
    ALBEDO=vec3(0.88,0.94,1.0);
    ROUGHNESS=1.0;
    ALPHA=smoothstep(0.0,0.85,facing)*0.82;
}
"""}};
        for(int i=0;i<8;i++)
        {
            var cluster=new Node3D {Position=new((i*7.17f)%25-12.5f,(i*3.41f)%9-1,-5-(i%3)*2)};_clouds.AddChild(cluster);
            for(int j=0;j<5;j++){var puff=Sphere(cluster,new((j-2)*.76f,Mathf.Sin(j*1.7f)*.25f,0),new(1.85f,1.25f+(j%2)*.45f,1.4f),cloud);puff.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;}
        }
    }
    public LineupPose Pose(int slot)=>slot<_rosterModels.Count?new(_clock,_rosterModels[slot].WalkPose,_rosterModels[slot].Position.Y,_renderedAppearances[slot],_rosterModels[slot].IsVisibleInTree()):default;
    public void ConfigureRoster(LocalRoster roster,int selected)
    {
        _roster=roster;_selectedSlot=selected;
        for(int i=0;i<_rosterModels.Count;i++)
        {
            _renderedAppearances[i]=roster.Slots[i].Appearance;
            _rosterModels[i].SetSuitColor(ArtMaterials.PlayerColors[roster.Slots[i].Appearance]);
            Dim(_rosterModels[i],roster.Slots[i].Role==RosterRole.Off?.80f:0);
        }
    }
    private static void Dim(Node node,float transparency)
    {
        if(node is GeometryInstance3D geometry)geometry.Transparency=transparency;
        foreach(var child in node.GetChildren())Dim(child,transparency);
    }
    internal static StandardMaterial3D Surface(string color,float roughness=.4f,float metallic=0)=>new(){AlbedoColor=new Color(color),Roughness=roughness,Metallic=metallic};
    internal static MeshInstance3D Box(Node parent,Vector3 position,Vector3 size,Material material)
    {var mesh=new MeshInstance3D {Position=position,Mesh=new BoxMesh {Size=size},MaterialOverride=material};parent.AddChild(mesh);return mesh;}
    internal static MeshInstance3D ChamferBox(Node parent,Vector3 position,Vector3 size,Material material,float bevel=.08f)
    {
        var builder=new SurfaceTool();builder.Begin(Mesh.PrimitiveType.Triangles);
        float halfX=size.X/2,halfY=size.Y/2,halfZ=size.Z/2;
        Vector3[] Ring(float x,float y,float z,float corner)=>[new(-x+corner,y,z),new(x-corner,y,z),new(x,y-corner,z),new(x,-y+corner,z),new(x-corner,-y,z),new(-x+corner,-y,z),new(-x,-y+corner,z),new(-x,y-corner,z)];
        var rear=Ring(halfX-bevel,halfY-bevel,-halfZ,bevel*.3f);
        var rim=Ring(halfX,halfY,halfZ-bevel,bevel);
        var face=Ring(halfX-bevel,halfY-bevel,halfZ,bevel*.3f);
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            var normal=(b-a).Cross(c-a).Normalized();
            foreach(var v in new[]{a,b,c}){builder.SetNormal(normal);builder.AddVertex(v);}
        }
        for(int i=0;i<8;i++)
        {
            int j=(i+1)%8;
            Triangle(new(0,0,halfZ),face[j],face[i]);
            Triangle(new(0,0,-halfZ),rear[i],rear[j]);
            Triangle(rear[i],rim[i],rim[j]);Triangle(rear[i],rim[j],rear[j]);
            Triangle(rim[i],face[i],face[j]);Triangle(rim[i],face[j],rim[j]);
        }
        var mesh=new MeshInstance3D {Position=position,Mesh=builder.Commit(),MaterialOverride=material};parent.AddChild(mesh);return mesh;
    }
    internal static MeshInstance3D Sphere(Node parent,Vector3 position,Vector3 scale,Material material)
    {var mesh=new MeshInstance3D {Position=position,Scale=scale,Mesh=new SphereMesh {Radius=.5f,Height=1,RadialSegments=32,Rings=16},MaterialOverride=material};parent.AddChild(mesh);return mesh;}
    internal static MeshInstance3D Cylinder(Node parent,Vector3 position,float radius,float height,Material material)
    {var mesh=new MeshInstance3D {Position=position,Mesh=new CylinderMesh {TopRadius=radius,BottomRadius=radius,Height=height,RadialSegments=48},MaterialOverride=material};parent.AddChild(mesh);return mesh;}
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree())return;
        _clock+=delta;_untilDraw+=delta;
        if(_untilDraw<1d/30)return;float dt=(float)_untilDraw;_untilDraw=0;
        for(int i=0;i<_heroes.Count;i++)
        {
            var hero=_heroes[i];hero.Visible=Page=="Modes";
            if(hero.Visible){hero.Animate(dt,i==1?dt*.35f:0);hero.RotationDegrees=new(0,i==0?10:i==2?-12:0,0);}
        }
        for(int i=0;i<_rosterModels.Count;i++)
        {
            var player=_rosterModels[i];player.Visible=Quiet&&!Results;
            if(player.Visible)player.Animate(dt,i==_selectedSlot&&_roster?.Slots[i].Role!=RosterRole.Off?dt*2.1f:0);
        }
        _green.Visible=Quiet&&!Results;_clouds.Visible=Results;_cabinet.Visible=!Results;
        if(_wasResults!=Results){_wasResults=Results;_environment.BackgroundColor=new Color(Results?"73b6e4":"121129");_environment.AmbientLightEnergy=Results?.65f:.48f;}
        var mouse=GetGlobalMousePosition()/GetViewportRect().Size-new Vector2(.5f,.5f);
        _parallax=_parallax.Lerp(mouse,1-Mathf.Exp(-dt*2));
        _camera.Position=new(_parallax.X*.32f,4.5f-_parallax.Y*.18f,18.5f);
        _camera.LookAt(new Vector3(0,1.6f,-1));
        _clouds.Position=new(Mathf.Sin((float)_clock*.035f)*.6f,0,0);
        _viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
    }
}

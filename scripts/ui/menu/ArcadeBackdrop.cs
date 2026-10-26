using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Simulation.Stages;
using System.Text.Json;
using BlastCircuit.Arena.Models;
using BlastCircuit.Rendering;
using BlastCircuit.Arena.Stages;
using BlastCircuit.Arena.Effects;
namespace BlastCircuit.UI.Menu;

/// <summary>A lit miniature theatre behind the actual menu controls.</summary>
public partial class ArcadeBackdrop : RenderSurface
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
    private Node3D _world=null!,_green=null!,_clouds=null!,_cabinet=null!,_arenaMount=null!,_resultsStage=null!,_resultsBoard=null!;
    private TempleStageView? _miniature;
    private readonly Dictionary<string,TempleStageView> _miniatures=[];
    private string _arenaId="";
    private Camera3D _camera=null!;
    private Godot.Environment _environment=null!;
    private Vector2 _parallax;
    private double _clock,_untilDraw;
    private bool _wasResults;
    private float _boardCenter,_boardHeight;
    private int _graphicsRevision=-1;
    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Ignore;
        _viewport=new SubViewport {OwnWorld3D=true,Size=new(1440,900),RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};SetViewport(_viewport);
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
        _arenaMount=new Node3D {Name="ArenaDiorama",Position=new(-4.3f,2.6f,.4f),Scale=Vector3.One*.86f,RotationDegrees=new(21,-16,0)};_world.AddChild(_arenaMount);
        Cylinder(_arenaMount,new(0,-.60f,0),4.7f,.3f,gold);
        Cylinder(_arenaMount,new(0,-.39f,0),4.55f,.15f,Surface("252c51",.35f,.18f));
        _clouds=new Node3D();_world.AddChild(_clouds);
        BuildResultsStage();
    }
    private void BuildResultsStage()
    {
        _resultsStage=new Node3D {Name="VictoryTheatre",Visible=false};_world.AddChild(_resultsStage);
        var gold=Surface("c9a257",.3f,.65f);var navy=Surface("25335f",.38f,.22f);
        var velvet=Surface("343259",.68f);var trim=Surface("e3c982",.3f,.55f);
        _resultsBoard=new Node3D();_resultsStage.AddChild(_resultsBoard);SetResultCount(8);
        // Stacked foreground steps and tall wings give the theatre real near/far planes.
        ChamferBox(_resultsStage,new(-5.1f,-.1f,1.0f),new(7.2f,.42f,5.7f),navy,.14f);
        ChamferBox(_resultsStage,new(-5.1f,.14f,1.0f),new(6.7f,.10f,5.2f),gold,.08f);
        foreach(float x in new[]{-10.7f,10.7f})
        {
            Cylinder(_resultsStage,new(x,1.55f,3.8f),.43f,4.0f,navy);
            Cylinder(_resultsStage,new(x,-.34f,3.8f),.65f,.25f,gold);
            Cylinder(_resultsStage,new(x,3.4f,3.8f),.57f,.20f,gold);
            Sphere(_resultsStage,new(x,3.75f,3.8f),Vector3.One*.58f,trim);
        }
        // The arch sits behind the winner rather than filling the view with a flat image.
        foreach(float radius in new[]{3.0f,3.22f})
        {
            var arch=new MeshInstance3D {Mesh=new TorusMesh {InnerRadius=radius-.055f,OuterRadius=radius+.055f,Rings=72,RingSegments=12},MaterialOverride=gold,Position=new(-5.15f,2.55f,-2.9f),RotationDegrees=new(90,0,0)};_resultsStage.AddChild(arch);
        }
        for(int i=0;i<9;i++)
        {
            float a=Mathf.Pi*.14f+i*Mathf.Pi*.09f;
            var glint=Box(_resultsStage,new(-5.15f+Mathf.Cos(a)*3.55f,2.55f+Mathf.Sin(a)*3.55f,-2.9f),new(.13f,.33f,.13f),trim);glint.RotationDegrees=new(0,0,-a*180/Mathf.Pi);
        }
        _resultsStage.AddChild(new OmniLight3D {Position=new(-5,3.7f,2),LightColor=new Color("ffdc8e"),LightEnergy=1.4f,OmniRange=7});
        _resultsStage.AddChild(new OmniLight3D {Position=new(4.4f,4,3),LightColor=new Color("e1c8ff"),LightEnergy=.65f,OmniRange=6});
    }
    public void SetResultCount(int count)
    {
        if(_resultsBoard==null)return;
        foreach(var child in _resultsBoard.GetChildren()){_resultsBoard.RemoveChild(child);child.QueueFree();}
        // The stage floor hides anything below y≈.5, so large fields get a capped board and denser rows.
        float height=Math.Min(6.2f,1.8f+.75f*Math.Clamp(count,3,8)),center=6.7f-height/2;
        _boardCenter=center;_boardHeight=height;
        var navy=Surface("25335f",.38f,.22f);var gold=Surface("c9a257",.3f,.65f);
        ChamferBox(_resultsBoard,new(4,center,.45f),new(11,height,.55f),navy,.16f);
        ChamferBox(_resultsBoard,new(4,center,.76f),new(10.78f,height-.21f,.13f),gold,.12f);
        ChamferBox(_resultsBoard,new(4,center,.85f),new(10.53f,height-.44f,.1f),Surface("343259",.68f),.10f);
        foreach(float x in new[]{-1.13f,9.13f})foreach(float y in new[]{6.33f,6.7f-height+.37f})Sphere(_resultsBoard,new(x,y,.97f),Vector3.One*.11f,Surface("e3c982",.3f,.55f));
    }
    /// <summary>The standings board's velvet face in this control's coordinates, inscribed within its perspective outline.</summary>
    public Rect2? ResultsBoardFace()
    {
        if(!Results||_camera==null||_viewport.Size.X<2||_boardHeight<=0)return null;
        float halfX=10.53f/2-.1f,halfY=(_boardHeight-.44f)/2-.1f;
        var scale=Size/new Vector2(_viewport.Size.X,_viewport.Size.Y);
        Vector2 Corner(float x,float y)=>_camera.UnprojectPosition(new Vector3(4+x,_boardCenter+y,.9f))*scale;
        Vector2 topLeft=Corner(-halfX,halfY),topRight=Corner(halfX,halfY),bottomLeft=Corner(-halfX,-halfY),bottomRight=Corner(halfX,-halfY);
        float left=Math.Max(topLeft.X,bottomLeft.X),right=Math.Min(topRight.X,bottomRight.X);
        float top=Math.Max(topLeft.Y,topRight.Y),bottom=Math.Min(bottomLeft.Y,bottomRight.Y);
        // The theatre floor's front lip sits nearer the camera than the board and can occlude its base.
        bottom=Math.Min(bottom,_camera.UnprojectPosition(new Vector3(4,.145f,3.55f)).Y*scale.Y);
        return right>left&&bottom>top?new Rect2(left,top,right-left,bottom-top):null;
    }
    public void SelectArena(string id)
    {
        if(_arenaId==id)return;
        if(_miniature!=null)
        {
            _miniature.Visible=false;
            _miniature.ProcessMode=ProcessModeEnum.Disabled;
        }
        if(!_miniatures.TryGetValue(id,out var miniature))
        {
            miniature=BuildMiniature(id);
            _miniatures.Add(id,miniature);
        }
        _miniature=miniature;_arenaId=id;
        miniature.Visible=true;miniature.ProcessMode=ProcessModeEnum.Inherit;
    }

    /// <summary>Retain and draw the real miniatures behind the loading overlay, in the current render format.</summary>
    public async Task PrepareArenas(IEnumerable<string> ids)
    {
        string selected=_arenaId;bool visible=_arenaMount.Visible;
        GraphicsQuality.ApplyViewport(_viewport);
        ApplyGraphics();
        try
        {
            _arenaMount.Visible=true;
            foreach(string id in ids)
            {
                // Yield before each procedural build so the loading screen can draw.
                await EffectWarmup.Draw(this);
                SelectArena(id);
                for(int frame=0;frame<3;frame++)
                {
                    _viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
                    await EffectWarmup.Draw(this);
                }
                using var pixels=_viewport.GetTexture().GetImage();
                if(pixels.IsEmpty())throw new InvalidOperationException("Arena preview did not render: "+id);
            }
        }
        finally
        {
            if(selected.Length>0)SelectArena(selected);
            _arenaMount.Visible=visible;
        }
    }

    private TempleStageView BuildMiniature(string id)
    {
        var layout=BlastStages.Get(id);
        var cells=new List<object>();
        for(int z=0;z<layout.Height;z++)for(int x=0;x<layout.Width;x++)
        {
            var c=layout.Cells[x,z];cells.Add(new {k=(int)c.Kind,t=c.Tier,oh=c.ObstacleHeight,rf=c.RampFromTier,rt=c.RampToTier,rx=c.RampDX,rz=c.RampDZ,below=c.Below is {} lower?new {k=(int)lower.Kind,t=lower.Tier,rf=lower.RampFromTier,rt=lower.RampToTier,rx=lower.RampDX,rz=lower.RampDZ}:null});
        }
        var data=JsonSerializer.SerializeToElement(new {id=layout.Id,width=layout.Width,height=layout.Height,coverHeight=layout.SoftBlockHeight,cells,underpasses=layout.Underpasses.Select(c=>c.Z*layout.Width+c.X).Order().ToArray()});
        float scale=8.1f/Math.Max(layout.Width,layout.Height);
        var miniature=new TempleStageView(new ArtMaterials()) {Scale=Vector3.One*scale,Position=new(-layout.Width*scale/2,0,-layout.Height*scale/2)};
        _arenaMount.AddChild(miniature);miniature.Build(data);
        if(id==BlastStages.BlizzardBattle)
            for(int z=1;z<layout.Height-1;z++)for(int x=1;x<layout.Width-1;x++)
                if(!layout.ClearCells.Contains((x,z)))miniature.AddChild(miniature.CreateCover(z*layout.Width+x));
        // Keep the authority terrain/materials, while trimming exterior scenery to the display plinth.
        foreach(var child in miniature.GetChildren())
        {
            if(child is Node3D scenery&&(child.Name=="GreedyExterior"||child.Name==TempleStageView.SceneryNodeName)){scenery.Visible=false;continue;}
            // The trimmed display plinth has no open sea, so it needs no seabed.
            if(child is MeshInstance3D seabed&&seabed.Name=="Seabed")seabed.Visible=false;
            if(child is MeshInstance3D {Mesh:PlaneMesh plane} water){plane.Size=new(layout.Width+.8f,layout.Height+.8f);water.Position=new(layout.Width/2f,-.53f,layout.Height/2f);}
            else if(child is MeshInstance3D {Mesh:BoxMesh grass} exterior&&(child.Name=="GardenExterior"||child.Name=="FrozenExterior"))
            {
                // ArtGeometry caches meshes used by gameplay too; resize a private menu copy.
                var displayGrass=(BoxMesh)grass.Duplicate();
                displayGrass.Size=new(layout.Width+.8f,grass.Size.Y,layout.Height+.8f);
                exterior.Mesh=displayGrass;
            }
            // Gameplay's deep sea foundation would project through the display plinth.
            else if(child is MeshInstance3D {Mesh:not null} terrain&&terrain.Mesh.GetAabb().Position.Y< -3.5f)terrain.Visible=false;
        }
        if(id==BlastStages.UpAndDown)miniature.FitUpAndDownMiniature(4.45f/scale,-.31f/scale);
        return miniature;
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
            // Godot's front faces are clockwise; keep the outward shading normal.
            foreach(var v in new[]{a,c,b}){builder.SetNormal(normal);builder.AddVertex(v);}
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
    private void ApplyGraphics()
    {
        if(_graphicsRevision==GraphicsQuality.Revision)return;
        _environment.SsaoEnabled=GraphicsQuality.Current==GraphicsPreset.High;
        _environment.SsaoRadius=.4f;_environment.SsaoIntensity=.8f;_environment.SsaoPower=1.2f;
        _graphicsRevision=GraphicsQuality.Revision;
    }
    public override void _Process(double delta)
    {
        base._Process(delta);
        if(!IsVisibleInTree())return;
        ApplyGraphics();
        _clock+=delta;_untilDraw+=delta;
        if(_untilDraw<GraphicsQuality.MenuFrameInterval)return;float dt=(float)Math.Min(_untilDraw,.1);_untilDraw=0;
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
        _arenaMount.Visible=Page is "Setup" or "Practice" or "HostSetup";
        foreach(var ornament in _cabinet.GetChildren().Skip(4).OfType<Node3D>())ornament.Visible=!_arenaMount.Visible;
        _green.Visible=Quiet&&!Results;_clouds.Visible=false;_cabinet.Visible=true;_resultsStage.Visible=Results;
        if(_wasResults!=Results){_wasResults=Results;_environment.BackgroundColor=new Color(Results?"181631":"121129");_environment.AmbientLightEnergy=Results?.44f:.48f;}
        var mouse=GetGlobalMousePosition()/GetViewportRect().Size-new Vector2(.5f,.5f);
        _parallax=_parallax.Lerp(mouse,1-Mathf.Exp(-dt*2));
        _camera.Position=new(_parallax.X*.32f,4.5f-_parallax.Y*.18f,18.5f);
        _camera.LookAt(new Vector3(0,1.6f,-1));
        _clouds.Position=new(Mathf.Sin((float)_clock*.035f)*.6f,0,0);
        _viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
    }
}

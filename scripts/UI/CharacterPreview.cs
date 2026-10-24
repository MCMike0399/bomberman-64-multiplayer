using Godot;
using BlastCircuit.Rendering;
namespace BlastCircuit.UI;

/// <summary>A live miniature world using the same articulated character as the arena.</summary>
public partial class CharacterPreview : RenderSurface
{
    private static readonly ArtMaterials Materials=new();
    private SubViewport _viewport=null!;
    private Node3D _world=null!;
    private Camera3D _camera=null!;
    private Godot.Environment _environment=null!;
    private int _graphicsRevision=-1;
    private BomberModel? _model;
    private int _appearance;
    private bool? _lastEnabled;
    private double _clock,_untilDraw;
    private float _untilCelebrate;
    public bool Celebrating {get;private set;}
    public bool Selected {get;set;}
    public bool EnabledSeat {get;set;}=true;
    public bool Platform {get;set;}=true;
    public bool ResultsPresentation {get;set;}
    public double AnimationTime=>_clock;
    public int Appearance=>_appearance;
    public float WalkPose=>_model?.WalkPose??0;
    public float BobHeight=>_model?.Position.Y??0;
    public void Celebrate(){Celebrating=true;_untilCelebrate=0;}
    public void SetAppearance(int appearance)
    {
        _appearance=Math.Clamp(appearance,0,7);
        if(_model!=null)_model.SetSuitColor(ArtMaterials.PlayerColors[_appearance]);
    }
    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Ignore;
        _viewport=new SubViewport {Size=new Vector2I(320,240),TransparentBg=true,OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};SetViewport(_viewport);
        _world=new Node3D();_viewport.AddChild(_world);
        // A small studio: warm key with soft contact shadows, cool fill, and a rim that separates the helmet
        // from the navy menus. A sky feeds reflections only, so glossy suits and gloves catch highlights
        // while the transparent background still shows the menu behind.
        var studio=new ProceduralSkyMaterial {SkyTopColor=new Color("8fa2e8"),SkyHorizonColor=new Color("fff0d2"),GroundHorizonColor=new Color("e6cf9c"),GroundBottomColor=new Color("3a3f6e"),SunAngleMax=20,UseDebanding=true};
        _environment=new Godot.Environment {BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("fff6dc"),Sky=new Sky {SkyMaterial=studio},
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("f6ebd2"),AmbientLightEnergy=.42f,ReflectedLightSource=Godot.Environment.ReflectionSource.Sky,
            TonemapMode=Godot.Environment.ToneMapper.Filmic,TonemapExposure=1.05f,AdjustmentEnabled=true,AdjustmentContrast=1.06f,AdjustmentSaturation=1.06f,
            SsaoRadius=.35f,SsaoIntensity=1.1f,SsaoPower=1.3f};
        _world.AddChild(new WorldEnvironment {Environment=_environment});
        _world.AddChild(new DirectionalLight3D {RotationDegrees=new(-38,-32,0),LightColor=new Color("fff0c8"),LightEnergy=1.45f,ShadowEnabled=true,ShadowBias=.03f,ShadowNormalBias=.6f,ShadowBlur=1.6f,DirectionalShadowMaxDistance=12,DirectionalShadowMode=DirectionalLight3D.ShadowMode.Orthogonal});
        _world.AddChild(new DirectionalLight3D {RotationDegrees=new(-18,145,0),LightColor=new Color("bcd2ff"),LightEnergy=.5f});
        _world.AddChild(new DirectionalLight3D {RotationDegrees=new(-28,172,0),LightColor=new Color("ffe2f1"),LightEnergy=.75f});
        var camera=_camera=new Camera3D {Position=new(0,1.52f,3.9f),Projection=Camera3D.ProjectionType.Orthogonal,Size=2.05f,Current=true};_world.AddChild(camera);camera.LookAt(new Vector3(0,.86f,0));
        if(Platform)ArtGeometry.Cylinder(_world,new Vector3(0,-.04f,0),.7f,.07f,Materials.Color(new Color("efc455")));
        if(ResultsPresentation)
        {
            // A turned medal dais shares the winner's lighting and contact shadows.
            var gold=ArcadeBackdrop.Surface("d8ad51",.28f,.7f);
            ArtGeometry.Cylinder(_world,new(0,-.24f,0),.92f,.42f,gold);
            ArtGeometry.Cylinder(_world,new(0,-.035f,0),.88f,.08f,ArcadeBackdrop.Surface("edce84",.4f,.45f));
            ArtGeometry.Cylinder(_world,new(0,-.47f,0),1.12f,.14f,ArcadeBackdrop.Surface("28345f",.32f,.35f));
            var number=new Label3D {Text="1",Position=new(0,-.25f,.925f),FontSize=110,PixelSize=.003f,Modulate=new Color("fff0bc"),OutlineModulate=new Color("775329"),OutlineSize=5};_world.AddChild(number);
            _camera.Position=new(.1f,1.45f,4.8f);_camera.LookAt(new Vector3(0,.68f,0));
        }
        RebuildModel();
    }
    private void RebuildModel()
    {
        if(_model!=null){_world.RemoveChild(_model);_model.QueueFree();}
        _model=new BomberModel(Materials,ArtMaterials.PlayerColors[_appearance],"");_world.AddChild(_model);_model.ShowName(false);_lastEnabled=null;
    }
    private static void DimModel(Node node,float opacity)
    {
        if(node is GeometryInstance3D geometry)geometry.Transparency=opacity;
        foreach(var child in node.GetChildren())DimModel(child,opacity);
    }
    public override void _Process(double delta)
    {
        base._Process(delta);
        if(!IsVisibleInTree()||_model==null)return;
        _camera.Size=ResultsPresentation?2.85f:Math.Max(2.05f,1.05f/Math.Max(.2f,Size.X/Math.Max(1,Size.Y)));
        if(_graphicsRevision!=GraphicsQuality.Revision)
        {
            // Contact shading reads on the close-up winner and lobby seats; Low keeps the cheaper look.
            _environment.SsaoEnabled=GraphicsQuality.Current!=GraphicsPreset.Low;_graphicsRevision=GraphicsQuality.Revision;
        }
        _clock+=delta;_untilDraw+=delta;if(_untilDraw<GraphicsQuality.MenuFrameInterval)return;
        float dt=(float)Math.Min(_untilDraw,.1);_untilDraw=0;
        if(Celebrating){_untilCelebrate-=dt;if(_untilCelebrate<=0){_model.React("victory");_untilCelebrate=1.6f;}}
        _model.Animate(dt,Selected&&EnabledSeat&&!Celebrating?dt*2.1f:0);
        _model.Position=new(0,Mathf.Sin((float)_clock*2.3f)*.025f,0);
        _model.Rotation=new(0,Selected?Mathf.Sin((float)_clock*.7f)*.12f:0,0);
        if(_lastEnabled!=EnabledSeat){DimModel(_model,EnabledSeat?0:.77f);_lastEnabled=EnabledSeat;}
        _viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
    }
}

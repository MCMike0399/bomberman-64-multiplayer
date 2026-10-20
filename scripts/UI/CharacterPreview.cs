using Godot;
using BlastCircuit.Rendering;
namespace BlastCircuit.UI;

/// <summary>A live miniature world using the same articulated character as the arena.</summary>
public partial class CharacterPreview : SubViewportContainer
{
    private static readonly ArtMaterials Materials=new();
    private SubViewport _viewport=null!;
    private Node3D _world=null!;
    private Camera3D _camera=null!;
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
        MouseFilter=MouseFilterEnum.Ignore;Stretch=true;
        _viewport=new SubViewport {Size=new Vector2I(320,240),TransparentBg=true,OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled,Msaa3D=Viewport.Msaa.Msaa2X};AddChild(_viewport);
        _world=new Node3D();_viewport.AddChild(_world);
        _world.AddChild(new WorldEnvironment {Environment=new Godot.Environment {BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("fff6dc"),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("fff0cd"),AmbientLightEnergy=.5f,TonemapMode=Godot.Environment.ToneMapper.Filmic}});
        _world.AddChild(new DirectionalLight3D {RotationDegrees=new(-35,-30,0),LightColor=new Color("fff2c9"),LightEnergy=1.3f,ShadowEnabled=ResultsPresentation});
        _world.AddChild(new DirectionalLight3D {RotationDegrees=new(-20,150,0),LightColor=new Color("c2d9ff"),LightEnergy=.45f});
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
        if(!IsVisibleInTree()||_model==null)return;
        _camera.Size=ResultsPresentation?2.85f:Math.Max(2.05f,1.05f/Math.Max(.2f,Size.X/Math.Max(1,Size.Y)));
        _clock+=delta;_untilDraw+=delta;if(_untilDraw<1d/30)return;
        float dt=(float)Math.Min(_untilDraw,.1);_untilDraw=0;
        if(Celebrating){_untilCelebrate-=dt;if(_untilCelebrate<=0){_model.React("victory");_untilCelebrate=1.6f;}}
        _model.Animate(dt,Selected&&EnabledSeat&&!Celebrating?dt*2.1f:0);
        _model.Position=new(0,Mathf.Sin((float)_clock*2.3f)*.025f,0);
        _model.Rotation=new(0,Selected?Mathf.Sin((float)_clock*.7f)*.12f:0,0);
        if(_lastEnabled!=EnabledSeat){DimModel(_model,EnabledSeat?0:.77f);_lastEnabled=EnabledSeat;}
        _viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
    }
}

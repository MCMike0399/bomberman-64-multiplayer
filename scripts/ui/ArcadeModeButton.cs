using Godot;
using BlastCircuit.UI.Menu;
namespace BlastCircuit.UI;

/// <summary>A real raised, lit menu plaque, with a native Button retaining focus and input semantics.</summary>
public partial class ArcadeModeButton : Button
{
    private SubViewport _viewport=null!;
    private Node3D _plaque=null!;
    private StandardMaterial3D _face=null!;
    private float _lift;
    private bool? _lastFocused;
    private bool _lastPressed;
    private string _lastText="";
    private float _settleTime;
    public int RenderRequests {get;private set;}
    private Label3D _title=null!;
    public Color FaceColor {get;set;}=new("99577f");
    public Color Accent {get;set;}=new("df9ac4");
    public override void _Ready()
    {
        MouseDefaultCursorShape=CursorShape.PointingHand;
        foreach(var state in new[]{"normal","hover","pressed","focus"})AddThemeStyleboxOverride(state,new StyleBoxEmpty());
        foreach(var state in new[]{"font_color","font_hover_color","font_pressed_color","font_focus_color","font_hover_pressed_color"})AddThemeColorOverride(state,new Color(0,0,0,0));
        var container=new RenderSurface();AddChild(container);container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _viewport=new SubViewport {OwnWorld3D=true,Size=new(680,150),TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};container.SetViewport(_viewport);
        var world=new Node3D();_viewport.AddChild(world);
        world.AddChild(new WorldEnvironment {Environment=new Godot.Environment {AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("a9b8e5"),AmbientLightEnergy=.45f,TonemapMode=Godot.Environment.ToneMapper.Filmic}});
        world.AddChild(new DirectionalLight3D {RotationDegrees=new(-42,-30,0),LightEnergy=1.8f,ShadowEnabled=true});
        world.AddChild(new DirectionalLight3D {RotationDegrees=new(-20,150,0),LightColor=new Color("9c9fff"),LightEnergy=.8f});
        var camera=new Camera3D {Position=new(.3f,1.25f,9),Projection=Camera3D.ProjectionType.Orthogonal,Size=1.8f,Current=true};world.AddChild(camera);camera.LookAt(Vector3.Zero);
        _plaque=new Node3D {RotationDegrees=new(0,-2,0)};world.AddChild(_plaque);
        // The narrow gold shoulder, dark sidewall and inset face are separate solid surfaces.
        ArcadeBackdrop.ChamferBox(_plaque,new(0,-.10f,-.18f),new(7.25f,1.20f,.32f),ArcadeBackdrop.Surface("0d1734",.32f,.35f));
        ArcadeBackdrop.ChamferBox(_plaque,new(0,0,0),new(7.25f,1.19f,.30f),ArcadeBackdrop.Surface("bda66d",.26f,.65f));
        _face=ArcadeBackdrop.Surface("344e88",.29f,.08f);_face.AlbedoColor=FaceColor;_face.EmissionEnabled=true;_face.Emission=Colors.Black;
        ArcadeBackdrop.ChamferBox(_plaque,new(0,.01f,.18f),new(7.08f,1.03f,.17f),_face);
        ArcadeBackdrop.ChamferBox(_plaque,new(-3.39f,.01f,.29f),new(.13f,.89f,.06f),new StandardMaterial3D {AlbedoColor=Accent,Roughness=.27f,Metallic=.2f},.02f);
        var title=_title=new Label3D {Text=Text,Font=ArcadeTheme.Display,Position=new(.08f,.0f,.30f),FontSize=52,PixelSize=.008f,OutlineSize=12,OutlineModulate=ArcadeTheme.Ink,Modulate=ArcadeTheme.Cream,NoDepthTest=false,Shaded=false};_plaque.AddChild(title);
    }
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree()||_plaque==null)return;
        bool focused=HasFocus()||IsHovered();
        if(_lastFocused!=focused||_lastPressed!=ButtonPressed||_lastText!=Text)
        {_lastFocused=focused;_lastPressed=ButtonPressed;_lastText=Text;_settleTime=0;}
        else if(_settleTime>=1)return;
        _settleTime+=(float)delta;
        _title.Text=Text;
        float target=ButtonPressed?-.08f:focused?.09f:0;
        _lift=Mathf.Lerp(_lift,target,1-Mathf.Exp(-(float)delta*14));
        _plaque.Position=new(0,_lift,_lift*1.8f);_plaque.RotationDegrees=new(focused?-2:0,-2,0);
        _face.AlbedoColor=_face.AlbedoColor.Lerp(focused?new Color("ffe27a"):FaceColor,1-Mathf.Exp(-(float)delta*12));
        // A little warm emission keeps the lit face gold instead of olive under the cool rim light.
        _face.Emission=_face.Emission.Lerp(focused?new Color("b07c1c"):Colors.Black,1-Mathf.Exp(-(float)delta*12));
        // Focus lights the face gold and flips the title to ink, matching the 2D keys.
        _title.OutlineSize=focused?0:12;
        _title.Modulate=focused?ArcadeTheme.Ink:ArcadeTheme.Cream;
        _viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
        RenderRequests++;
    }
}

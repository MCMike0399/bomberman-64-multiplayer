using BlastCircuit.Rendering;
using Godot;
using Bomber.Game.Simulation.Rules;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.UI;
using BlastCircuit.Arena.Stages;
namespace BlastCircuit.Arena.Effects;

/// <summary>Advisory arc for a controlled carrier. Static cover truncates it; combat can change its outcome.</summary>
public partial class BombThrowPreview : Node3D
{
    private readonly MeshInstance3D[] _dots=new MeshInstance3D[24];
    private readonly StandardMaterial3D _material=new(){ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,EmissionEnabled=true};
    private readonly MeshInstance3D _target;
    private string _endpointLabel="";
    private readonly ThrowEndpointReticle _reticle=new();
    private Vector3 _endpoint;
    private ThrowProjectionEnd _end;
    public bool EndpointMarkerVisible=>_reticle.IsVisibleInTree();
    public Vector2 EndpointScreenPosition=>_reticle.Position;
    public bool EndpointEdgeClamped=>_reticle.EdgeClamped;
    public bool LandingMarkerVisible=>_target.Visible;
    // Semantic diagnostics retained for existing contracts; no label is rendered.
    public string EndpointLabel=>_endpointLabel;
    public BombThrowPreview()
    {
        for(int i=0;i<_dots.Length;i++)_dots[i]=Sphere(this,Vector3.Zero,Vector3.One*.105f,_material);
        _target=new(){Mesh=PrimitiveTemplates.Get("TorusMesh",("inner_radius",.31f),("outer_radius",.39f),("rings",32),("ring_segments",6)),MaterialOverride=_material};AddChild(_target);
        Box(_target,Vector3.Zero,new(.18f,.014f,.035f),_material);
        Box(_target,Vector3.Zero,new(.035f,.014f,.18f),_material);
        var overlay=new CanvasLayer{Layer=2};AddChild(overlay);overlay.AddChild(_reticle);
    }
    public void Update(ThrowProjection projection,int pumps,int slot,bool aimSet=false,bool aiming=false,float range=0)
    {
        var color=pumps==3?ArcadeTheme.Gold:ArcadeTheme.Aqua.Lerp(ArcadeTheme.Gold,pumps/3f);
        _material.AlbedoColor=color;_material.Emission=color*.55f;
        int visible=Math.Min(_dots.Length,projection.Points.Count);
        for(int i=0;i<_dots.Length;i++)
        {
            _dots[i].Visible=i<visible;
            if(i>=visible)continue;
            int index=(int)MathF.Round((projection.Points.Count-1)*i/(float)Math.Max(1,visible-1));
            var p=projection.Points[index];_dots[i].Position=new(p.X,p.Y*TempleStageView.HeightScale,p.Z);
        }
        var last=projection.Last;
        _target.Position=new(last.X,last.Y*TempleStageView.HeightScale+.055f,last.Z);
        _target.Visible=projection.End==ThrowProjectionEnd.Landing;
        _endpoint=_target.Position;_end=projection.End;
        _target.Scale=Vector3.One*(aiming?1.08f:1);
        _endpointLabel=projection.End switch{
            ThrowProjectionEnd.Obstructed=>$"P{slot+1} · blocked landing",
            ThrowProjectionEnd.Fuse=>$"P{slot+1} · fuse ends here",
            ThrowProjectionEnd.Water=>$"P{slot+1} · sinks here",
            ThrowProjectionEnd.Void=>$"P{slot+1} · falls away here",
            _=>aimSet?$"P{slot+1} · {range:0.0} range · {pumps}/3":$"P{slot+1} · {pumps}/3 · aim"
        };
    }
    public void HidePreview(){Visible=false;_reticle.Visible=false;}
    public void UpdateScreen(Camera3D camera)=>_reticle.Update(camera,_endpoint,_end,_material.AlbedoColor,IsVisibleInTree());
}
public sealed record ThrowPreviewState(string PlayerId,int Pumps,float X,float Y,float Z,string End,int Points,bool LandingMarkerVisible,string EndpointLabel,bool AimSet=false,bool Aiming=false,float AimRange=0,bool EndpointMarkerVisible=false,float ScreenX=0,float ScreenY=0,bool EdgeClamped=false);

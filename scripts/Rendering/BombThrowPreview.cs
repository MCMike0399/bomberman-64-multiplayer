using Godot;
using Bomber.Game.Simulation;
using BlastCircuit.UI;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Rendering;

/// <summary>Advisory arc for a controlled carrier. Static cover truncates it; combat can change its outcome.</summary>
public partial class BombThrowPreview : Node3D
{
    private readonly MeshInstance3D[] _dots=new MeshInstance3D[24];
    private readonly StandardMaterial3D _material=new(){ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,EmissionEnabled=true};
    private readonly MeshInstance3D _target;
    private readonly Label3D _label;
    public bool LandingMarkerVisible=>_target.Visible;
    public string EndpointLabel=>_label.Text;
    public BombThrowPreview()
    {
        for(int i=0;i<_dots.Length;i++)_dots[i]=Sphere(this,Vector3.Zero,Vector3.One*.105f,_material);
        _target=new(){Mesh=new TorusMesh{InnerRadius=.31f,OuterRadius=.39f,Rings=32,RingSegments=6},MaterialOverride=_material};AddChild(_target);
        _label=new Label3D{Font=ArcadeTheme.Body,FontSize=32,PixelSize=.012f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineModulate=ArcadeTheme.Ink,OutlineSize=8,NoDepthTest=false};AddChild(_label);
    }
    public void Update(ThrowProjection projection,int pumps,int slot)
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
        _label.Position=_target.Position+Vector3.Up*.42f;
        _label.Modulate=color;
        _label.Text=projection.End switch{
            ThrowProjectionEnd.Obstructed=>$"P{slot+1} · arc guide stops here",
            ThrowProjectionEnd.Fuse=>$"P{slot+1} · fuse ends here",
            ThrowProjectionEnd.Water=>$"P{slot+1} · sinks here",
            ThrowProjectionEnd.Void=>$"P{slot+1} · falls away here",
            _=>$"P{slot+1} · {pumps}/3 · aim"
        };
    }
}
public sealed record ThrowPreviewState(string PlayerId,int Pumps,float X,float Y,float Z,string End,int Points,bool LandingMarkerVisible,string EndpointLabel);

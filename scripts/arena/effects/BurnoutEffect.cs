using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Arena.Models;
using BlastCircuit.Rendering;
namespace BlastCircuit.Arena.Effects;

/// <summary>
/// Blast elimination gag at the accepted death point: the charred player is flung up, lands
/// running on the spot wrapped in flames, stiffens and vanishes in a puff of smoke.
/// Presentation only; authority already removed or ghosted the real player.
/// </summary>
public partial class BurnoutEffect : Node3D
{
    public const float Duration=2.4f;
    private const int FlameCount=20,TrailCount=4,PuffCount=11;
    private static readonly SphereMesh BlobMesh=new() {Radius=.5f,Height=1,RadialSegments=12,Rings=6};
    // A rounded base drawn up into a point: a toy candle-flame tongue rather than a flat disc.
    private static readonly CylinderMesh TongueMesh=new() {TopRadius=0,BottomRadius=.5f,Height=1.1f,RadialSegments=10,Rings=3};
    private static StandardMaterial3D? _flameOuter,_flameCore,_aura,_trail;
    private readonly MeshInstance3D _auraShell;
    private readonly BomberModel _puppet;
    private readonly Node3D _mover=new();
    private readonly List<(MeshInstance3D Mesh,float Phase)> _flames=[];
    private readonly List<(MeshInstance3D Mesh,float Phase)> _trails=[];
    private readonly List<(MeshInstance3D Mesh,Vector3 Velocity)> _puffs=[];
    private readonly MeshInstance3D _scorch;
    private readonly float _yaw;
    private float _age;
    public float Age=>_age;
    public BomberModel Puppet=>_puppet;
    public BurnoutEffect(ArtMaterials art,Color suit,float yaw)
    {
        _yaw=yaw;
        _scorch=Cylinder(this,new(0,.025f,0),.5f,.006f,art.Color(new Color(.08f,.06f,.05f,.5f),.95f));
        _scorch.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(_mover);
        _puppet=new BomberModel(art,suit,"");_mover.AddChild(_puppet);_puppet.BeginBurnout();
        _puppet.Scale=Vector3.One*1.12f;
        _flameOuter??=Flame(new Color(1,.42f,.1f,.88f),1.4f);
        _flameCore??=Flame(new Color(1,.83f,.3f,.94f),1.8f);
        _aura??=Unshaded(new Color(1,.5f,.15f,.2f));
        _trail??=Unshaded(new Color(.22f,.19f,.18f,.5f));
        _auraShell=Blob(_aura);
        for(int i=0;i<FlameCount;i++)
        {
            var flame=Blob(i%3==0?_flameCore:_flameOuter);
            // A sphere base under each cone keeps the tongue round when seen from above.
            var tongue=new MeshInstance3D {Mesh=TongueMesh,MaterialOverride=flame.MaterialOverride,Position=new(0,.5f,0),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
            flame.AddChild(tongue);_flames.Add((flame,i*.37f%1));
        }
        for(int i=0;i<TrailCount;i++)_trails.Add((Blob(_trail),i/(float)TrailCount));
        var puff=art.Color(new Color(.97f,.95f,.91f,.92f),.95f);
        for(int i=0;i<PuffCount;i++)
        {
            float angle=i*Mathf.Tau/PuffCount;
            var node=Blob(puff);
            _puffs.Add((node,new(Mathf.Cos(angle)*(1.1f+i%3*.25f),.9f+(i%4)*.25f,Mathf.Sin(angle)*(1.1f+i%3*.25f))));
        }
        Advance(0);
    }
    private MeshInstance3D Blob(Material material)
    {
        var node=new MeshInstance3D {Mesh=BlobMesh,MaterialOverride=material,Visible=false,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
        AddChild(node);return node;
    }
    private static StandardMaterial3D Flame(Color color,float energy)=>new()
    {
        AlbedoColor=color,Roughness=.4f,EmissionEnabled=true,Emission=new Color(color.R,color.G,color.B),EmissionEnergyMultiplier=energy,
        RimEnabled=true,Rim=.6f,RimTint=.2f,
        Transparency=BaseMaterial3D.TransparencyEnum.Alpha,DepthDrawMode=BaseMaterial3D.DepthDrawModeEnum.Disabled
    };
    private static StandardMaterial3D Unshaded(Color color)=>new()
    {
        AlbedoColor=color,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency=BaseMaterial3D.TransparencyEnum.Alpha,DepthDrawMode=BaseMaterial3D.DepthDrawModeEnum.Disabled
    };
    public override void _Process(double delta)=>Advance(_age+(float)delta);
    private void Advance(float age)
    {
        _age=age;
        if(_age>Duration){QueueFree();return;}
        float flail=Mathf.Clamp((_age-.08f)/.12f,0,1)*(1-Mathf.SmoothStep(BomberModel.BurnoutFlailEnd-.12f,BomberModel.BurnoutFlailEnd,_age));
        // Flung up by the blast, then frantic little hops while darting around the spot.
        float launch=Mathf.Clamp((_age-.04f)/.56f,0,1);
        float height=_age<.6f?4*.85f*launch*(1-launch):Mathf.Abs(Mathf.Sin((_age-.6f)*Mathf.Pi*3.3f))*.16f*flail;
        _mover.Position=new(Mathf.Sin(_age*6.5f)*.26f*flail,height,Mathf.Sin(_age*9.3f+1)*.15f*flail);
        _mover.Rotation=new(0,_yaw+Mathf.Sin(_age*4.2f)*.75f*flail,0);
        _puppet.AnimateBurnout(_age);
        _puppet.Visible=_age<BomberModel.BurnoutCrumbleEnd;
        bool burning=_age>.06f&&_age<BomberModel.BurnoutFlailEnd+.1f&&_puppet.IsInsideTree();
        float fire=Mathf.Clamp((_age-.06f)/.1f,0,1)*(1-Mathf.SmoothStep(BomberModel.BurnoutFlailEnd-.2f,BomberModel.BurnoutFlailEnd+.1f,_age));
        for(int i=0;i<_flames.Count;i++)
        {
            var (mesh,phase)=_flames[i];
            // Each tongue licks up from a body part, shrinks and is reborn at the next one.
            float life=Mathf.PosMod(_age*3.1f+phase,1);int anchor=(i+(int)(_age*3.1f+phase))%6;
            mesh.Visible=burning&&fire>.02f;
            if(!mesh.Visible)continue;
            var origin=ToLocal(_puppet.BurnAnchor(anchor));
            // Lean each tongue outward from the body so the fire wraps the silhouette.
            var belly=ToLocal(_puppet.BurnAnchor(1));
            var outward=new Vector3(origin.X-belly.X,0,origin.Z-belly.Z);
            outward=outward.LengthSquared()<.0001f?new Vector3(Mathf.Cos(i*2.4f),0,Mathf.Sin(i*2.4f)):outward.Normalized();
            float wobble=Mathf.Sin(_age*23+i*1.7f);
            mesh.Position=origin+outward*(.12f+life*.1f)+new Vector3(wobble*.05f,life*.5f,Mathf.Cos(_age*19+i)*.05f);
            float size=(i%3==0?.26f:.38f)*Mathf.Sqrt(1-life)*fire*(1+wobble*.12f);
            mesh.Scale=Vector3.One*size;
            mesh.Rotation=new(outward.Z*.35f,0,-outward.X*.35f+wobble*.15f);
        }
        // A pulsing glow wraps the whole body, like the original's burning blob.
        _auraShell.Visible=burning&&fire>.02f;
        if(_auraShell.Visible)
        {
            _auraShell.Position=(ToLocal(_puppet.BurnAnchor(0))+ToLocal(_puppet.BurnAnchor(1)))*.5f;
            float pulse=1+Mathf.Sin(_age*21)*.06f;
            _auraShell.Scale=new Vector3(1.25f,1.55f,1.25f)*fire*pulse;
        }
        for(int i=0;i<_trails.Count;i++)
        {
            var (mesh,phase)=_trails[i];
            float life=Mathf.PosMod(_age*1.6f+phase,1);
            mesh.Visible=burning&&_age>.25f;
            if(!mesh.Visible)continue;
            mesh.Position=ToLocal(_puppet.BurnAnchor(0))+new Vector3(Mathf.Sin(i*2.1f)*.1f,.2f+life*.9f,0);
            mesh.Scale=Vector3.One*(.14f+life*.24f)*fire;
            mesh.Transparency=life;
        }
        // The final poof hides the crumble, billowing out from the ash heap and fading.
        float poof=_age-(BomberModel.BurnoutFlailEnd+.06f);
        foreach(var (mesh,velocity) in _puffs)
        {
            mesh.Visible=poof>0;
            if(poof<=0)continue;
            float spread=1-Mathf.Exp(-poof*4.5f);
            mesh.Position=new Vector3(velocity.X*.55f*spread,.45f+velocity.Y*.55f*spread,velocity.Z*.55f*spread);
            mesh.Scale=Vector3.One*(.3f+.5f*spread);
            mesh.Transparency=Mathf.Clamp((poof-.25f)/.45f,0,1);
        }
        _scorch.Transparency=Mathf.Clamp(1-_age/.15f,0,1)+Mathf.Clamp((_age-1.9f)/.5f,0,1);
    }
}

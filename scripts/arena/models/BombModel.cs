using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Rendering;
namespace BlastCircuit.Arena.Models;

/// <summary>Charge, fuse and release animation driven by accepted authoritative bomb state.</summary>
public partial class BombModel : Node3D
{
    // Retain the warm shader variant after the temporary warmup models are freed.
    private static readonly StandardMaterial3D StreakMaterial=new()
    {AlbedoColor=new Color(1,.94f,.75f,.5f),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,Transparency=BaseMaterial3D.TransparencyEnum.Alpha};
    public const float BaseVisualScale=1.22f;
    private readonly Node3D _shape=new(),_fuseAssembly=new();
    private readonly MeshInstance3D _shell,_spark;
    private readonly ArtMaterials _art;
    private int _pumps=-1;
    private float _size=BaseVisualScale,_targetSize=BaseVisualScale,_pulse,_fuse=2.5f,_clock;
    private bool _remote;
    private float _charge;
    private readonly Node3D _remoteIndicators=new();
    private bool _shot;
    private Vector3 _previous,_shotDirection=Vector3.Forward;
    private readonly Node3D _trail=new();
    public string? CarrierId {get;private set;}
    public int Charge=>Math.Max(0,_pumps);
    public float VisualScale=>_size;
    public BombModel(ArtMaterials art)
    {
        _art=art;AddChild(_shape);
        _shell=Sphere(_shape,new(0,.35f,0),Vector3.One*.7f,art.Toy(new("222536"),.32f));
        _shape.AddChild(_fuseAssembly);
        Cylinder(_fuseAssembly,new(0,.69f,0),.09f,.12f,art.Toy(new("ac8b3a"),.35f));
        var fuse=Cylinder(_fuseAssembly,new(.035f,.81f,0),.027f,.2f,art.Toy(new("e7c989")));fuse.RotationDegrees=new(0,0,-24);
        _spark=Sphere(_fuseAssembly,new(.076f,.91f,0),Vector3.One*.1f,art.Toy(new("ffcf51"),.3f,true));
        _shape.AddChild(_remoteIndicators);_remoteIndicators.Visible=false;
        var indicator=art.Toy(new("a9ed69"),.35f,true);
        for(int i=0;i<4;i++)
        {
            float angle=i*Mathf.Pi*.5f;
            Sphere(_remoteIndicators,new(Mathf.Sin(angle)*.342f,.38f,Mathf.Cos(angle)*.342f),Vector3.One*.085f,indicator);
        }
        AddChild(_trail);_trail.Visible=false;
        for(int i=0;i<3;i++)
        {
            var streak=Box(_trail,new((i-1)*.16f,.18f,-.75f-i*.1f),new(.035f,.025f,.45f+i*.15f),StreakMaterial);
            streak.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }
    public bool UpdateState(int pumps,int level,bool red,bool remote,float fuse,string? carrier,int motion=0,string mode="normal",float charge=0)
    {
        bool pumped=_pumps>=0&&pumps>_pumps;
        if(pumped)_pulse=1;
        _pumps=pumps;_remote=remote;_fuse=fuse;CarrierId=carrier;
        _charge=charge;_fuseAssembly.Visible=!remote;
        _shot=motion==1&&carrier is null;_remoteIndicators.Visible=remote;
        _targetSize=Math.Min(2.3f,BaseVisualScale+Math.Max(0,level-1)*.04f+pumps*.32f);
        _shell.MaterialOverride=_art.Toy(remote?new("2478e8"):red?new("d82e43"):mode=="bounce"?new("a365ea"):new("222536"),.32f);
        return pumped;
    }
    public override void _Process(double delta)
    {
        float dt=(float)delta;_clock+=dt;_pulse=Mathf.MoveToward(_pulse,0,dt*3.8f);
        var travel=Position-_previous;travel.Y=0;_previous=Position;
        if(travel.LengthSquared()>.0001f)_shotDirection=travel.Normalized();
        _trail.Visible=_shot;
        _trail.Rotation=new(0,Mathf.Atan2(_shotDirection.X,_shotDirection.Z),0);
        _size=Mathf.Lerp(_size,_targetSize,1-Mathf.Exp(-13*dt));
        float spring=Mathf.Sin(_pulse*Mathf.Pi)*.16f;
        float tension=CarrierId!=null?.018f+_charge*.06f:Mathf.Clamp(1-_fuse/2.5f,0,1)*.035f;
        float breathing=1+Mathf.Sin(_clock*(CarrierId==null?16:5))*tension;
        _shape.Scale=new Vector3(1+spring,1-spring*.65f,1+spring)*_size*breathing;
        _spark.Visible=!_remote;
        _spark.Scale=Vector3.One*.1f*(.8f+Mathf.Sin(_clock*30)*.22f);
    }
}

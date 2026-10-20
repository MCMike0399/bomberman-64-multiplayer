using Godot;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Rendering;

/// <summary>Charge, fuse and release animation driven by accepted authoritative bomb state.</summary>
public partial class BombModel : Node3D
{
    private readonly Node3D _shape=new();
    private readonly MeshInstance3D _shell,_spark;
    private readonly ArtMaterials _art;
    private int _pumps=-1;
    private float _size=1,_targetSize=1,_pulse,_fuse=2.5f,_clock;
    private bool _remote;
    private bool _shot;
    private Vector3 _previous,_shotDirection=Vector3.Forward;
    private readonly Node3D _trail=new();
    public string? CarrierId {get;private set;}
    public int Charge=>Math.Max(0,_pumps);
    public float VisualScale=>_size;
    public BombModel(ArtMaterials art)
    {
        _art=art;AddChild(_shape);
        _shell=Sphere(_shape,new(0,.35f,0),Vector3.One*.7f,art.Color(new("222536"),.2f));
        Cylinder(_shape,new(0,.69f,0),.09f,.12f,art.Color(new("ac8b3a"),.35f));
        var fuse=Cylinder(_shape,new(.035f,.81f,0),.027f,.2f,art.Color(new("e7c989")));fuse.RotationDegrees=new(0,0,-24);
        _spark=Sphere(_shape,new(.076f,.91f,0),Vector3.One*.1f,art.Color(new("ffcf51"),.3f,true));
        AddChild(_trail);_trail.Visible=false;
        var streakMaterial=new StandardMaterial3D {AlbedoColor=new Color(1,.94f,.75f,.5f),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,Transparency=BaseMaterial3D.TransparencyEnum.Alpha};
        for(int i=0;i<3;i++)
        {
            var streak=Box(_trail,new((i-1)*.16f,.18f,-.75f-i*.1f),new(.035f,.025f,.45f+i*.15f),streakMaterial);
            streak.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }
    public bool UpdateState(int pumps,int level,bool red,bool remote,float fuse,string? carrier,int motion=0)
    {
        bool pumped=_pumps>=0&&pumps>_pumps;
        if(pumped)_pulse=1;
        _pumps=pumps;_remote=remote;_fuse=fuse;CarrierId=carrier;
        _shot=motion==1&&carrier is null;
        _targetSize=1+Math.Max(0,level-1)*.08f+pumps*.48f;
        _shell.MaterialOverride=_art.Color(red?new("d82e43"):new("222536"),.2f);
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
        float tension=CarrierId!=null?.018f:Mathf.Clamp(1-_fuse/2.5f,0,1)*.035f;
        float breathing=1+Mathf.Sin(_clock*(CarrierId==null?16:5))*tension;
        _shape.Scale=new Vector3(1+spring,1-spring*.65f,1+spring)*_size*breathing;
        _spark.Visible=!_remote;
        _spark.Scale=Vector3.One*.1f*(.8f+Mathf.Sin(_clock*30)*.22f);
    }
}

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
    }
    public bool UpdateState(int pumps,int level,bool red,bool remote,float fuse,string? carrier)
    {
        bool pumped=_pumps>=0&&pumps>_pumps;
        if(pumped)_pulse=1;
        _pumps=pumps;_remote=remote;_fuse=fuse;CarrierId=carrier;
        _targetSize=1+Math.Max(0,level-1)*.08f+pumps*.48f;
        _shell.MaterialOverride=_art.Color(red?new("d82e43"):new("222536"),.2f);
        return pumped;
    }
    public override void _Process(double delta)
    {
        float dt=(float)delta;_clock+=dt;_pulse=Mathf.MoveToward(_pulse,0,dt*3.8f);
        _size=Mathf.Lerp(_size,_targetSize,1-Mathf.Exp(-13*dt));
        float spring=Mathf.Sin(_pulse*Mathf.Pi)*.16f;
        float tension=CarrierId!=null?.018f:Mathf.Clamp(1-_fuse/2.5f,0,1)*.035f;
        float breathing=1+Mathf.Sin(_clock*(CarrierId==null?16:5))*tension;
        _shape.Scale=new Vector3(1+spring,1-spring*.65f,1+spring)*_size*breathing;
        _spark.Visible=!_remote;
        _spark.Scale=Vector3.One*.1f*(.8f+Mathf.Sin(_clock*30)*.22f);
    }
}

using Godot;
using BlastCircuit.Rendering;
namespace BlastCircuit.Arena.Effects;

/// <summary>Bounded, deterministic visual snow; wind comes from authority, particles never affect gameplay.</summary>
public partial class BlizzardWeather : Node3D
{
    public int Width {get;set;}=19;
    public int Depth {get;set;}=19;
    public Vector2 Wind {get;set;}
    public bool Active {get;set;}=true;
    public float Clock {get;private set;}
    public int VisibleFlakes=>_flakes?.VisibleInstanceCount??0;
    private MultiMesh _flakes=null!;
    private readonly Vector3[] _positions=new Vector3[240];
    private readonly float[] _transforms=new float[240*12];
    private int _visibleCount=-1;
    public override void _Ready()
    {
        var mesh=PrimitiveTemplates.Get("SphereMesh",("radius",1),("height",2),("radial_segments",8),("rings",3));
        _flakes=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=mesh,InstanceCount=_positions.Length};
        var snow=new StandardMaterial3D{AlbedoColor=new Color("e7f4ff"),Roughness=.88f,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
        AddChild(new MultiMeshInstance3D{Name="SnowFlurries",Multimesh=_flakes,MaterialOverride=snow,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        for(int i=0;i<_positions.Length;i++)_positions[i]=new(.6f+(i*.618034f%1)*(Width-1.2f),.2f+(i*.381966f%1)*5,.6f+(i*.414214f%1)*(Depth-1.2f));
        UpdateFlakes(0);
    }
    public override void _Process(double delta)
    {
        if(!Active||!IsVisibleInTree())return;
        float dt=(float)Math.Min(delta,.1);Clock+=dt;UpdateFlakes(dt);
    }
    private void UpdateFlakes(float dt)
    {
        int count=GraphicsQuality.Current switch{GraphicsPreset.Low=>72,GraphicsPreset.Medium=>152,_=>240};
        if(_visibleCount!=count){_flakes.VisibleInstanceCount=count;_visibleCount=count;}
        for(int i=0;i<count;i++)
        {
            var p=_positions[i];
            p+=new Vector3(Wind.X*1.8f+PresentationMath.Sin(Clock*.8f+i)*.22f,-.3f-i%5*.06f,Wind.Y*1.8f)*dt;
            p.X=Mathf.PosMod(p.X-.5f,Width-1)+.5f;p.Z=Mathf.PosMod(p.Z-.5f,Depth-1)+.5f;
            p.Y=Mathf.PosMod(p.Y-.18f,5)+.18f;_positions[i]=p;
            float size=i%19==0?.075f:.018f+i%4*.007f;
            // MultiMesh's 3D buffer stores three rows: basis XYZ, then origin.
            // All off-diagonal terms stay zero; hidden particles keep their last state.
            int at=i*12;
            _transforms[at]=_transforms[at+5]=_transforms[at+10]=size;
            _transforms[at+3]=p.X;_transforms[at+7]=p.Y;_transforms[at+11]=p.Z;
        }
        _flakes.Buffer=_transforms;
    }
}

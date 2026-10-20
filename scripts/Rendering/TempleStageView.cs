using Godot;
using System.Text.Json;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit.Rendering;

/// <summary>Builds terrain directly from the authority's cells, including visible solid risers and stairways.</summary>
public partial class TempleStageView : Node3D
{
    public const float HeightScale=1.35f;
    private readonly ArtMaterials _art;
    private JsonElement _cells;
    public int Width {get;private set;}
    public int Depth {get;private set;}
    private Material _stone=null!,_cobble=null!;
    private MeshInstance3D _water=null!;
    private float _targetWater=-.53f;
    private bool _waterInitialized;
    public float WaterHeight=>_water?.Position.Y??-.53f;
    public void SetWaterLevel(float tierLevel)
    {
        _targetWater=tierLevel*HeightScale;
        if(!_waterInitialized&&_water!=null){_water.Position=new(_water.Position.X,_targetWater,_water.Position.Z);_waterInitialized=true;}
    }
    public override void _Process(double delta)
    {
        if(_water==null)return;
        var p=_water.Position;p.Y=Mathf.Lerp(p.Y,_targetWater,1-Mathf.Exp(-7*(float)delta));_water.Position=p;
        if(_water.MaterialOverride is ShaderMaterial water)water.SetShaderParameter("flood_amount",Mathf.Clamp((_targetWater+.53f)/(4*HeightScale),0,1));
    }
    public TempleStageView(ArtMaterials art){_art=art;}
    public float Surface(float x,float z)
    {
        int cx=Math.Clamp((int)x,0,Width-1),cz=Math.Clamp((int)z,0,Depth-1);
        var c=_cells[cz*Width+cx];float tier=c.GetProperty("t").GetSingle();
        if(c.GetProperty("k").GetInt32()==4)
        {
            float dx=c.GetProperty("rx").GetSingle(),dz=c.GetProperty("rz").GetSingle();
            float progress=dx!=0?(dx>0?x-cx:1-(x-cx)):(dz>0?z-cz:1-(z-cz));
            tier=c.GetProperty("rf").GetSingle()+progress*(c.GetProperty("rt").GetSingle()-c.GetProperty("rf").GetSingle());
        }
        return tier*HeightScale;
    }
    public void Build(JsonElement data)
    {
        Width=data.GetProperty("width").GetInt32();Depth=data.GetProperty("height").GetInt32();_cells=data.GetProperty("cells").Clone();
        _stone=_art.Stone();_cobble=_art.Stone(true);
        var wall=_art.Color(new("aa8850"));var cap=_art.Color(new("e7c987"));
        _water=new MeshInstance3D {Mesh=new PlaneMesh {Size=new Vector2(160,160),SubdivideWidth=80,SubdivideDepth=80},MaterialOverride=_art.Water(),Position=new Vector3(Width/2f,-.53f,Depth/2f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_water);
        bool temple=data.GetProperty("id").GetString()=="sunken-temple";
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)
        {
            var c=_cells[z*Width+x];int kind=c.GetProperty("k").GetInt32();if(kind is 2 or 3)continue;
            float tier=c.GetProperty("t").GetSingle(),y=tier*HeightScale;
            bool courtyard=temple && tier==0;
            float floorBottom=-1.8f;
            Box(this,new(x+.5f,(y-.15f+floorBottom)/2,z+.5f),new(.998f,y-.15f-floorBottom, .998f),wall);
            Box(this,new(x+.5f,y-.085f,z+.5f),new(.985f,.17f,.985f),courtyard?_cobble:_stone,.025f);
            if(kind==4)
            {
                float from=c.GetProperty("rf").GetSingle()*HeightScale,to=c.GetProperty("rt").GetSingle()*HeightScale;
                int dx=c.GetProperty("rx").GetInt32(),dz=c.GetProperty("rz").GetInt32();
                for(int step=0;step<6;step++)
                {
                    float h=(to-from)*(step+1)/6f;
                    var point=new Vector3(x+.5f+dx*((step+.5f)/6-.5f),from+h/2,z+.5f+dz*((step+.5f)/6-.5f));
                    Box(this,point,new(dx==0?.99f:1f/6,h,dz==0?.99f:1f/6),cap,.012f);
                }
            }
            if(kind==1)
            {
                if(temple && z==2 && x>=9 && x<=11)continue;
                Pillar(new(x+.5f,y,z+.5f));
            }
            if(!courtyard && kind!=4)
            {
                // Decorate only exposed risers: no invented walls across walkable connections.
                foreach(var (dx,dz) in new[]{(0,1),(0,-1),(1,0),(-1,0)})
                {
                    int nx=x+dx,nz=z+dz;
                    float neighbor=-1;
                    if(nx>=0&&nz>=0&&nx<Width&&nz<Depth)
                    {
                        var n=_cells[nz*Width+nx];int nk=n.GetProperty("k").GetInt32();
                        if(nk is not (2 or 3))neighbor=n.GetProperty("t").GetSingle();
                        if(nk==4)neighbor=n.GetProperty("rt").GetSingle();
                    }
                    if(tier-neighbor>=.9f)Relief(new(x+.5f+dx*.505f,y-.57f,z+.5f+dz*.505f),dx,dz);
                }
            }
        }
        if(temple)Shrine();
    }
    private void Relief(Vector3 point,int dx,int dz)
    {
        var root=new Node3D {Position=point,Rotation=new Vector3(0,Mathf.Atan2(dx,dz),0)};AddChild(root);
        var dark=_art.Color(new("795d36"));var light=_art.Color(new("dabb74"));
        Box(root,new(0,.29f,.012f),new(.94f,.075f,.025f),light);
        Box(root,new(0,-.29f,.012f),new(.94f,.075f,.025f),light);
        Box(root,new(0,0,.005f),new(.62f,.36f,.018f),dark,.015f);
        Box(root,new(0,0,.022f),new(.49f,.26f,.025f),light,.015f);
        Box(root,new(0,0,.04f),new(.3f,.12f,.02f),dark,.01f);
        Box(root,new(.16f,-.05f,.052f),new(.07f,.23f,.02f),light);
    }
    private void Pillar(Vector3 p)
    {
        var stone=_art.Color(new("c6a563"));var highlight=_art.Color(new("f0d99c"));
        Box(this,p+new Vector3(0,.095f,0),new(.94f,.19f,.94f),highlight,.055f);
        Box(this,p+new Vector3(0,.6f,0),new(.67f,.96f,.67f),stone,.055f);
        Box(this,p+new Vector3(0,1.13f,0),new(.9f,.16f,.9f),highlight,.045f);
        Box(this,p+new Vector3(0,.65f,.345f),new(.26f,.5f,.025f),_art.Color(new("856432")),.015f);
    }
    private void Shrine()
    {
        float y=4*HeightScale;
        var sandstone=_art.Color(new("d5af60"));var gold=_art.Color(new("f2d77f"));var dark=_art.Color(new("6d4f34"));
        Box(this,new(10.5f,y+.48f,2.5f),new(2.8f,.96f,.82f),sandstone,.12f);
        Box(this,new(10.5f,y+.98f,2.5f),new(3.02f,.13f,.96f),gold,.035f);
        Box(this,new(10.5f,y+.58f,2.932f),new(1.8f,.58f,.05f),dark,.05f);
        Box(this,new(10.5f,y+.58f,2.97f),new(1.55f,.42f,.045f),_art.Color(new("e88699"),.44f),.04f);
        for(int i=-1;i<=1;i++)Box(this,new(10.5f+i*.15f,y+.58f,3.003f),new(.045f,.28f,.025f),dark);
        foreach(float x in new[]{9.14f,11.86f})
        {
            Box(this,new(x,y+.61f,2.9f),new(.2f,1.05f,.23f),gold,.035f);
            Sphere(this,new(x,y+1.22f,2.9f),new(.23f,.23f,.23f),gold);
        }
    }
    public Node3D CreateCover(int cell)
    {
        int x=cell%Width,z=cell/Width;var root=new Node3D {Position=new(x+.5f,Surface(x+.5f,z+.5f),z+.5f)};
        var sandstone=_art.Color(new("caaa73"));var dark=_art.Color(new("8d6740"));var edge=_art.Color(new("e3c48c"));
        Box(root,new(0,.39f,0),new(.76f,.78f,.76f),sandstone,.06f);
        Box(root,new(0,.045f,0),new(.82f,.09f,.82f),edge,.025f);
        Box(root,new(0,.77f,0),new(.82f,.09f,.82f),edge,.025f);
        Box(root,new(0,.39f,.383f),new(.41f,.39f,.025f),dark,.04f);
        var inset=Box(root,new(0,.39f,.407f),new(.23f,.23f,.028f),edge,.025f);inset.RotationDegrees=new(0,0,45);
        return root;
    }
}

using Godot;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using System.Text.Json;
using static BlastCircuit.Rendering.ArtGeometry;
using BlastCircuit.Rendering;
using BlastCircuit.Arena.Effects;
namespace BlastCircuit.Arena.Stages;

/// <summary>Builds terrain directly from the authority's cells, including visible solid risers and stairways.</summary>
public partial class TempleStageView : Node3D
{
    public bool WeatherActive {get;set;}=true;
    public const float HeightScale=1.35f;
    /// <summary>Must match the water shader's seabed_height default.</summary>
    public const float SeabedHeight=-3f;
    private readonly ArtMaterials _art;
    private JsonElement _cells;
    private readonly HashSet<int> _underpasses = [];
    private bool _castle;
    private bool _blizzard;
    private BlizzardWeather? _blizzardWeather;
    private float _blizzardClock;
    private Vector2 _blizzardWind;
    private readonly List<ShaderMaterial> _frozenMaterials=[];
    private ShaderMaterial _crystalMaterial=null!,_snowMaterial=null!;
    private ArrayMesh[] _iceCones=[];
    private readonly List<(Node3D Node,float Age)> _avalancheSwells=[];
    private bool _greedy;
    public const float GreedyMoatHeight=-1.28f;
    private Node3D? _greedyExterior,_greedyWaterwheel;
    private MeshInstance3D? _greedyMoat;
    private readonly List<ShaderMaterial> _greedyExteriorMaterials=[];
    private readonly List<ShaderMaterial> _greedyMaterials=[];
    private readonly List<Node3D> _greedyCrushers=[],_greedyGears=[];
    private readonly List<MeshInstance3D> _greedyOuterLeft=[],_greedyOuterRight=[];
    private int _greedyClosed;
    private int _greedyClosedRight;
    private bool _greedyWallsInitialized;
    private Vector3 _greedyLeftFrom,_greedyRightFrom,_greedyLeftTarget,_greedyRightTarget;
    private float _greedyWallAge=.12f;
    private float _greedyGearImpulse;
    private readonly List<ShaderMaterial> _castleStone = [];
    private static Shader? _castleFloorShader;
    private ShaderMaterial _castleWallMaterial=null!;
    private ShaderMaterial? _castleCoverMaterial;
    public ShaderMaterial CastleWallMaterial=>_castleWallMaterial;
    public StageLayout Layout {get;private set;} = null!;
    private bool _garden;
    private readonly List<ShaderMaterial> _gardenMaterials=[];
    private Mesh[] _gardenRocks=[];
    private Material _gardenRockMaterial=null!;
    public int Width {get;private set;}
    public int Depth {get;private set;}
    private ShaderMaterial _stone=null!,_cobble=null!,_masonry=null!,_bedrock=null!;
    private MeshInstance3D _water=null!;
    private float _targetWater=-.53f;
    private bool _waterInitialized;
    private float _uploadedWater=float.NaN,_uploadedFlood=float.NaN;
    private int _graphicsRevision=-1;
    public float WaterHeight=>_water?.Position.Y??-.53f;
    private StageLighting? _lighting;
    public void SetLighting(StageLighting lighting)
    {
        _lighting=lighting;
        if(_water?.MaterialOverride is ShaderMaterial material)lighting.ApplyWater(material);
        if(_greedyMoat?.MaterialOverride is ShaderMaterial moat)lighting.ApplyWater(moat);
    }
    public void SetWaterLevel(float tierLevel)
    {
        _targetWater=tierLevel*HeightScale;
        if(!_waterInitialized&&_water!=null){_water.Position=new(_water.Position.X,_targetWater,_water.Position.Z);_waterInitialized=true;}
    }
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree())return;
        AnimateScenery((float)delta);
        if(_blizzard){ProcessBlizzard((float)delta);return;}
        if(_greedy){ProcessGreedyTrap((float)delta);return;}
        if(_castle)
        {
            if(_graphicsRevision!=GraphicsQuality.Revision)
            {
                foreach(var material in _castleStone)material.SetShaderParameter("quality_detail",GraphicsQuality.Current==GraphicsPreset.Low?0:1);
                _graphicsRevision=GraphicsQuality.Revision;
            }
            return;
        }
        if(_garden)
        {
            if(_graphicsRevision!=GraphicsQuality.Revision)
            {
                foreach(var material in _gardenMaterials)material.SetShaderParameter("quality_detail",GraphicsQuality.Current==GraphicsPreset.Low?0:1);
                _graphicsRevision=GraphicsQuality.Revision;
            }
            return;
        }
        if(_water==null)return;
        if(_water.MaterialOverride is not ShaderMaterial water)
        {
            // A menu display may use a static opaque sea instead of refraction
            // of the surrounding theatre. Stone still follows quality changes.
            if(_graphicsRevision!=GraphicsQuality.Revision)
            {
                foreach(var material in new[]{_stone,_cobble,_masonry,_bedrock})
                    material.SetShaderParameter("quality_detail",GraphicsQuality.Current==GraphicsPreset.Low?0:1);
                _graphicsRevision=GraphicsQuality.Revision;
            }
            return;
        }
        if(_graphicsRevision!=GraphicsQuality.Revision)
        {
            float detail=GraphicsQuality.Current==GraphicsPreset.Low?0:1;
            var shader=ArtMaterials.WaterShaderFor(GraphicsQuality.Current);
            if(water.Shader!=shader)
            {
                // Swapping between opaque and clear water keeps every authored uniform.
                water.Shader=shader;_uploadedFlood=float.NaN;
                if(_lighting!=null)_lighting.ApplyWater(water);
            }
            foreach(var material in new[]{water,_stone,_cobble,_masonry,_bedrock})material.SetShaderParameter("quality_detail",detail);
            foreach(var material in _gardenMaterials)material.SetShaderParameter("quality_detail",detail);
            _graphicsRevision=GraphicsQuality.Revision;
        }
        var p=_water.Position;p.Y=Mathf.Lerp(p.Y,_targetWater,1-Mathf.Exp(-7*(float)delta));
        if(Math.Abs(p.Y-_targetWater)<.0001f)p.Y=_targetWater;
        if(_water.Position!=p)_water.Position=p;
        float flood=Mathf.Clamp((_targetWater+.53f)/(4*HeightScale),0,1);
        if(_uploadedFlood!=flood){water.SetShaderParameter("flood_amount",flood);_uploadedFlood=flood;}
        if(_uploadedWater!=p.Y)
        {
            _stone.SetShaderParameter("water_height",p.Y);_cobble.SetShaderParameter("water_height",p.Y);
            _masonry.SetShaderParameter("water_height",p.Y);_bedrock.SetShaderParameter("water_height",p.Y);
            _uploadedWater=p.Y;
        }
    }
    public TempleStageView(ArtMaterials art){_art=art;}
    public float Surface(float x,float z,float y=float.PositiveInfinity) => Layout.SurfaceAt(x,z,y)*HeightScale;
    public CellKind AimCellKind(float x,float z) => Layout.At((int)MathF.Floor(x),(int)MathF.Floor(z)).Kind;
    public float AimSurface(float x,float z) => AimCellKind(x,z) switch
    {
        CellKind.Water => _targetWater/HeightScale,
        CellKind.Void => -1,
        _ => Layout.SurfaceAt(x,z)
    };
    private static CellDef ReadCell(JsonElement cell) => new()
    {
        Kind=(CellKind)cell.GetProperty("k").GetInt32(), Tier=cell.GetProperty("t").GetInt32(),
        ObstacleHeight=cell.TryGetProperty("oh",out var obstacle)&&obstacle.ValueKind==JsonValueKind.Number?obstacle.GetSingle():null,
        RampDX=cell.GetProperty("rx").GetInt32(), RampDZ=cell.GetProperty("rz").GetInt32(),
        RampFromTier=cell.GetProperty("rf").GetSingle(), RampToTier=cell.GetProperty("rt").GetSingle(),
        Below=cell.TryGetProperty("below",out var lower)&&lower.ValueKind==JsonValueKind.Object ? ReadCell(lower) : null
    };
    public float AimObstacleHeight(int x,int z)
    {
        if(x<0||z<0||x>=Width||z>=Depth)return Bomber.Game.Simulation.Rules.BombThrowRules.PillarHeight;
        var cell=_cells[z*Width+x];
        return cell.TryGetProperty("oh",out var height)&&height.ValueKind==JsonValueKind.Number?height.GetSingle():Bomber.Game.Simulation.Rules.BombThrowRules.PillarHeight;
    }
    public bool AimHasCover(int x,int z,byte[] cover)
    {
        if(x<0||z<0||x>=Width||z>=Depth)return false;
        int index=z*Width+x;
        return index/8<cover.Length&&(cover[index/8]&(1<<(index%8)))!=0;
    }
    public void Build(JsonElement data)
    {
        BuildTerrain(data);
        BuildScenery();
    }
    private void BuildTerrain(JsonElement data)
    {
        Width=data.GetProperty("width").GetInt32();Depth=data.GetProperty("height").GetInt32();_cells=data.GetProperty("cells").Clone();
        var cells=new CellDef[Width,Depth];
        for(int z=0;z<Depth;z++)for(int x=0;x<Width;x++)cells[x,z]=ReadCell(_cells[z*Width+x]);
        Layout=new StageLayout {Id=data.GetProperty("id").GetString()!,Width=Width,Height=Depth,Cells=cells,
            SoftBlockHeight=data.TryGetProperty("coverHeight",out var coverHeight)?coverHeight.GetSingle():BombThrowRules.CoverHeight};
        _underpasses.Clear();foreach(var cell in Layout.Underpasses)_underpasses.Add(cell.Z*Width+cell.X);
        _blizzard=Layout.Id==BlastStages.BlizzardBattle;
        if(_blizzard){BuildBlizzard();return;}
        _castle=Layout.Id==BlastStages.TopRules;
        if(_castle){BuildTopRules();return;}
        _greedy=Layout.Id==BlastStages.GreedyTrap;
        if(_greedy){BuildGreedyTrap();return;}
        _stone=_art.Stone();_cobble=_art.Stone(true);
        _masonry=_art.Masonry();_bedrock=_art.Bedrock();var wall=_masonry;var cap=_art.Color(new("e8cc82"));
        _garden=data.GetProperty("id").GetString()=="rock-garden";
        if(_garden){BuildRockGarden();return;}
        _water=new MeshInstance3D {Mesh=new PlaneMesh {Size=new Vector2(160,160),SubdivideWidth=180,SubdivideDepth=180},MaterialOverride=_art.Water(),Position=new Vector3(Width/2f,-.53f,Depth/2f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_water);
        // Never visible (the water shader keeps open sea opaque above it). It gives depth of
        // field and the shoreline depth reconstruction a real surface instead of the sky.
        AddChild(new MeshInstance3D {Name="Seabed",Mesh=new BoxMesh {Size=new Vector3(160,.05f,160)},MaterialOverride=_art.Color(new("0f3a46"),.95f),
            Position=new Vector3(Width/2f,SeabedHeight,Depth/2f),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        if(Layout.Id==BlastStages.UpAndDown&&cells.Cast<CellDef>().Any(c=>c.Below!=null)){BuildUpAndDown();return;}
        bool temple=Layout.Id==BlastStages.SunkenTemple;
        bool garden=Layout.Id==BlastStages.CircuitGarden;
        if(garden){_masonry.SetShaderParameter("base_color",new Color("9da18a"));_masonry.SetShaderParameter("mortar_color",new Color("596750"));}
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
                    // The pale tread owns the top surface. A full-height support
                    // shares that plane and depth-fights it into dark moving patches.
                    const float treadThickness=.036f;
                    Box(this,point-Vector3.Up*treadThickness/2,new(dx==0?.99f:1f/6,h-treadThickness,dz==0?.99f:1f/6),wall);
                    Box(this,new(point.X,from+h-treadThickness/2,point.Z),new(dx==0?.99f:1f/6,treadThickness,dz==0?.99f:1f/6),cap,.006f);
                }
            }
            if(kind==1)
            {
                if(temple && z<=2)continue;
                Pillar(new(x+.5f,y,z+.5f));
            }
            if(!courtyard && kind!=4 && !garden)
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
        if(temple){Bedrock();Shrine();}
        if(data.GetProperty("id").GetString()=="circuit-garden")GardenDetails();
    }
    private void Bedrock()
    {
        // This foundation is visual only: no bodies, colliders or walkable terrain.
        // Its crown remains below the lowest playable court and slopes into the sea.
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        const int segments=20;
        Vector3 Point(int side,int step,float expansion,float y)
        {
            float t=(float)step/segments;
            float wobble=Mathf.Sin(step*2.13f+side*1.7f)*Mathf.Sin(t*Mathf.Pi)*.12f;
            float left=1-expansion-wobble,right=Width-1+expansion+wobble;
            float back=1-expansion-wobble,front=Depth-1+expansion+wobble;
            return side switch {0=>new(Mathf.Lerp(left,right,t),y,back),1=>new(right,y,Mathf.Lerp(back,front,t)),2=>new(Mathf.Lerp(right,left,t),y,front),_=>new(left,y,Mathf.Lerp(front,back,t))};
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            // SurfaceTool follows Godot's clockwise front-face convention.
            Vector3 normal=(c-a).Cross(b-a).Normalized();
            foreach(var p in new[]{a,b,c}){surface.SetNormal(normal);surface.SetUV(new(p.X,p.Z));surface.AddVertex(p);}
        }
        float[] levels=[-.24f,-.9f,-2.15f,-4.5f];
        float[] expansions=[-.02f,.32f,1.05f,2.1f];
        for(int layer=0;layer<levels.Length-1;layer++)for(int side=0;side<4;side++)for(int i=0;i<segments;i++)
        {
            var a=Point(side,i,expansions[layer],levels[layer]);
            var b=Point(side,i+1,expansions[layer],levels[layer]);
            var c=Point(side,i+1,expansions[layer+1],levels[layer+1]);
            var d=Point(side,i,expansions[layer+1],levels[layer+1]);
            Triangle(a,c,b);Triangle(a,d,c);
        }
        AddChild(new MeshInstance3D {Mesh=surface.Commit(),MaterialOverride=_bedrock});
    }
    private void Relief(Vector3 point,int dx,int dz)
    {
        var root=new Node3D {Position=point,Rotation=new Vector3(0,Mathf.Atan2(dx,dz),0)};AddChild(root);
        var dark=_art.Color(new("745321"));var light=_art.Color(new("d4ad55"));
        Box(root,new(0,.3f,.012f),new(.99f,.065f,.03f),light);
        Box(root,new(0,-.3f,.012f),new(.99f,.065f,.03f),light);
        Box(root,new(0,0,.006f),new(.88f,.4f,.018f),dark);
        // Squared spiral frieze, built from our own simple strokes rather than copied artwork.
        Box(root,new(-.36f,0,.023f),new(.055f,.34f,.025f),light);
        Box(root,new(0,.15f,.023f),new(.75f,.055f,.025f),light);
        Box(root,new(.35f,.01f,.023f),new(.055f,.32f,.025f),light);
        Box(root,new(.095f,-.12f,.023f),new(.56f,.055f,.025f),light);
        Box(root,new(-.16f,-.025f,.023f),new(.055f,.23f,.025f),light);
        Box(root,new(.02f,.06f,.023f),new(.39f,.055f,.025f),light);
        Box(root,new(.19f,.005f,.023f),new(.055f,.15f,.025f),light);
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
        // A shallow summit mosaic keeps every playable tile visible and unobstructed.
        float y=5*HeightScale+.012f;
        var gold=_art.Color(new("eac46a"));var dark=_art.Color(new("6d4f27"));
        Box(this,new(Width*.5f,y,1.95f),new(2.7f,.02f,1.7f),gold,.015f);
        Box(this,new(Width*.5f,y+.015f,1.95f),new(2.3f,.015f,1.3f),dark,.015f);
        for(int i=-1;i<=1;i++)Box(this,new(Width*.5f+i*.5f,y+.03f,1.95f),new(.2f,.015f,.8f),gold);
    }

    public Node3D CreateCover(int cell)
    {
        int x=cell%Width,z=cell/Width;
        if(_greedy)return CreateGreedyCover(x,z);
        if(_castle)return CreateCastleCover(x,z);
        var root=new Node3D {Position=new(x+.5f,Surface(x+.5f,z+.5f),z+.5f)};
        if(_garden)return CreateGardenBoulder(root,cell);
        if(_blizzard)return CreateIceCone(root,cell);
        if(Layout.Id==BlastStages.UpAndDown)return CreateUpAndDownCover(root,cell);
        var sandstone=_art.Color(new("caaa73"));var dark=_art.Color(new("8d6740"));var edge=_art.Color(new("e3c48c"));
        Box(root,new(0,.39f,0),new(.76f,.78f,.76f),sandstone,.06f);
        Box(root,new(0,.045f,0),new(.82f,.09f,.82f),edge,.025f);
        Box(root,new(0,.77f,0),new(.82f,.09f,.82f),edge,.025f);
        Box(root,new(0,.39f,.383f),new(.41f,.39f,.025f),dark,.04f);
        var inset=Box(root,new(0,.39f,.407f),new(.23f,.23f,.028f),edge,.025f);inset.RotationDegrees=new(0,0,45);
        return root;
    }
}

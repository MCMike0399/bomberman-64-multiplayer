using Godot;
using System.Text.Json;
using BlastCircuit.Rendering;
using static BlastCircuit.Rendering.ArtGeometry;
namespace BlastCircuit;

/// <summary>Snapshot-driven presentation. Geometry, movement authority, and UI never share mutable entities.</summary>
public partial class ArenaView : Node3D
{
    public static readonly Color[] Colors=ArtMaterials.PlayerColors;
    private readonly ArtMaterials _art=new();
    private readonly Node3D _dynamic=new(),_effects=new();
    private readonly Dictionary<string,Node3D> _entities=[];
    private readonly Dictionary<string,Vector3> _targets=[];
    private readonly Dictionary<int,Node3D> _blocks=[];
    private readonly Dictionary<string,float> _ages=[];
    private readonly Dictionary<int,Node3D> _walls=[];
    private TempleStageView? _terrain;
    private Camera3D _camera=null!;
    private BattleAudio _audio=null!;
    private string _stage="";
    private double _clock;
    private float _shake;
    public string StageId=>_stage;
    public int VisiblePlayerCount=>_entities.Values.Count(n=>n is BomberModel);

    public override void _Ready()
    {
        AddChild(_dynamic);AddChild(_effects);_audio=new BattleAudio();AddChild(_audio);
        var environment=new Godot.Environment
        {
            BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("7ed4e4"),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("d7e2e9"),AmbientLightEnergy=.45f,
            TonemapMode=Godot.Environment.ToneMapper.Filmic,ReflectedLightSource=Godot.Environment.ReflectionSource.Disabled,
            SsaoEnabled=true,SsaoRadius=1.4f,SsaoIntensity=1.5f,SsaoPower=1.3f,
            GlowEnabled=true,GlowIntensity=.25f,GlowBloom=.05f,
            FogEnabled=true,FogLightColor=new Color("89cbd8"),FogDensity=.0015f
        };
        AddChild(new WorldEnvironment {Environment=environment});
        var sun=new DirectionalLight3D {RotationDegrees=new(-52,-27,0),LightColor=new Color("fff0cb"),LightEnergy=1.05f,ShadowEnabled=true,
            DirectionalShadowMaxDistance=65,ShadowBias=.025f,ShadowNormalBias=1.2f};AddChild(sun);
        AddChild(new DirectionalLight3D {RotationDegrees=new(-25,135,0),LightColor=new Color("b8dcf0"),LightEnergy=.22f});
        _camera=new Camera3D {Projection=Camera3D.ProjectionType.Perspective,Fov=40,Near=.1f,Far=180,Current=true};AddChild(_camera);
        FrameCamera(21,23);
    }
    private void FrameCamera(int width,int depth)
    {
        var center=new Vector3(width/2f,1.6f,depth*.52f);
        float span=Mathf.Max(width,depth);
        _camera.Position=center+new Vector3(0,span*.88f,span*1.32f);
        _camera.LookAt(center);
    }
    public void Stage(JsonElement data)
    {
        string id=data.GetProperty("id").GetString()!;
        if(_stage==id)return;
        _stage=id;
        _terrain?.QueueFree();_terrain=new TempleStageView(_art);AddChild(_terrain);_terrain.Build(data);
        foreach(Node child in _dynamic.GetChildren())child.QueueFree();
        foreach(Node child in _effects.GetChildren())child.QueueFree();
        _entities.Clear();_targets.Clear();_blocks.Clear();_ages.Clear();_walls.Clear();
        FrameCamera(_terrain.Width,_terrain.Depth);
    }
    public void State(JsonElement data)
    {
        if(_terrain==null)return;
        byte[] bits=Convert.FromBase64String(data.GetProperty("softBlocks").GetString()!);
        for(int i=0;i<_terrain.Width*_terrain.Depth;i++)
        {
            bool exists=i/8<bits.Length&&(bits[i/8]&(1<<(i%8)))!=0;
            if(exists&&!_blocks.ContainsKey(i)){var block=_terrain.CreateCover(i);_dynamic.AddChild(block);_blocks[i]=block;}
            if(!exists&&_blocks.Remove(i,out var old)){Debris(old.Position);old.QueueFree();}
        }
        var seen=new HashSet<string>();
        foreach(var p in data.GetProperty("players").EnumerateArray())
        {
            string id="p:"+p.GetProperty("id").GetString();bool ghost=p.GetProperty("ghost").GetBoolean();
            if(!p.GetProperty("alive").GetBoolean()&&!ghost)continue;
            seen.Add(id);int slot=p.GetProperty("slot").GetInt32();
            if(!_entities.TryGetValue(id,out var node))
            {
                node=new BomberModel(_art,Colors[slot%Colors.Length],$"P{slot+1}");_dynamic.AddChild(node);_entities[id]=node;node.Position=PositionOf(p);
            }
            _targets[id]=PositionOf(p);
            float angle=Mathf.Atan2(F(p,"fx"),F(p,"fz"));node.Rotation=new Vector3(0,angle,0);
            ((BomberModel)node).Pose(p.GetProperty("carrying").ValueKind!=JsonValueKind.Null,p.GetProperty("stunned").GetBoolean(),ghost);
        }
        foreach(var b in data.GetProperty("bombs").EnumerateArray())
        {
            string id="b:"+b.GetProperty("id").GetString();seen.Add(id);
            if(!_entities.TryGetValue(id,out var node))
            {
                node=Bomb(b.GetProperty("red").GetBoolean());_dynamic.AddChild(node);_entities[id]=node;node.Position=PositionOf(b);_audio.Bomb();
            }
            _targets[id]=PositionOf(b);
            float size=1+F(b,"pumps")*.27f;node.Scale=Vector3.One*size*(1+.045f*Mathf.Sin((float)_clock*14));
        }
        foreach(var h in data.GetProperty("hazards").EnumerateArray())
        {
            string id="h:"+h.GetProperty("id").GetString();seen.Add(id);
            if(_entities.ContainsKey(id))continue;
            float y=h.TryGetProperty("y",out var yValue)?yValue.GetSingle()*TempleStageView.HeightScale:_terrain.Surface(F(h,"x"),F(h,"z"));
            var node=new Node3D {Position=new(F(h,"x"),y+.3f,F(h,"z"))};
            float radius=F(h,"r");
            Sphere(node,Vector3.Zero,new(radius*2,radius*2*TempleStageView.HeightScale,radius*2),_art.Color(new Color(1,.43f,.09f,.2f),.3f,true));
            Sphere(node,Vector3.Zero,Vector3.One*radius*1.1f,_art.Color(new Color(1,.88f,.42f,.72f),.3f,true));
            for(int i=0;i<8;i++)
            {
                float a=i*Mathf.Tau/8;
                Sphere(node,new(Mathf.Cos(a)*radius*.66f,.1f,Mathf.Sin(a)*radius*.66f),Vector3.One*radius*.35f,_art.Color(new Color(1,.66f,.12f,.45f),.3f,true));
            }
            _dynamic.AddChild(node);_entities[id]=node;_ages[id]=0;_shake=.09f;_audio.Blast();
        }
        foreach(var item in data.GetProperty("items").EnumerateArray())
        {
            string id="i:"+item.GetProperty("id").GetString();seen.Add(id);
            if(!_entities.TryGetValue(id,out var node))
            {
                float x=F(item,"cx")+.5f,z=F(item,"cz")+.5f;node=Pickup(item.GetProperty("type").GetString()??"");
                node.Position=new(x,_terrain.Surface(x,z)+.48f,z);_dynamic.AddChild(node);_entities[id]=node;
            }
            node.Rotation=new(0,(float)_clock*1.4f,0);
        }
        foreach(string id in _entities.Keys.Where(k=>!seen.Contains(k)).ToArray())
        {
            if(id.StartsWith("i:"))_audio.Pickup();_entities[id].QueueFree();_entities.Remove(id);_targets.Remove(id);_ages.Remove(id);
        }
        int left=(int)F(data,"closedLeft"),right=(int)F(data,"closedRight");
        for(int x=0;x<_terrain.Width;x++)
        {
            bool closed=x<left||x>=_terrain.Width-right;
            if(closed&&!_walls.ContainsKey(x))
            {
                var n=new Node3D();Box(n,new(x+.5f,2.1f,_terrain.Depth/2f),new(.92f,5.8f,_terrain.Depth),_art.Color(new Color(.92f,.18f,.17f,.4f),.7f,true),.03f);_effects.AddChild(n);_walls[x]=n;
            }
            if(!closed&&_walls.Remove(x,out var wall))wall.QueueFree();
        }
    }
    public override void _Process(double delta)
    {
        _clock+=delta;float dt=(float)delta;
        foreach(var (id,target) in _targets)
        {
            if(!_entities.TryGetValue(id,out var node))continue;
            Vector3 previous=node.Position;
            node.Position=previous.DistanceSquaredTo(target)>16?target:previous.Lerp(target,1-Mathf.Exp(-20*dt));
            if(node is BomberModel model)model.Animate(dt,previous.DistanceTo(node.Position));
        }
        foreach(string id in _ages.Keys.ToArray())
        {
            _ages[id]+=dt;
            if(_entities.TryGetValue(id,out var blast))blast.Scale=Vector3.One*(.85f+.15f*Mathf.Sin(Mathf.Min(1,_ages[id]*7)*Mathf.Pi/2));
        }
        _shake=Mathf.MoveToward(_shake,0,dt*.6f);_camera.HOffset=Mathf.Sin((float)_clock*75)*_shake;_camera.VOffset=Mathf.Cos((float)_clock*83)*_shake*.5f;
    }
    private Node3D Bomb(bool red)
    {
        var n=new Node3D();
        Sphere(n,new(0,.36f,0),Vector3.One*.69f,_art.Color(red?new("cf263e"):new("222536"),.22f));
        Cylinder(n,new(0,.69f,0),.09f,.12f,_art.Color(new("ac8b3a"),.35f));
        var fuse=Cylinder(n,new(.035f,.81f,0),.027f,.2f,_art.Color(new("e7c989")));fuse.RotationDegrees=new(0,0,-24);
        Sphere(n,new(.076f,.91f,0),Vector3.One*.1f,_art.Color(new("ffcf51"),.3f,true));
        return n;
    }
    private Node3D Pickup(string type)
    {
        int kind=type switch {"fire"=>0,"bomb"=>1,"remote"=>2,"red"=>3,"heart"=>4,_=>1};
        Color[] colors=[new("ff7840"),new("5884f1"),new("b585eb"),new("ed4162"),new("f9689f")];
        string[] marks=["F","B","R","P","+"];
        var n=new Node3D();Box(n,Vector3.Zero,Vector3.One*.46f,_art.Color(colors[Math.Clamp(kind,0,4)],.35f),.08f);
        n.AddChild(new Label3D {Text=marks[Math.Clamp(kind,0,4)],Position=new(0,0,.25f),FontSize=48,PixelSize=.008f,OutlineSize=8,Modulate=Godot.Colors.White});return n;
    }
    private void Debris(Vector3 position)
    {
        var root=new Node3D {Position=position};_effects.AddChild(root);
        var material=_art.Color(new("d5b472"));
        for(int i=0;i<6;i++)
        {
            float angle=i*Mathf.Tau/6;
            var piece=Box(root,new(0,.4f,0),Vector3.One*(.12f+i*.008f),material,.02f);
            var tween=piece.CreateTween().SetParallel();tween.TweenProperty(piece,"position",new Vector3(Mathf.Cos(angle)*.65f,.06f,Mathf.Sin(angle)*.65f),.35);tween.TweenProperty(piece,"rotation",new Vector3(i,2,i*.4f),.35);
        }
        var cleanup=root.CreateTween();cleanup.TweenInterval(.4);cleanup.TweenProperty(root,"scale",Vector3.Zero,.2);cleanup.TweenCallback(Callable.From(root.QueueFree));
    }
    private static float F(JsonElement e,string key)=>e.GetProperty(key).GetSingle();
    private static Vector3 PositionOf(JsonElement e)=>new(F(e,"x"),F(e,"y")*TempleStageView.HeightScale,F(e,"z"));
}

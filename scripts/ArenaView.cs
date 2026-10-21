using Godot;
using System.Text.Json;
using BlastCircuit.Rendering;
using Bomber.Client;
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
    private readonly Dictionary<string,SnapshotMotion> _playerMotion=[];
    private readonly Dictionary<string,float> _facings=[];
    private readonly Dictionary<string,string> _ghostHosts=[];
    private long _snapshotTick;
    private readonly Dictionary<int,Node3D> _blocks=[];
    private readonly Dictionary<string,ExplosionView> _explosions=[];
    private readonly Dictionary<string,int> _effectCounts=[];
    private readonly PresentationCursor _presentation=new();
    private readonly Dictionary<int,Node3D> _walls=[];
    private TempleStageView? _terrain;
    private Camera3D _camera=null!;
    private BattleCameraRig _cameraRig=null!;
    private BattleAudio _audio=null!;
    private string _stage="";
    private double _clock;
    private float _shake;
    private int _lastRound=-1;
    private bool _wasHurry;
    private readonly Dictionary<string,int> _pickups=[];
    public float WaterHeight=>_terrain?.WaterHeight??0;
    public float LargestBombScale=>_entities.Values.OfType<BombModel>().Select(b=>b.VisualScale).DefaultIfEmpty(0).Max();
    public int PumpAnimationCount=>_entities.Values.OfType<BomberModel>().Sum(p=>p.PumpAnimationCount);
    public float PumpIntensity=>_entities.Values.OfType<BomberModel>().Select(p=>p.PumpIntensity).DefaultIfEmpty(0).Max();
    public int CarriedBombs=>_entities.Values.OfType<BombModel>().Count(b=>b.CarrierId!=null);
    public int VisiblePowerups=>_entities.Values.Count(n=>n is PowerupModel);
    public string StageId=>_stage;
    public int VisiblePlayerCount=>_entities.Values.Count(n=>n is BomberModel);
    public BattleAudio Audio=>_audio;
    public object PresentationState=>new {sequence=_presentation.Sequence,audio=_audio.Counts,voicePlays=_audio.VoicePlayCount,droppedCues=_audio.DroppedCueCount,effects=_effectCounts,explosions=_explosions.Values.Where(GodotObject.IsInstanceValid).Select(v=>new {tier=v.Tier,radius=v.Radius,age=v.Age,phase=v.Phase,lobes=v.LobeCount,hazard=v.HazardActive,extent=v.VisualExtent}).ToArray()};
    public void ResetPresentationBaseline()=>_presentation.Reset();

    public override void _Ready()
    {
        AddChild(_dynamic);AddChild(_effects);_audio=new BattleAudio();AddChild(_audio);
        var environment=new Godot.Environment
        {
            BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("70afbd"),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("a4b6d4"),AmbientLightEnergy=.29f,
            TonemapMode=Godot.Environment.ToneMapper.Filmic,ReflectedLightSource=Godot.Environment.ReflectionSource.Disabled,
            SsaoEnabled=true,SsaoRadius=.7f,SsaoIntensity=1.65f,SsaoPower=1.25f,
            GlowEnabled=true,GlowIntensity=.17f,GlowBloom=0,
            AdjustmentEnabled=true,AdjustmentContrast=1.08f,AdjustmentSaturation=1.02f,
            FogEnabled=true,FogLightColor=new Color("78b3bf"),FogDensity=.0007f
        };
        AddChild(new WorldEnvironment {Environment=environment});
        var sun=new DirectionalLight3D {RotationDegrees=new(-46,-34,0),LightColor=new Color("ffe8c7"),LightEnergy=.92f,ShadowEnabled=true,
            DirectionalShadowMaxDistance=65,ShadowBias=.025f,ShadowNormalBias=.45f,LightAngularDistance=1.8f};AddChild(sun);
        AddChild(new DirectionalLight3D {RotationDegrees=new(-25,135,0),LightColor=new Color("9fbae5"),LightEnergy=.12f});
        _camera=new Camera3D {Projection=Camera3D.ProjectionType.Perspective,Fov=36,Near=.1f,Far=180,Current=true};AddChild(_camera);
        _cameraRig=new BattleCameraRig(_camera);
        GetViewport().SizeChanged+=_cameraRig.Fit;
        VisibilityChanged+=UpdateProcessing;
        UpdateProcessing();
    }
    private void UpdateProcessing(){ProcessMode=IsVisibleInTree()?ProcessModeEnum.Inherit:ProcessModeEnum.Disabled;if(!IsVisibleInTree())_audio?.StopAll();}
    public void ResetMatch()
    {
        _playerMotion.Clear();_facings.Clear();_ghostHosts.Clear();_snapshotTick=0;
        _audio.StopAll();
        foreach(Node child in _dynamic.GetChildren())child.QueueFree();
        foreach(Node child in _effects.GetChildren())child.QueueFree();
        _entities.Clear();_targets.Clear();_blocks.Clear();_explosions.Clear();_presentation.Reset();_walls.Clear();_pickups.Clear();_lastRound=-1;_wasHurry=false;
    }
    public void Stage(JsonElement data)
    {
        string id=data.GetProperty("id").GetString()!;
        if(_stage==id)return;
        _stage=id;
        _playerMotion.Clear();_facings.Clear();_ghostHosts.Clear();
        _terrain?.QueueFree();_terrain=new TempleStageView(_art);AddChild(_terrain);_terrain.Build(data);
        foreach(Node child in _dynamic.GetChildren())child.QueueFree();
        foreach(Node child in _effects.GetChildren())child.QueueFree();
        _entities.Clear();_targets.Clear();_blocks.Clear();_explosions.Clear();_presentation.Reset();_walls.Clear();_pickups.Clear();_lastRound=-1;_wasHurry=false;
        _cameraRig.SetStage(data);
    }
    public void State(JsonElement data,long tick=-1)
    {
        if(_terrain==null)return;
        _snapshotTick=tick<0?_snapshotTick+2:tick;
        int round=(int)F(data,"round");
        if(round!=_lastRound){_lastRound=round;_wasHurry=false;_pickups.Clear();}
        bool eventsAvailable=data.TryGetProperty("events",out _);
        bool hurry=(int)F(data,"phase")==2;
        if(!eventsAvailable&&hurry&&!_wasHurry)_audio.Hurry();_wasHurry=hurry;
        if(data.TryGetProperty("waterLevel",out var water))_terrain.SetWaterLevel(water.GetSingle());
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
            int appearance=p.TryGetProperty("appearance",out var selected)?selected.GetInt32():slot;
            var color=Colors[Math.Clamp(appearance,0,Colors.Length-1)];
            if(!_entities.TryGetValue(id,out var node))
            {
                node=new BomberModel(_art,color,$"P{slot+1}");_dynamic.AddChild(node);_entities[id]=node;node.Position=PositionOf(p);
            }
            ((BomberModel)node).SetSuitColor(color);
            ((BomberModel)node).KickWindup(F(p,"kickWindup"));
            _targets[id]=PositionOf(p);
            if(!_playerMotion.TryGetValue(id,out var motion))_playerMotion[id]=new SnapshotMotion(node.Position,_snapshotTick);
            else motion.Push(_targets[id],_snapshotTick);
            float angle=Mathf.Atan2(F(p,"fx"),F(p,"fz"));_facings[id]=angle;
            if(p.TryGetProperty("latchedTo",out var latch)&&latch.ValueKind==JsonValueKind.String)_ghostHosts[id]="p:"+latch.GetString();
            else _ghostHosts.Remove(id);
            ((BomberModel)node).Pose(p.GetProperty("carrying").ValueKind!=JsonValueKind.Null,p.GetProperty("stunned").GetBoolean(),ghost);
            bool falling=p.TryGetProperty("falling",out var fall)&&fall.GetBoolean();
            ((BomberModel)node).RecoveryPose(p.TryGetProperty("carriedBy",out var heldBy)&&heldBy.ValueKind==JsonValueKind.String,!falling&&p.TryGetProperty("airborne",out var air)&&air.GetBoolean());
            if(p.TryGetProperty("pickupSerial",out var serial))
            {
                int value=serial.GetInt32();
                if(_pickups.TryGetValue(id,out int previous)&&value>previous&&p.TryGetProperty("lastPickup",out var pickup))
                {
                    string kind=pickup.GetString()??"";
                    bool maximum=kind=="fire"&&F(p,"fire")>=3||kind=="bomb"&&F(p,"bombs")>=8;
                    PowerupMessage(node.Position,kind,maximum);_audio.Pickup();
                }
                _pickups[id]=value;
            }
        }
        foreach(var b in data.GetProperty("bombs").EnumerateArray())
        {
            string id="b:"+b.GetProperty("id").GetString();seen.Add(id);
            if(!_entities.TryGetValue(id,out var node))
            {
                node=new BombModel(_art);_dynamic.AddChild(node);_entities[id]=node;node.Position=PositionOf(b);if(!eventsAvailable)_audio.Bomb();
            }
            _targets[id]=PositionOf(b);
            string? carrier=b.TryGetProperty("carrierId",out var carriedBy)&&carriedBy.ValueKind==JsonValueKind.String?carriedBy.GetString():null;
            var model=(BombModel)node;
            if(model.UpdateState((int)F(b,"pumps"),(int)F(b,"level"),b.GetProperty("red").GetBoolean(),b.GetProperty("remote").GetBoolean(),F(b,"fuse"),carrier,(int)F(b,"state")))
            {
                if(!eventsAvailable)_audio.Pump(model.Charge);
                if(carrier!=null&&_entities.TryGetValue("p:"+carrier,out var player)&&player is BomberModel bomber)bomber.Pump(model.Charge);
            }
        }
        foreach(var h in data.GetProperty("hazards").EnumerateArray())
        {
            string id=h.GetProperty("id").GetString()!;
            if(_explosions.TryGetValue(id,out var active)&&GodotObject.IsInstanceValid(active))continue;
            float y=h.TryGetProperty("y",out var yValue)?yValue.GetSingle()*TempleStageView.HeightScale:_terrain.Surface(F(h,"x"),F(h,"z"));
            float radius=F(h,"r"),age=h.TryGetProperty("age",out var lifeAge)?lifeAge.GetSingle():0;
            bool red=h.TryGetProperty("red",out var redValue)&&redValue.GetBoolean();
            var blast=new ExplosionView(_art,radius,red,age,_explosions.Count>=16){Position=new(F(h,"x"),y+.3f,F(h,"z"))};
            _effects.AddChild(blast);_explosions[id]=blast;
            CountEffect("blast-"+blast.Tier);_shake=Mathf.Max(_shake,Mathf.Clamp(radius*.024f,.04f,.15f));
            if(!eventsAvailable)_audio.Blast(radius);
        }
        foreach(var item in data.GetProperty("items").EnumerateArray())
        {
            string id="i:"+item.GetProperty("id").GetString();seen.Add(id);
            if(!_entities.TryGetValue(id,out var node))
            {
                float x=F(item,"cx")+.5f,z=F(item,"cz")+.5f;node=new PowerupModel(_art,item.GetProperty("type").GetString()??"");
                node.Position=new(x,_terrain.Surface(x,z)+.48f,z);_dynamic.AddChild(node);_entities[id]=node;
            }
        }
        foreach(string id in _entities.Keys.Where(k=>!seen.Contains(k)).ToArray())
        {
            _entities[id].QueueFree();_entities.Remove(id);_targets.Remove(id);_playerMotion.Remove(id);_facings.Remove(id);_ghostHosts.Remove(id);
        }
        Present(_presentation.Read(data));
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
            if(node is BombModel {CarrierId:not null})continue;
            Vector3 previous=node.Position;
            node.Position=_playerMotion.TryGetValue(id,out var motion)?motion.Advance(dt):previous.DistanceSquaredTo(target)>16?target:previous.Lerp(target,1-Mathf.Exp(-20*dt));
            if(_facings.TryGetValue(id,out var angle))node.Rotation=new Vector3(0,Mathf.LerpAngle(node.Rotation.Y,angle,1-Mathf.Exp(-18*dt)),0);
            if(node is BomberModel model)model.Animate(dt,new Vector2(node.Position.X-previous.X,node.Position.Z-previous.Z).Length());
        }
        foreach(var (ghost,host) in _ghostHosts)
            if(_entities.TryGetValue(ghost,out var rider)&&_entities.TryGetValue(host,out var living))
                rider.Position=living.Position+new Vector3(0,1.5f,0);
        foreach(var bomb in _entities.Values.OfType<BombModel>())
            if(bomb.CarrierId is { } carrier&&_entities.TryGetValue("p:"+carrier,out var player)&&player is BomberModel actor)
                bomb.Position=bomb.Position.Lerp(_dynamic.ToLocal(actor.BombSocket),1-Mathf.Exp(-24*dt));
        foreach(var id in _explosions.Keys.Where(id=>!GodotObject.IsInstanceValid(_explosions[id])).ToArray())_explosions.Remove(id);
        _shake=Mathf.MoveToward(_shake,0,dt*.6f);_cameraRig.Shake((float)_clock,_shake);
    }
    public override void _ExitTree(){if(_cameraRig!=null)GetViewport().SizeChanged-=_cameraRig.Fit;VisibilityChanged-=UpdateProcessing;}
    private void CountEffect(string key)=>_effectCounts[key]=_effectCounts.GetValueOrDefault(key)+1;
    private void Present(IReadOnlyList<JsonElement> events)
    {
        foreach(var e in events)
        {
            string kind=e.GetProperty("kind").GetString()!,reason=e.GetProperty("reason").GetString()??"";
            string? player=e.GetProperty("playerId").GetString();
            if(player!=null&&_entities.TryGetValue("p:"+player,out var actor)&&actor is BomberModel model)model.React(kind);
            switch(kind)
            {
                case "bomb-drop":case "bomb-carry":_audio.Bomb();break;
                case "kick-windup":CountEffect(kind);break;
                case "kick":
                    _audio.Kick();CountEffect("kick");
                    var heading=player!=null&&_entities.TryGetValue("p:"+player,out var kicker)?kicker.Basis.Z:Vector3.Forward;
                    _effects.AddChild(new KickBurst(_art,heading){Position=PositionOf(e)+Vector3.Up*.25f});break;
                case "remote-fuse":PowerupMessage(PositionOf(e),"FUSE LIT!",false);CountEffect(kind);break;
                case "lift":_audio.Lift();break;
                case "player-grab":_audio.Lift();CountEffect(kind);break;
                case "player-throw":_audio.Throw();CountEffect(kind);break;
                case "player-release":case "recover":_audio.Pickup();CountEffect(kind);break;
                case "player-land":_audio.Kick();break;
                case "ghost-latch":_audio.Lift();CountEffect(kind);break;
                case "ghost-detach":_audio.Throw();CountEffect(kind);break;
                case "knockdown":
                    _audio.Kick();CountEffect(kind);
                    PowerupMessage(PositionOf(e),reason=="rolling-bomb"?"KICK IMPACT!":"FALLING BOMB HIT!",false);break;
                case "pump":_audio.Pump((int)F(e,"magnitude"));break;
                case "throw":_audio.Throw(F(e,"magnitude")/6);break;
                case "blast":_audio.Blast(F(e,"radius"));break;
                case "death":
                    bool water=reason is "water" or "fall";
                    if(water)_audio.Splash();else _audio.Death();
                    var pos=PositionOf(e);
                    if(water)
                    {
                        // Death is an immediate state transition. Finish the buffered fall
                        // at its accepted contact point, but never rewind a practice respawn.
                        if(player!=null&&_entities.TryGetValue("p:"+player,out var exiting)&&
                            _targets.TryGetValue("p:"+player,out var latest)&&latest.DistanceTo(pos)<.3f)
                        {exiting.Position=latest;_playerMotion["p:"+player]=new SnapshotMotion(latest,_snapshotTick);}
                        pos.Y=WaterHeight;
                    }
                    _effects.AddChild(new ExitEffect(_art,water){Position=pos+Vector3.Up*(water?.12f:.8f)});CountEffect(water?"splash":"death");break;
                case "round-ready":_audio.RoundReady();break;
                case "fight":_audio.Fight();break;
                case "hurry":_audio.Hurry();break;
                case "waterfall":_audio.Splash();CountEffect("water-rise");break;
                case "round-over":
                    _audio.RoundWin();
                    if(player!=null&&_entities.TryGetValue("p:"+player,out var winner)&&winner is BomberModel bomber)bomber.React("victory");break;
            }
        }
    }
    private void PowerupMessage(Vector3 position,string kind,bool maximum=false)
    {
        string text=kind switch {"fire"=>maximum?"MAX BLAST!":"BLAST UP!","bomb"=>maximum?"MAX BOMBS!":"BOMB UP!","remote"=>"REMOTE!","red"=>"POWER BOMB!","heart"=>"HEART!",_=>kind};
        var label=new Label3D {Text=text,Position=position+new Vector3(0,2.2f,0),FontSize=34,PixelSize=.012f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=true,OutlineSize=10,Modulate=new Color("ffe571"),OutlineModulate=new Color("233657")};
        _effects.AddChild(label);var tween=label.CreateTween().SetParallel();tween.TweenProperty(label,"position",label.Position+Vector3.Up*.9f,1.2);tween.TweenProperty(label,"modulate:a",0f,1.2);tween.Chain().TweenCallback(Callable.From(label.QueueFree));
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

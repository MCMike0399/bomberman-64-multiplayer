using Godot;
using System.Text.Json;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Simulation.Stages;
using BlastCircuit.Arena.Effects;
using BlastCircuit.Arena.Models;
using BlastCircuit.Audio;
using BlastCircuit.Rendering;
using BlastCircuit.Arena.Stages;
namespace BlastCircuit.Arena;

/// <summary>Snapshot-driven presentation. Geometry, movement authority, and UI never share mutable entities.</summary>
public partial class ArenaView : Node3D
{
    public static readonly Color[] Colors=ArtMaterials.PlayerColors;
    private readonly ArtMaterials _art=new();
    private readonly Dictionary<string,PlayerInput> _localInputs=[];
    private readonly Dictionary<string,BombThrowPreview> _throwPreviews=[];
    private bool _battleActive;
    private MeshInstance3D? _tutorialTarget;
    public void SetBattleActive(bool active)
    {
        _battleActive=active;
        if(_terrain!=null){_terrain.WeatherActive=active;if(_terrain.Weather!=null)_terrain.Weather.Active=active;}
        if(!active && _tutorialTarget!=null)_tutorialTarget.Visible=false;
        if(!active)ClearThrowPreviews();
        foreach(var meteor in _entities.Values.OfType<MeteorView>())meteor.SetProcess(active);
    }
    public IReadOnlyList<ThrowPreviewState> ThrowPreviews {get;private set;}=[];
    public void SetLocalInput(string playerId,PlayerInput input)=>_localInputs[playerId]=input;
    public event Action<string,string?>? PlayerFeedback;
    private readonly Node3D _dynamic=new(),_effects=new();
    private readonly Dictionary<string,Node3D> _entities=[];
    private readonly HashSet<string> _seenEntities=new(StringComparer.Ordinal);
    private readonly List<string> _staleEntities=[];
    private readonly Dictionary<string,Vector3> _targets=[];
    private readonly Dictionary<string,SnapshotMotion> _playerMotion=[];
    private readonly Dictionary<string,float> _facings=[];
    private readonly Dictionary<string,string> _ghostHosts=[];
    private long _snapshotTick;
    private readonly Dictionary<int,Node3D> _blocks=[];
    private readonly Dictionary<string,ExplosionView> _explosions=[];
    private readonly List<string> _expiredExplosions=[];
    private string? _softBlockEncoding;
    private byte[] _softBlockBits=[];
    private readonly Dictionary<string,int> _effectCounts=[];
    private readonly PresentationCursor _presentation=new();
    private readonly Dictionary<int,Node3D> _walls=[];
    private TempleStageView? _terrain;
    private Godot.Environment _environment=null!;
    private ProceduralSkyMaterial _sky=null!;
    private DirectionalLight3D _keyLight=null!,_fillLight=null!;
    public StageTime TimeOfDay {get;private set;}=StageTime.Midday;
    private Camera3D _camera=null!;
    private readonly CameraAttributesPractical _cameraAttributes=new() {DofBlurAmount=.075f};
    private Transform3D _depthCueTransform;
    private BattleCameraRig _cameraRig=null!;
    private BattleAudio _audio=null!;
    private string _stage="";
    private double _clock;
    private float _shake;
    private int _lastRound=-1;
    private bool _wasHurry;
    private readonly Dictionary<string,int> _pickups=[];
    public float WaterHeight=>_terrain?.WaterHeight??0;
    public IReadOnlyList<BombRenderPose> BombRenderPoses=>_entities.Where(pair=>pair.Value is BombModel).Select(pair=>
    {
        var bomb=(BombModel)pair.Value;float? socketY=null;
        if(bomb.CarrierId is {} carrier&&_entities.TryGetValue("p:"+carrier,out var player)&&player is BomberModel actor)socketY=actor.BombSocket.Y;
        var position=bomb.GlobalPosition;
        return new BombRenderPose(pair.Key,bomb.CarrierId,position.X,position.Y,position.Z,socketY);
    }).ToArray();
    public float LargestBombScale=>_entities.Values.OfType<BombModel>().Select(b=>b.VisualScale).DefaultIfEmpty(0).Max();
    public int PumpAnimationCount=>_entities.Values.OfType<BomberModel>().Sum(p=>p.PumpAnimationCount);
    public float PumpIntensity=>_entities.Values.OfType<BomberModel>().Select(p=>p.PumpIntensity).DefaultIfEmpty(0).Max();
    public int CarriedBombs=>_entities.Values.OfType<BombModel>().Count(b=>b.CarrierId!=null);
    public int VisiblePowerups=>_entities.Values.Count(n=>n is PowerupModel);
    public string StageId=>_stage;
    public int VisiblePlayerCount=>_entities.Values.Count(n=>n is BomberModel);
    public BattleAudio Audio=>_audio;
    public object PresentationState=>new {sequence=_presentation.Sequence,audio=_audio.Counts,voicePlays=_audio.VoicePlayCount,droppedCues=_audio.DroppedCueCount,effects=_effectCounts,explosions=_explosions.Values.Where(GodotObject.IsInstanceValid).Select(v=>new {tier=v.Tier,radius=v.Radius,age=v.Age,phase=v.Phase,bodyAspect=v.BodyAspect,geometry=v.GeometryCount,flares=v.FlareCount,powerWave=v.PowerWaveVisible,hazard=v.HazardActive,extent=v.VisualExtent}).ToArray()};
    public void ResetPresentationBaseline()=>_presentation.Reset();
    public Task PrepareEffects()=>EffectWarmup.Run(GetParent(),_art,_environment,_keyLight,_fillLight);
    public void ApplyGraphicsQuality()
    {
        GraphicsQuality.ApplyArena(_environment,_cameraAttributes,GraphicsQuality.GlossyStage(_stage));
        // Smaller shadow texels need more depth bias to avoid speckled self-shadowing.
        float grazingSun=TimeOfDay is StageTime.Morning or StageTime.Afternoon?2:1;
        _keyLight.ShadowBias=grazingSun*(GraphicsQuality.Current switch {GraphicsPreset.Low=>.12f,GraphicsPreset.Medium=>.06f,_=>.025f});
        // Contact-hardening sun shadows (blocker search per pixel) stay a High feature.
        _keyLight.LightAngularDistance=GraphicsQuality.Current==GraphicsPreset.High?(TimeOfDay==StageTime.Afternoon?2.4f:1.8f):0;
        _terrain?.SetSceneryVisible(GraphicsQuality.Current!=GraphicsPreset.Low);
    }

    public override void _Ready()
    {
        AddChild(_dynamic);AddChild(_effects);_audio=new BattleAudio();AddChild(_audio);
        _sky=new ProceduralSkyMaterial {SkyCurve=.22f,GroundCurve=.3f,SunAngleMax=8,SunCurve=.08f,UseDebanding=true};
        _environment=new Godot.Environment
        {
            BackgroundMode=Godot.Environment.BGMode.Sky,Sky=new Sky {SkyMaterial=_sky},
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightSkyContribution=0,
            TonemapMode=Godot.Environment.ToneMapper.Filmic,ReflectedLightSource=Godot.Environment.ReflectionSource.Sky,
            SsaoEnabled=true,SsaoRadius=.7f,SsaoIntensity=1.65f,SsaoPower=1.25f,
            GlowEnabled=true,GlowIntensity=.17f,GlowBloom=.025f,GlowHdrThreshold=.9f,
            AdjustmentEnabled=true,AdjustmentContrast=1.08f,AdjustmentSaturation=1.02f,
            // Haze starts beyond the playable arena (see ApplyDepthCues) in the horizon
            // color, so the diorama melts into its horizon without fogging play. Aerial
            // perspective stays off: a downward camera would sample the dark lower sky.
            FogEnabled=true,FogMode=Godot.Environment.FogModeEnum.Depth,FogDensity=.32f,FogDepthCurve=1.6f,
            FogSunScatter=.15f,
            SsilRadius=2.6f,SsilIntensity=.75f,SsilNormalRejection=.8f,
            // High-only (see GraphicsQuality.ApplyArena): a short march keeps the mirror
            // on nearby walls and figures; fades hide the screen border and grazing misses.
            SsrEnabled=false,SsrMaxSteps=64,SsrFadeIn=.15f,SsrFadeOut=2,SsrDepthTolerance=.25f
        };
        AddChild(new WorldEnvironment {Environment=_environment});
        _keyLight=new DirectionalLight3D {ShadowEnabled=true,
            DirectionalShadowMaxDistance=65,ShadowBias=.025f,ShadowNormalBias=.45f,LightAngularDistance=1.8f};AddChild(_keyLight);
        _fillLight=new DirectionalLight3D {RotationDegrees=new(-25,135,0),SkyMode=DirectionalLight3D.SkyModeEnum.LightOnly};AddChild(_fillLight);
        SetTimeOfDay(TimeOfDay);
        _camera=new Camera3D {Projection=Camera3D.ProjectionType.Perspective,Fov=36,Near=.1f,Far=180,Current=true,Attributes=_cameraAttributes};AddChild(_camera);
        _cameraRig=new BattleCameraRig(_camera);
        GetViewport().SizeChanged+=FitCamera;
        VisibilityChanged+=UpdateProcessing;
        UpdateProcessing();
    }
    public void SetTimeOfDay(StageTime time)
    {
        TimeOfDay=Enum.IsDefined(time)?time:StageTime.Midday;
        if(_environment==null)return;
        var look=StageLighting.For(TimeOfDay);
        _keyLight.RotationDegrees=look.KeyRotation;_keyLight.LightColor=look.KeyColor;_keyLight.LightEnergy=look.KeyEnergy;
        ApplyGraphicsQuality();
        _fillLight.LightColor=look.Fill;_fillLight.LightEnergy=look.FillEnergy;
        _environment.AmbientLightColor=look.Ambient;_environment.AmbientLightEnergy=look.AmbientEnergy;
        _environment.FogLightColor=look.Horizon;
        _sky.SkyTopColor=look.Zenith;_sky.SkyHorizonColor=look.Horizon;
        _sky.GroundHorizonColor=look.Horizon;_sky.GroundBottomColor=look.Ground;
        _sky.SkyEnergyMultiplier=look.SkyEnergy;_sky.GroundEnergyMultiplier=look.SkyEnergy;
        _terrain?.SetLighting(look);
    }
    private void UpdateProcessing()
    {
        ProcessMode=IsVisibleInTree()?ProcessModeEnum.Inherit:ProcessModeEnum.Disabled;
        GetViewport().Disable3D=!IsVisibleInTree();
        if(!IsVisibleInTree())_audio?.StopAll();
        if(_camera!=null)foreach(var preview in _throwPreviews.Values)preview.UpdateScreen(_camera);
    }
    public void ResetMatch()
    {
        if(_tutorialTarget!=null)_tutorialTarget.Visible=false;
        _localInputs.Clear();ClearThrowPreviews();
        _seenEntities.Clear();_staleEntities.Clear();_expiredExplosions.Clear();
        _softBlockEncoding=null;_softBlockBits=[];
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
        _stage=id;ClearThrowPreviews();
        _softBlockEncoding=null;_softBlockBits=[];
        _environment.BackgroundMode=id==BlastStages.TopRules?Godot.Environment.BGMode.Color:Godot.Environment.BGMode.Sky;
        _environment.BackgroundColor=new Color("171a22");
        _environment.FogSkyAffect=id==BlastStages.TopRules?0:1;
        ApplyGraphicsQuality();
        _playerMotion.Clear();_facings.Clear();_ghostHosts.Clear();
        _terrain?.QueueFree();_terrain=new TempleStageView(_art);AddChild(_terrain);_terrain.Build(data);
        _terrain.SetLighting(StageLighting.For(TimeOfDay));
        _terrain.WeatherActive=_battleActive;
        foreach(Node child in _dynamic.GetChildren())child.QueueFree();
        foreach(Node child in _effects.GetChildren())child.QueueFree();
        _entities.Clear();_targets.Clear();_blocks.Clear();_explosions.Clear();_presentation.Reset();_walls.Clear();_pickups.Clear();_lastRound=-1;_wasHurry=false;
        _cameraRig.SetStage(data);
        ApplyDepthCues();
    }
    private void FitCamera(){_cameraRig.Fit();ApplyDepthCues();}
    /// <summary>Nearest/farthest playable view depth used to keep blur and haze off the arena.</summary>
    public Vector2 PlayableDepth=>_cameraRig.PlayableDepth;
    /// <summary>Tilt-shift style blur and horizon haze begin just outside the fitted playable depth.</summary>
    private void ApplyDepthCues()
    {
        var depth=_cameraRig.PlayableDepth;
        _cameraAttributes.DofBlurFarDistance=depth.Y+2;
        _cameraAttributes.DofBlurFarTransition=Mathf.Max(10,depth.Y*.45f);
        _cameraAttributes.DofBlurNearDistance=Mathf.Max(.5f,depth.X-2.5f);
        _cameraAttributes.DofBlurNearTransition=Mathf.Max(1,depth.X*.2f);
        _environment.FogDepthBegin=_cameraRig.PlayableDistance+3;
        _environment.FogDepthEnd=_cameraRig.PlayableDistance+70;
    }
    public override void _Process(double delta)
    {
        _clock+=delta;float dt=(float)delta;
        float positionBlend=1-Mathf.Exp(-20*dt),facingBlend=1-Mathf.Exp(-18*dt),carryBlend=1-Mathf.Exp(-24*dt);
        // Include projection offsets in the effective view used by radial fog.
        var viewTransform=_camera.GetCameraTransform();
        if(viewTransform!=_depthCueTransform){_depthCueTransform=viewTransform;_cameraRig.MeasureDepth();ApplyDepthCues();}
        foreach(var (id,target) in _targets)
        {
            if(!_entities.TryGetValue(id,out var node))continue;
            if(node is BombModel {CarrierId:not null})continue;
            Vector3 previous=node.Position;
            var position=_playerMotion.TryGetValue(id,out var motion)?motion.Advance(dt):previous.DistanceSquaredTo(target)>16?target:previous.Lerp(target,positionBlend);
            node.Position=position;
            if(_facings.TryGetValue(id,out var angle))node.Rotation=new Vector3(0,Mathf.LerpAngle(node.Rotation.Y,angle,facingBlend),0);
            if(node is BomberModel model)model.Animate(dt,new Vector2(position.X-previous.X,position.Z-previous.Z).Length());
        }
        foreach(var (ghost,host) in _ghostHosts)
            if(_entities.TryGetValue(ghost,out var rider)&&_entities.TryGetValue(host,out var living))
                rider.Position=living.Position+new Vector3(0,1.5f,0);
        foreach(var entity in _entities.Values)
            if(entity is BombModel {CarrierId: { } carrier} bomb&&_entities.TryGetValue("p:"+carrier,out var player)&&player is BomberModel actor)
                bomb.Position=bomb.Position.Lerp(_dynamic.ToLocal(actor.BombSocket),carryBlend);
        foreach(var (id,explosion) in _explosions)
            if(!GodotObject.IsInstanceValid(explosion))_expiredExplosions.Add(id);
        foreach(var id in _expiredExplosions)_explosions.Remove(id);
        _expiredExplosions.Clear();
        AdvanceClosure(dt);
        _shake=Mathf.MoveToward(_shake,0,dt*.6f);_cameraRig.Shake((float)_clock,_shake);
        foreach(var preview in _throwPreviews.Values)preview.UpdateScreen(_camera);
    }
    public override void _ExitTree(){if(_cameraRig!=null)GetViewport().SizeChanged-=FitCamera;VisibilityChanged-=UpdateProcessing;}
    private static float F(JsonElement e,string key)=>e.GetProperty(key).GetSingle();
    private static Vector3 PositionOf(JsonElement e)=>new(F(e,"x"),F(e,"y")*TempleStageView.HeightScale,F(e,"z"));
}

public sealed record BombRenderPose(string Id,string? CarrierId,float X,float Y,float Z,float? SocketY);

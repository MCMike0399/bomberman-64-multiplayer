using Godot;
using Bomber.Client;
using Bomber.Game;
using BlastCircuit.UI;
using BlastCircuit.InputDevices;
using BlastCircuit.Rendering;
using System.Text.Json;
namespace BlastCircuit;

/// <summary>Composition root for screen navigation, session lifetime, controls and snapshot presentation.</summary>
public partial class GameRoot : Node3D
{
    private ArenaView _arena=null!;
    private ArcadeMenu _menu=null!;
    private BattleHud _hud=null!;
    private BlastCircuit.Rendering.BattleAudio _uiAudio=null!;
    private ISession? _session;
    private CancellationTokenSource? _connectCancel;
    private readonly Dictionary<int,PlayerInput> _injected=[];
    private bool _connecting,_practice,_setupForHost,_smoke,_editingRoom;
    private string _capture="gameplay";
    private double _elapsed,_smokeDuration=8;
    private SessionSettings _lastSettings=new();
    private int _lastHumans=1;
    private int[]? _lastAppearances;
    private int _lastOnlineAppearance;
    private NetworkMode _lastMode;
    private Uri? _lastServer;
    private string _lastName="Player",_lastCode="";
    private JsonElement? _lobby;
    private JsonElement? _result;
    private string _screenName="Modes", _settingsReturn="Modes", _pauseReturn="Match";
    private int _partyMatch=-1;
    private PlayerPreferences _preferences=new();
    private GraphicsPreset? _appliedGraphics;
    private bool _preparingGraphics,_skipGraphicsPreparation;
    public bool GraphicsPreparing=>_preparingGraphics;
    private BlastCircuit.Updates.GameUpdater _updater=null!;
    public string PreferencesPath {get;private set;}="user://preferences.cfg";
    public PlayerPreferences Preferences=>_preferences;
    public double GraphicsWarmupMilliseconds {get;private set;}
    public string ScreenName
    {
        get=>_screenName;
        private set
        {
            _screenName=value;
            _arena?.SetBattleActive(value=="Match");
            // Input role follows the visible screen; lobby/results retain match bindings.
            if(ControllerHub.Current is {} hub)hub.MenuNavigationEnabled=!_preparingGraphics&&value is not ("Match" or "Controls");
        }
    }
    public int SnapshotCount { get; private set; }
    public JsonElement? LatestState { get; private set; }
    public JsonElement? LatestStage { get; private set; }
    public ISession? ActiveSession=>_session;
    public IReadOnlyList<BlastCircuit.Rendering.ThrowPreviewState> ThrowPreviews=>_arena.ThrowPreviews;
    public IReadOnlyList<BlastCircuit.BombRenderPose> BombRenderPoses=>_arena.BombRenderPoses;
    public object VisualState=>new {waterHeight=_arena.WaterHeight,largestBombScale=_arena.LargestBombScale,pumpAnimations=_arena.PumpAnimationCount,pumpIntensity=_arena.PumpIntensity,carriedBombs=_arena.CarriedBombs,powerups=_arena.VisiblePowerups};
    public JsonElement? LatestLobby=>_lobby;
    public object RosterState=>_menu.RosterState;
    public object PresentationState=>_arena.PresentationState;
    public BlastCircuit.Rendering.BattleAudio BattleAudio=>_arena.Audio;
    public int SelectedAppearance=>_menu.OnlineAppearance;
    public void ConfigureRosterSlot(int slot,string role,int appearance)=>_menu.ConfigureRosterSlot(slot,role.ToLowerInvariant() switch {"human"=>RosterRole.Human,"cpu"=>RosterRole.Cpu,"off"=>RosterRole.Off,_=>throw new ArgumentException("Role must be human, cpu or off.")},appearance);
    public void SetOnlineAppearance(int appearance)=>_menu.SetOnlineAppearance(appearance);

    public override async void _Ready()
    {
        var args=OS.GetCmdlineUserArgs();
        _uiAudio=new BlastCircuit.Rendering.BattleAudio();AddChild(_uiAudio);
        _arena=new ArenaView();AddChild(_arena);_arena.Visible=false;
        var layer=new CanvasLayer();AddChild(layer);
        _menu=new ArcadeMenu();layer.AddChild(_menu);
        _hud=new BattleHud {Visible=false};layer.AddChild(_hud);
        _menu.AppearanceChanged+=index=>{if(_session is NetworkSession network)network.SetAppearance(index);};
        _menu.Navigation+=Navigate;_menu.BackRequested+=Back;_menu.ReplayRequested+=Replay;
        _menu.LocalRequested+=StartConfigured;_menu.OnlineRequested+=StartConfiguredOnline;
        _hud.LeaveRequested+=OpenGameMenu;_hud.RestartRequested+=Replay;
        _menu.StartRoomRequested+=()=>{if(_session is NetworkSession network)network.RequestStart();};
        _menu.ResumeRequested+=ResumeGame;
        _menu.SettingsChanged+=SavePreferences;
        _menu.TestVibrationRequested+=()=>_menu.SettingsNotice(ControllerHub.Current.TestVibration()?"Vibration sent to your controller.":_preferences.Vibration?"Connect a controller that supports vibration.":"Enable controller vibration first.");
        if(OS.GetCmdlineUserArgs().Any(a=>a.StartsWith("--playtest=")))PreferencesPath=$"user://qa-preferences-{OS.GetProcessId()}.cfg";
        _preferences=PlayerPreferences.Load(PreferencesPath);_menu.ConfigurePreferences(_preferences);ApplyPreferences();
        _menu.IdentityChanged+=SavePreferences;
        _arena.PlayerFeedback+=PlayerFeedback;
        _updater=new BlastCircuit.Updates.GameUpdater();AddChild(_updater);
        _menu.CheckUpdatesRequested+=()=>{_ = _updater.Check();};
        _menu.InstallUpdateRequested+=async ()=>{if(_session==null&&!_connecting&&await _updater.Install())GetTree().Quit();};
        bool coldEffects=args.Any(a=>a.StartsWith("--playtest="))&&args.Contains("--playtest-cold-effects");
        _skipGraphicsPreparation=DisplayServer.GetName()=="headless"||coldEffects;
        if(!_skipGraphicsPreparation)
        {
            var warmupClock=System.Diagnostics.Stopwatch.StartNew();
            _preparingGraphics=true;
            ScreenName="Loading";_menu.Visible=false;ControllerHub.Current.MenuNavigationEnabled=false;
            var loading=PreparationOverlay("Getting ready...");
            try
            {
                await EffectWarmup.Draw(this);
                await _arena.PrepareEffects();
            }
            finally
            {
                GraphicsWarmupMilliseconds=warmupClock.Elapsed.TotalMilliseconds;
                _preparingGraphics=false;
                loading.QueueFree();ScreenName="Modes";_menu.Visible=true;_menu.ShowScreen("Modes");
            }
        }
        _smoke=args.Any(a=>a.StartsWith("--smoke"));
        var duration=args.FirstOrDefault(a=>a.StartsWith("--smoke-seconds="));
        if(duration!=null&&double.TryParse(duration.Split('=')[1],out var seconds))_smokeDuration=Math.Clamp(seconds,1,120);
        if(args.Contains("--smoke-roster")){_capture="roster";Navigate("Roster");}
        else if(args.Contains("--smoke-menu"))_capture="menu";
        else if(args.Contains("--smoke-online")){_capture="online";_ = StartOnlineAsync(new Uri("http://127.0.0.1:18496"),"Player",new SessionSettings(Bots:7),NetworkMode.CreatePrivate);}
        else if(_smoke)
        {
            var settings=new SessionSettings();
            if(args.Contains("--smoke-tier")){settings=settings with { Stage="up-and-down"};_capture="tiers";}
            StartLocal(settings);
        }
        Bomber.Testing.PlaytestDriver.TryAttach(this);
    }

    public void Navigate(string screen)
    {
        if(screen=="Advanced" && ScreenName=="Setup"){ScreenName="Advanced";_menu.ShowAdvanced();return;}
        if(screen=="HostSetup"&&_session is NetworkSession room)
        {
            if(!room.IsLeader||room.Party?.Phase!="lobby"){_menu.Notice("Only the room leader can change waiting-room rules.");return;}
            _editingRoom=true;_setupForHost=true;
            if(room.Party.Settings is {} rules)_menu.Configure(SessionSettings.FromWire(rules));
            ScreenName="Setup";_arena.Visible=false;_hud.Visible=false;_menu.SetOverlay(false);_menu.Visible=true;_menu.ShowScreen("HostSetup");return;
        }
        if(screen=="Settings")
        {
            _settingsReturn=ScreenName;ScreenName="Settings";_menu.SetOverlay(_session!=null&&LatestState!=null);_menu.Visible=true;_hud.Visible=false;_menu.ShowScreen("Settings");return;
        }
        _editingRoom=false;
        _menu.SetOverlay(false);
        if(screen is not ("Modes" or "Controls" or "Roster" or "Setup" or "Practice" or "HostSetup" or "Online" or "Lobby" or "Match" or "Results"))throw new ArgumentException("Unknown screen.",nameof(screen));
        if(screen is "Modes" or "Controls" or "Roster" or "Online" or "Setup" or "Practice" or "HostSetup")
        {
            CancelConnection();
            if(_session!=null)ReleaseSession();
        }
        if(screen!=ScreenName){if(screen=="Modes"||(screen=="Roster"&&ScreenName=="Setup"))_uiAudio.MenuBack();else _uiAudio.MenuConfirm();}
        _setupForHost=screen=="HostSetup";
        ControllerHub.Current.TesterActive=screen=="Controls";
        ScreenName=screen=="Practice"||screen=="HostSetup"?"Setup":screen;
        _arena.Visible=ScreenName is "Match" or "Results";
        _hud.Visible=ScreenName=="Match";
        _menu.Visible=ScreenName!="Match";
        if(_menu.Visible)_menu.ShowScreen(screen);
    }
    private void StartConfigured()
    {
        if(_editingRoom)
        {
            if(_session is NetworkSession {IsLeader:true,Party.Phase:"lobby"} room)room.SetRoomSettings(_menu.OnlineSettings);
            _editingRoom=false;ShowPartyLobby();return;
        }
        if(_setupForHost){Navigate("Online");return;}
        int humans=_menu.HumanCount;
        if(humans>LocalControllerLayout.Capacity(ControllerHub.Current.PlayableCount))
        {_menu.Notice($"Connect more gamepads for {humans} human players. Two keyboard seats are available.");return;}
        StartLocal(_menu.Settings,humans,_menu.Practice,_menu.PlayerAppearances);
    }
    public void StartLocal(SessionSettings settings,int humans=1,bool practice=false,IReadOnlyList<int>? playerAppearances=null)
    {
        if(_connecting)return;
        try
        {
            var session=new LocalSession(settings,humans,practice:practice,playerAppearances:playerAppearances);
            _lastSettings=settings;_lastHumans=humans;_lastAppearances=playerAppearances?.ToArray();_practice=practice;_lastServer=null;
            Begin(session,false);
        }
        catch(Exception error){_menu.Notice(error.Message);GD.PushWarning(error.Message);}
    }
    private async void StartConfiguredOnline(NetworkMode mode)
    {
        try{await StartOnlineAsync(new Uri(_menu.ServerUrl),_menu.PlayerName,_menu.OnlineSettings,mode,_menu.Code,appearance:_menu.OnlineAppearance);}
        catch(Exception error){_menu.SetBusy(false,"Could not connect: "+error.Message);}
    }
    public async Task StartOnlineAsync(Uri server,string name,SessionSettings settings,NetworkMode mode,string? code=null,int appearance=0)
    {
        if(_connecting)throw new InvalidOperationException("A connection is already in progress.");
        ReleaseSession();_menu.SetOnlineAppearance(appearance);_connecting=true;_practice=false;
        var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(120));_connectCancel=cancellation;
        ScreenName="Online";_menu.Visible=true;_arena.Visible=false;_hud.Visible=false;_menu.ShowScreen("Online");_menu.SetBusy(true,mode==NetworkMode.QuickPlay?"Searching for players... Cancel to leave the queue.":"Connecting to your battle room...");
        try
        {
            var session=await NetworkSession.ConnectAsync(server,name,settings,mode,code,cancellation.Token,appearance);
            if(cancellation.IsCancellationRequested||!IsInsideTree()){await session.DisposeAsync();return;}
            _lastSettings=settings;_lastHumans=1;_lastServer=server;_lastName=name;_lastOnlineAppearance=appearance;_lastMode=mode;_lastCode=code??"";
            Begin(session,true);
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested){if(IsInsideTree())_menu.Notice("Connection cancelled. Choose a mode to try again.");}
        catch(Exception error){if(IsInsideTree())_menu.Notice("Could not connect: "+error.Message);throw;}
        finally
        {
            if(ReferenceEquals(_connectCancel,cancellation)){_connectCancel=null;_connecting=false;if(IsInsideTree())_menu.SetBusy(false);}
            cancellation.Dispose();
        }
    }
    private void Begin(ISession session,bool online)
    {
        _uiAudio.MenuConfirm();
        ReleaseSession();_arena.ResetMatch();_session=session;LatestStage=null;LatestState=null;_result=null;_lobby=null;SnapshotCount=0;_injected.Clear();_hud.Reset();_hud.Practice=_practice;_menu.ResetLobby();_partyMatch=-1;
        ControllerHub.Current.BeginMatch(session.LocalPlayerCount);
        ApplyStageLighting();
        if(online)
        {ScreenName="Lobby";_menu.ShowScreen("Lobby");_menu.ShowLobby(null,session.JoinCode,session.Status);_menu.Visible=true;_hud.Visible=false;_arena.Visible=false;}
        else EnterMatch();
        if(!online)GetViewport().GuiReleaseFocus();
    }
    private void EnterMatch()
    {
        _editingRoom=false;ScreenName="Match";_menu.Visible=false;_arena.Visible=true;_hud.Visible=true;GetViewport().GuiReleaseFocus();
    }
    public void InjectInput(int seat,PlayerInput? input)
    {
        if(input is { } value)_injected[seat]=value;else _injected.Remove(seat);
    }
    public void Back()
    {
        if(ScreenName=="Advanced"){ScreenName="Setup";_menu.CloseAdvanced();return;}
        if(_editingRoom&&ScreenName=="Setup"){_editingRoom=false;ShowPartyLobby();return;}
        if(ScreenName=="Settings"){RestoreScreen(_settingsReturn);return;}
        if(ScreenName=="Pause"){ResumeGame();return;}
        if(_connecting){CancelConnection();_menu.SetBusy(false,"Connection cancelled.");ScreenName="Online";_menu.ShowScreen("Online");return;}
        if(_session!=null){ReleaseSession();Navigate("Modes");return;}
        if(ScreenName=="Setup"&&_setupForHost)Navigate("Online");else if(ScreenName=="Setup"&&!_menu.Practice)Navigate("Roster");else Navigate("Modes");
    }
    private void Replay()
    {
        if(_session is NetworkSession {Party:not null}){ShowPartyLobby();return;}
        ReleaseSession();
        if(_lastServer is { } server)
        {
            // Quick play finds a fresh match; a private host gets a fresh shareable room.
            if(_lastMode==NetworkMode.JoinCode){Navigate("Online");_menu.Notice("Enter a new room code to join your next battle.");}
            else _=ReplayOnline(server);
        }
        else StartLocal(_lastSettings,_lastHumans,_practice,_lastAppearances);
    }
    private async Task ReplayOnline(Uri server)
    {try{await StartOnlineAsync(server,_lastName,_lastSettings,_lastMode,_lastCode,_lastOnlineAppearance);}catch(Exception error){_menu.Notice(error.Message);}}
    private void CancelConnection()
    {
        var cancellation=_connectCancel;_connectCancel=null;_connecting=false;cancellation?.Cancel();
    }
    private void ReleaseSession()
    {
        var old=_session;_session=null;_injected.Clear();
        ControllerHub.Current.EndMatch();
        if(old!=null)_=DisposeSessionAsync(old);
    }
    private static async Task DisposeSessionAsync(ISession session)
    {try{await session.DisposeAsync();}catch(Exception error){GD.PushWarning("Session cleanup: "+error.Message);}}
    public override void _UnhandledInput(InputEvent @event)
    {
        if(_preparingGraphics)return;
        bool start=@event is InputEventJoypadButton {Pressed:true,ButtonIndex:JoyButton.Start};
        bool cancel=@event is InputEventKey {Pressed:true,Echo:false,Keycode:Key.Escape}
            || (ScreenName is not ("Match" or "Controls") && @event.IsActionPressed("ui_cancel"));
        if(start)
        {
            if(ScreenName=="Match")OpenGameMenu();
            else if(ScreenName=="Controls")Back();
            else _menu.FocusStart();
            GetViewport().SetInputAsHandled();return;
        }
        if(cancel)
        {
            if(ScreenName is "Match" or "Lobby")OpenGameMenu();else Back();
            GetViewport().SetInputAsHandled();
        }
    }
    private void OpenGameMenu()
    {
        if(_session==null)return;
        _pauseReturn=ScreenName;_session.ReleaseInputs();_injected.Clear();ControllerHub.Current.RearmControls();ControllerHub.Current.StopVibration();
        ScreenName="Pause";_hud.Visible=false;_menu.SetOverlay(LatestState!=null);_menu.Visible=true;_menu.ShowPause(_session is NetworkSession);
    }
    private void ResumeGame(){_session?.ReleaseInputs();ControllerHub.Current.RearmControls();RestoreScreen(_pauseReturn);}
    private void RestoreScreen(string screen)
    {
        if(screen=="Pause"){ScreenName="Pause";_menu.Visible=true;_menu.SetOverlay(LatestState!=null);_menu.ShowPause(_session is NetworkSession);return;}
        if(screen=="Match"&&_session!=null){EnterMatch();return;}
        if(screen=="Lobby"&&_session!=null){ShowPartyLobby();return;}
        Navigate(screen);
    }
    private void ShowPartyLobby()
    {
        _session?.ReleaseInputs();ControllerHub.Current.StopVibration();ControllerHub.Current.RearmControls();
        ScreenName="Lobby";_arena.Visible=false;_hud.Visible=false;_menu.SetOverlay(false);_menu.Visible=true;_menu.ShowScreen("Lobby");
    }
    private void ApplyPreferences()
    {
        _uiAudio.Volume=_preferences.Volume;_arena.Audio.Volume=_preferences.Volume;
        ControllerHub.Current.VibrationEnabled=_preferences.Vibration;
        if(_appliedGraphics!=_preferences.Graphics)
        {
            GraphicsQuality.Apply(_preferences.Graphics,GetViewport());
            _arena.ApplyGraphicsQuality();
            _appliedGraphics=_preferences.Graphics;
        }
        ApplyStageLighting();
    }
    private void ApplyStageLighting()
    {
        var time=_preferences.StageLighting;
        if(_session is NetworkSession)
            time=LatestState is {} state&&state.TryGetProperty("stageLighting",out var lighting)&&lighting.ValueKind==JsonValueKind.String
                ?StageLighting.FromWire(lighting.GetString()):StageTime.Midday;
        if(_arena.TimeOfDay!=time)_arena.SetTimeOfDay(time);
    }
    private CanvasLayer PreparationOverlay(string message)
    {
        var layer=new CanvasLayer {Layer=100};AddChild(layer);
        var backdrop=new ColorRect {Color=ArcadeTheme.Night,FocusMode=Control.FocusModeEnum.All};layer.AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);backdrop.GrabFocus();
        var center=new CenterContainer();backdrop.AddChild(center);center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var column=ArcadeTheme.Column(center,24);
        ArcadeTheme.Heading(column,"Blast Circuit",54).HorizontalAlignment=HorizontalAlignment.Center;
        ArcadeTheme.Text(column,message,24).HorizontalAlignment=HorizontalAlignment.Center;
        return layer;
    }
    private async void SavePreferences()
    {
        bool graphicsChanged=_preferences.Graphics!=_menu.Graphics;
        _preferences.PlayerName=_menu.PlayerName;_preferences.ServerUrl=_menu.ServerUrl;
        _preferences.AutomaticUpdates=_menu.AutomaticUpdates;
        _preferences.StageLighting=_menu.LocalStageTime;
        _preferences.Graphics=_menu.Graphics;
        _preferences.Vibration=_menu.VibrationEnabled;_preferences.Volume=_menu.SoundVolume;ApplyPreferences();
        if(_preferences.Save(PreferencesPath)!=Error.Ok)_menu.SettingsNotice("Could not save settings on this device.");
        if(graphicsChanged&&!_skipGraphicsPreparation)
        {
            string screen=ScreenName;bool menuVisible=_menu.Visible;var focus=GetViewport().GuiGetFocusOwner();
            _preparingGraphics=true;_menu.Visible=false;ControllerHub.Current.MenuNavigationEnabled=false;
            var loading=PreparationOverlay("Applying graphics...");
            try {await EffectWarmup.Draw(this);await _arena.PrepareEffects();}
            finally
            {
                loading.QueueFree();_preparingGraphics=false;
                if(ScreenName==screen)_menu.Visible=menuVisible;
                ControllerHub.Current.MenuNavigationEnabled=ScreenName is not ("Match" or "Controls");
                if(_menu.Visible)
                {
                    if(GodotObject.IsInstanceValid(focus)&&focus.IsVisibleInTree())focus.GrabFocus();
                    else _menu.FocusStart();
                }
            }
        }
    }
    private void PlayerFeedback(string kind,string? player)
    {
        if(ScreenName!="Match"||_session==null||player==null)return;
        for(int seat=0;seat<_session.LocalPlayerCount;seat++)
        {
            var id=_session is NetworkSession network?network.PlayerId:$"local-{seat}";
            if(id!=player)continue;
            var pulse=kind switch {"death"=>(.7f,.9f,.25f),"knockdown"=>(.5f,.7f,.18f),"kick"=>(.3f,.2f,.1f),"pump"=>(.25f,.15f,.08f),"bomb-drop"=>(.15f,0f,.05f),_=>(0f,0f,0f)};
            if(pulse.Item3>0)ControllerHub.Current.VibrateSeat(seat,pulse.Item1,pulse.Item2,pulse.Item3);
        }
    }
    public override void _Process(double delta)
    {
        _updater.Tick(delta,_preferences.AutomaticUpdates,_session==null&&!_connecting);
        _menu.UpdateStatus(_updater.Version,_updater.Status,_updater.Busy,_updater.UpdateReady,_session==null&&!_connecting);
        if(_smoke)
        {
            _elapsed+=delta;
            if(_elapsed>_smokeDuration)
            {
                bool captured=true;
                if(DisplayServer.GetName()!="headless")
                {
                    string folder=ProjectSettings.GlobalizePath(OS.HasFeature("editor")?"res://artifacts":"user://captures");
                    captured=DirAccess.MakeDirRecursiveAbsolute(folder)==Error.Ok
                        &&GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder,_capture+".png"))==Error.Ok;
                }
                bool passed=captured&&(_capture is "menu" or "roster"||SnapshotCount>0);GD.Print($"{(passed?"GODOT_SMOKE_OK":"GODOT_SMOKE_FAILED")} {_capture} snapshots={SnapshotCount}");GetTree().Quit(passed?0:1);return;
            }
        }
        var session=_session;if(session==null)return;
        if(ScreenName=="Match")for(int i=0;i<session.LocalPlayerCount;i++)
        {
            var input=_injected.TryGetValue(i,out var injected)?injected:PlayerControls.Read(i,session.LocalPlayerCount);
            session.SendInput(i,input);
            var playerId=session is NetworkSession network?network.PlayerId:$"local-{i}";
            if(playerId!=null)_arena.SetLocalInput(playerId,input);
        }
        if(session is not LocalSession || ScreenName=="Match")session.Advance(delta);
        foreach(var envelope in session.DrainMessages())
        {
            if(!envelope.TryGetProperty("d",out var data))continue;
            switch(envelope.GetProperty("t").GetString())
            {
                case "stage":LatestStage=data.Clone();_arena.Stage(data);_hud.Stage(data);break;
                case "state":
                    LatestState=data.Clone();SnapshotCount++;ApplyStageLighting();_arena.State(data,envelope.TryGetProperty("k",out var stateTick)&&stateTick.ValueKind==JsonValueKind.Number?stateTick.GetInt64():-1);_hud.State(data);
                    if(ScreenName=="Lobby")EnterMatch();
                    break;
                case "party.status":
                    var party=PartyStatus.Parse(data);_menu.ShowParty(data,(session as NetworkSession)?.PlayerId);
                    if(party.Phase=="playing"&&party.Match!=_partyMatch)
                    {
                        _partyMatch=party.Match;_arena.ResetMatch();_hud.Reset();LatestState=null;LatestStage=null;ControllerHub.Current.RearmControls();
                        EnterMatch();
                    }
                    else if(party.Phase=="lobby"&&ScreenName is "Match" or "Results")ShowPartyLobby();
                    else if(party.Phase=="lobby"&&ScreenName is "Pause" or "Settings")_pauseReturn="Lobby";
                    break;
                case "party.error":_menu.Notice(data.GetProperty("message").GetString()??"Room settings rejected.");break;
                case "party.result":
                    _result=data.Clone();session.ReleaseInputs();ControllerHub.Current.StopVibration();ScreenName="Results";_hud.Visible=false;_menu.SetOverlay(false);_menu.Visible=true;_menu.ShowResults(LatestState,_result,true);break;
                case "appearance":
                    if(data.TryGetProperty("playerId",out var owner)&&data.TryGetProperty("index",out var suit))_menu.SetLobbyAppearance(owner.GetString()!,suit.GetInt32());
                    break;
                case "sys.welcome":
                    if(LatestState!=null)_arena.ResetPresentationBaseline();
                    if(data.TryGetProperty("lobby",out var welcomeLobby)){_lobby=welcomeLobby.Clone();_menu.ShowLobby(_lobby,session.JoinCode,session.Status);}
                    break;
                case "sys.lobby":_lobby=data.Clone();_menu.ShowLobby(_lobby,session.JoinCode,session.Status);break;
                case "sys.ended":
                    _result=data.Clone();ScreenName="Results";_hud.Visible=false;_menu.Visible=true;_menu.ShowResults(LatestState,_result);break;
                case "sys.closed":
                    if(ScreenName!="Results"){ScreenName="Results";_hud.Visible=false;_menu.Visible=true;_menu.ShowResults(LatestState,data);}break;
                case "sys.error":
                    if(data.TryGetProperty("message",out var message))_menu.Notice(message.GetString()??"Connection error.");break;
            }
        }
        _hud.Connection(session.Status,session.JoinCode,_practice);_menu.UpdateConnection(session.Status);
        if(session.IsEnded&&session.Status.StartsWith("Error",StringComparison.Ordinal)&&ScreenName!="Results")
        {ScreenName="Results";_hud.Visible=false;_menu.Visible=true;_menu.ShowResults(LatestState,JsonSerializer.SerializeToElement(new {reason=session.Status}));}
    }
    public override void _ExitTree(){CancelConnection();ReleaseSession();if(PreferencesPath.StartsWith("user://qa-"))DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(PreferencesPath));}
}

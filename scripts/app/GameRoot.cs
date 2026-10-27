using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using System.Text.Json;
using BlastCircuit.Arena;
using BlastCircuit.Arena.Effects;
using BlastCircuit.InputDevices;
using BlastCircuit.Rendering;
using BlastCircuit.UI;
using BlastCircuit.UI.Hud;
using BlastCircuit.UI.Menu;
namespace BlastCircuit.App;

/// <summary>Composition root for screen navigation, session lifetime, controls and snapshot presentation.</summary>
public partial class GameRoot : Node3D
{
    private ArenaView _arena=null!;
    private ArcadeMenu _menu=null!;
    private BattleHud _hud=null!;
    private PerformanceBar _performanceBar=null!;
    private TutorialHud _tutorialHud=null!;
    private string? _tutorialLessonId;
    private bool _trainingSession;
    private BlastCircuit.Audio.BattleAudio _uiAudio=null!;
    private ISession? _session;
    private CancellationTokenSource? _connectCancel;
    private readonly Dictionary<int,PlayerInput> _injected=[];
    private bool _connecting,_practice,_setupForHost,_smoke,_editingRoom;
    private string _capture="gameplay";
    private string? _smokeOutput;
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
    private string _screenName="Modes", _settingsReturn="Modes", _pauseReturn="Match", _menuRoute="Modes";
    private readonly Stack<string> _menuHistory=[];
    private int _partyMatch=-1;
    private int _networkGeneration;
    private PlayerPreferences _preferences=new();
    private GraphicsPreset? _appliedGraphics;
    // The project sets no cap, so a launch-time value came from --max-fps (QA timing) and outranks the saved cap.
    private readonly int _launchMaxFps=Engine.MaxFps;
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
            if(_tutorialHud!=null)_tutorialHud.Visible=_tutorialLessonId is not null && value=="Match";
            _arena?.SetBattleActive(value=="Match");
            // Input role follows the visible screen; lobby/results retain match bindings.
            if(ControllerHub.Current is {} hub)hub.MenuNavigationEnabled=!_preparingGraphics&&value is not ("Match" or "Controls");
        }
    }
    public int SnapshotCount { get; private set; }
    public JsonElement? LatestState { get; private set; }
    public JsonElement? LatestStage { get; private set; }
    public ISession? ActiveSession=>_session;
    public IReadOnlyList<BlastCircuit.Arena.Effects.ThrowPreviewState> ThrowPreviews=>_arena.ThrowPreviews;
    public Vector2 ArenaScreenPoint(System.Numerics.Vector3 arena)=>_arena.ScreenPoint(arena);
    public IReadOnlyList<BlastCircuit.Arena.BombRenderPose> BombRenderPoses=>_arena.BombRenderPoses;
    public object VisualState=>new {waterHeight=_arena.WaterHeight,largestBombScale=_arena.LargestBombScale,pumpAnimations=_arena.PumpAnimationCount,pumpIntensity=_arena.PumpIntensity,carriedBombs=_arena.CarriedBombs,powerups=_arena.VisiblePowerups};
    public JsonElement? LatestLobby=>_lobby;
    public object RosterState=>_menu.RosterState;
    public object PresentationState=>_arena.PresentationState;
    public BlastCircuit.Audio.BattleAudio BattleAudio=>_arena.Audio;
    public int SelectedAppearance=>_menu.OnlineAppearance;
    public void ConfigureRosterSlot(int slot,string role,int appearance)=>_menu.ConfigureRosterSlot(slot,role.ToLowerInvariant() switch {"human"=>RosterRole.Human,"cpu"=>RosterRole.Cpu,"off"=>RosterRole.Off,_=>throw new ArgumentException("Role must be human, cpu or off.")},appearance);
    public void SetOnlineAppearance(int appearance)=>_menu.SetOnlineAppearance(appearance);

    public override async void _Ready()
    {
        var args=OS.GetCmdlineUserArgs();
        _uiAudio=new BlastCircuit.Audio.BattleAudio();AddChild(_uiAudio);
        _arena=new ArenaView();AddChild(_arena);_arena.Visible=false;
        var layer=new CanvasLayer();AddChild(layer);
        _menu=new ArcadeMenu();layer.AddChild(_menu);
        _hud=new BattleHud {Visible=false};layer.AddChild(_hud);
        BuildTutorialHud(layer);
        var metricsLayer=new CanvasLayer {Layer=90};AddChild(metricsLayer);
        _performanceBar=new PerformanceBar {Name="PerformanceBar",Visible=false};metricsLayer.AddChild(_performanceBar);
        _menu.AppearanceChanged+=index=>{if(_session is NetworkSession network)network.SetAppearance(index);};
        _menu.Navigation+=Navigate;_menu.BackRequested+=Back;_menu.ReplayRequested+=Replay;
        _menu.LocalRequested+=StartConfigured;_menu.OnlineRequested+=StartConfiguredOnline;
        _hud.LeaveRequested+=OpenGameMenu;_hud.RestartRequested+=Replay;
        _menu.StartRoomRequested+=()=>{if(_session is NetworkSession network)network.RequestStart();};
        _menu.ResumeRequested+=ResumeGame;
        _menu.SettingsChanged+=SavePreferences;
        _menu.TestVibrationRequested+=()=>_menu.SettingsNotice(ControllerHub.Current.TestVibration()?"Vibration sent to your controller.":_preferences.Vibration?"Connect a controller that supports vibration.":"Enable controller vibration first.");
        if(args.Any(a=>a.StartsWith("--playtest=")))
        {
            // Never key this by PID: a rebooted QA guest reuses PIDs, and a killed
            // run leaves its file behind, so a new process would inherit stale choices.
            PreferencesPath=$"user://qa-preferences-{Guid.NewGuid():N}.cfg";
            // A fresh QA process may reopen a fixture without touching personal settings.
            var id=args.FirstOrDefault(a=>a.StartsWith("--playtest-preferences="))?.Split('=',2)[1];
            if(Guid.TryParseExact(id,"N",out var fixture))PreferencesPath=$"user://qa-preferences-{fixture:N}.cfg";
        }
        if(args.Any(a=>a is "--smoke" or "--smoke-menu"))
        {
            string? fixture=args.FirstOrDefault(a=>a.StartsWith("--smoke-preferences="))?.Split('=',2)[1];
            string? output=args.FirstOrDefault(a=>a.StartsWith("--smoke-output="))?.Split('=',2)[1];
            if(fixture is not null&&Path.IsPathFullyQualified(fixture))PreferencesPath=fixture;
            if(output is not null&&Path.IsPathFullyQualified(output))_smokeOutput=output;
        }
        _preferences=PlayerPreferences.Load(PreferencesPath);_menu.ConfigurePreferences(_preferences);_menu.TutorialProgress(_preferences.CompletedTutorials);ApplyPreferences();
        DisplayModeController.Current.Configure(_preferences.Fullscreen,_preferences.Resolution,startup:true);
        DisplayModeController.Current.Changed+=SaveDisplayShortcut;
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
                await _menu.PrepareArenas();
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
        BlastCircuit.Testing.PlaytestDriver.TryAttach(this);
    }

    // Release startup gates compare these with the saved preferences they launched.
    private string SmokeSettings()=>$"graphics={(_appliedGraphics is {} preset?GraphicsQuality.ToWire(preset):"none")} window={GetWindow().Mode.ToString().ToLowerInvariant()} size={GetWindow().Size.X}x{GetWindow().Size.Y} adapter={RenderingServer.GetVideoAdapterName()}";

    public void Navigate(string screen)=>Navigate(screen,restoreFocus:false);

    private void Navigate(string screen,bool restoreFocus)
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
            _settingsReturn=ScreenName=="Setup"?_menuRoute:ScreenName;ScreenName="Settings";_menu.SetOverlay(_session!=null&&LatestState!=null);_menu.Visible=true;_hud.Visible=false;_menu.ShowScreen("Settings");return;
        }
        _editingRoom=false;
        _menu.SetOverlay(false);
        if(screen is not ("Modes" or "Tutorial" or "Controls" or "Roster" or "Setup" or "Practice" or "HostSetup" or "Online" or "Lobby" or "Match" or "Results"))throw new ArgumentException("Unknown screen.",nameof(screen));
        if(screen is "Modes" or "Tutorial" or "Controls" or "Roster" or "Online" or "Setup" or "Practice" or "HostSetup")
        {
            CancelConnection();
            if(_session!=null)ReleaseSession();
        }
        if(screen!=ScreenName){if(screen=="Modes"||(screen=="Roster"&&ScreenName=="Setup"))_uiAudio.MenuBack();else _uiAudio.MenuConfirm();}
        if(screen=="Modes")_menuHistory.Clear();
        else if(!restoreFocus&&screen!=_menuRoute&&screen is "Tutorial" or "Controls" or "Roster" or "Setup" or "Practice" or "HostSetup" or "Online")
        {
            // Returning through Continue/Save should not build a loop between the same two pages.
            if(_menuHistory.Contains(screen)){while(_menuHistory.Pop()!=screen){}}
            else _menuHistory.Push(_menuRoute);
        }
        if(screen is "Modes" or "Tutorial" or "Controls" or "Roster" or "Setup" or "Practice" or "HostSetup" or "Online")_menuRoute=screen;
        _setupForHost=screen=="HostSetup";
        ControllerHub.Current.TesterActive=screen=="Controls";
        ScreenName=screen=="Practice"||screen=="HostSetup"?"Setup":screen;
        _arena.Visible=ScreenName is "Match" or "Results";
        _hud.Visible=ScreenName=="Match";
        _menu.Visible=ScreenName!="Match";
        if(_menu.Visible)_menu.ShowScreen(screen,restoreFocus);
    }
    public void Back()
    {
        if(ScreenName=="Advanced"){ScreenName="Setup";_menu.CloseAdvanced();return;}
        if(_editingRoom&&ScreenName=="Setup"){_editingRoom=false;ShowPartyLobby(restoreFocus:true);return;}
        if(ScreenName=="Settings"){RestoreScreen(_settingsReturn);return;}
        if(ScreenName=="Pause"){ResumeGame();return;}
        if(_connecting){CancelConnection();_menu.SetBusy(false,"Connection cancelled.");ScreenName="Online";_menu.ShowScreen("Online",restoreFocus:true);return;}
        if(_session!=null)
        {
            string target=_trainingSession?"Tutorial":_session is NetworkSession&&ScreenName is "Lobby" or "Results"?"Online":"Modes";
            ReleaseSession();Navigate(target,restoreFocus:true);return;
        }
        Navigate(_menuHistory.TryPop(out var previous)?previous:"Modes",restoreFocus:true);
    }
    public override void _Input(InputEvent @event)
    {
        if(@event is InputEventKey {Pressed:true,Echo:false,Keycode:Key.F3,AltPressed:false,CtrlPressed:false,MetaPressed:false,ShiftPressed:false})
        {
            _menu.ConfigurePerformanceMetrics(!_preferences.PerformanceMetrics);SavePreferences();
            GetViewport().SetInputAsHandled();return;
        }
        // LineEdit consumes Escape itself. Back still leaves one menu in a single press.
        if(!_preparingGraphics&&!_menu.HasOpenPopup&&GetViewport().GuiGetFocusOwner() is LineEdit
            &&@event.IsActionPressed("ui_cancel"))
        {Back();GetViewport().SetInputAsHandled();}
    }
    public override void _UnhandledInput(InputEvent @event)
    {
        if(_preparingGraphics||_menu.HasOpenPopup)return;
        bool start=@event is InputEventJoypadButton {Pressed:true,ButtonIndex:JoyButton.Start};
        bool cancel=@event is InputEventKey {Pressed:true,Echo:false,Keycode:Key.Escape}
            || (ScreenName!="Controls"&&(ScreenName!="Match"||_tutorialLessonId is not null&&_tutorialHud.Finished)&&@event.IsActionPressed("ui_cancel"));
        if(@event is InputEventJoypadButton {Pressed:true,ButtonIndex:JoyButton.LeftShoulder or JoyButton.RightShoulder} shoulder
            &&ScreenName is not ("Match" or "Controls")&&_menu.SwitchColumn(shoulder.ButtonIndex==JoyButton.LeftShoulder?-1:1))
        {GetViewport().SetInputAsHandled();return;}
        if(start)
        {
            if(ScreenName=="Match"&&_tutorialHud.Finished&&_tutorialLessonId is not null)_tutorialHud.FocusAction();
            else if(ScreenName=="Match")OpenGameMenu();
            else {if(ScreenName=="Controls")ControllerHub.Current.TesterActive=false;_menu.FocusStart();}
            GetViewport().SetInputAsHandled();return;
        }
        if(cancel)
        {
            if(ScreenName=="Match"&&_tutorialHud.Finished&&_tutorialLessonId is not null)Navigate("Tutorial",restoreFocus:true);
            else if(ScreenName=="Match")OpenGameMenu();else Back();
            GetViewport().SetInputAsHandled();
        }
    }
    private void OpenGameMenu()
    {
        if(_session==null)return;
        _pauseReturn=ScreenName;_session.ReleaseInputs();_injected.Clear();ControllerHub.Current.RearmControls();ControllerHub.Current.StopVibration();
        ScreenName="Pause";_hud.Visible=false;_menu.SetOverlay(LatestState!=null);_menu.Visible=true;_menu.ShowPause(_session is NetworkSession,_trainingSession,_tutorialLessonId);
    }
    private void ResumeGame(){_session?.ReleaseInputs();ControllerHub.Current.RearmControls();RestoreScreen(_pauseReturn);}
    private void RestoreScreen(string screen)
    {
        if(screen=="Pause"){ScreenName="Pause";_menu.Visible=true;_menu.SetOverlay(LatestState!=null);_menu.ShowPause(_session is NetworkSession,_trainingSession,_tutorialLessonId,restoreFocus:true);return;}
        if(screen=="Match"&&_session!=null){EnterMatch();return;}
        if(screen=="Lobby"&&_session!=null){ShowPartyLobby(restoreFocus:true);return;}
        Navigate(screen,restoreFocus:true);
    }
    private void ShowPartyLobby(bool restoreFocus=false)
    {
        _session?.ReleaseInputs();ControllerHub.Current.StopVibration();ControllerHub.Current.RearmControls();
        ScreenName="Lobby";_arena.Visible=false;_hud.Visible=false;_menu.SetOverlay(false);_menu.Visible=true;_menu.ShowScreen("Lobby",restoreFocus);
    }
    /// <summary>Adds a right-stick offset or mouse/trackpad point for a seat's carried bomb.</summary>
    private PlayerInput WithPointer(PlayerInput input,int seat,string? playerId)
    {
        var hub=ControllerHub.Current;
        var offset=hub.AimStickOffset(seat);
        if(playerId==null||!_arena.LocalCarrier(playerId,out _)){hub.MouseAimActive(seat,false);return input;}
        if(offset is {} stick)return input with {AimTarget=stick,AimRelative=true};
        return hub.MouseAimActive(seat,true)?input with {AimTarget=_arena.ArenaPoint(GetViewport().GetMousePosition())}:input;
    }
    private bool _matchCursor;
    /// <summary>In matches the pointer is a crosshair while the mouse is in use and hides when idle,
    /// so keyboard and controller players are not left with an arrow over the arena.</summary>
    private void UpdateMatchCursor()
    {
        bool match=ScreenName=="Match"&&_session!=null;
        if(match!=_matchCursor){_matchCursor=match;Input.SetDefaultCursorShape(match?Input.CursorShape.Cross:Input.CursorShape.Arrow);}
        var mode=match&&ControllerHub.Current.HasApplicationFocus&&ControllerHub.Current.MouseIdleSeconds>2?Input.MouseModeEnum.Hidden:Input.MouseModeEnum.Visible;
        if(Input.MouseMode!=mode)Input.MouseMode=mode;
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
                    string folder=_smokeOutput??ProjectSettings.GlobalizePath(OS.HasFeature("editor")?"res://artifacts":"user://captures");
                    captured=DirAccess.MakeDirRecursiveAbsolute(folder)==Error.Ok
                        &&GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder,_capture+".png"))==Error.Ok;
                }
                bool passed=captured&&(_capture is "menu" or "roster"||SnapshotCount>0);GD.Print($"{(passed?"GODOT_SMOKE_OK":"GODOT_SMOKE_FAILED")} {_capture} snapshots={SnapshotCount} {SmokeSettings()}");GetTree().Quit(passed?0:1);return;
            }
        }
        UpdateMatchCursor();
        var session=_session;if(session==null)return;
        if(session is NetworkSession reconnecting&&reconnecting.ConnectionGeneration!=_networkGeneration)
        {_networkGeneration=reconnecting.ConnectionGeneration;session.ReleaseInputs();ControllerHub.Current.RearmControls();}
        if(ScreenName=="Match" && !(_tutorialLessonId is not null && _tutorialHud.Finished))for(int i=0;i<session.LocalPlayerCount;i++)
        {
            var playerId=session is NetworkSession network?network.PlayerId:$"local-{i}";
            var input=_injected.TryGetValue(i,out var injected)?injected:WithPointer(PlayerControls.Read(i,session.LocalPlayerCount),i,playerId);
            session.SendInput(i,input);
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
                    LatestState=data.Clone();SnapshotCount++;ApplyStageLighting();_arena.State(data,envelope.TryGetProperty("k",out var stateTick)&&stateTick.ValueKind==JsonValueKind.Number?stateTick.GetInt64():-1);_hud.State(data);TutorialState(data);
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
        string connection=session is NetworkSession {Party.Hosting:"peer"} peerSession && session.Status=="Connected" ? peerSession.PeerTransport : session.Status;
        _hud.Connection(connection,session.JoinCode,_practice);_menu.UpdateConnection(session.Status);
        if(session.IsEnded&&session.Status.StartsWith("Error",StringComparison.Ordinal)&&ScreenName!="Results")
        {ScreenName="Results";_hud.Visible=false;_menu.Visible=true;_menu.ShowResults(LatestState,JsonSerializer.SerializeToElement(new {reason=session.Status}));}
    }
    public override void _ExitTree(){DisplayModeController.Current.Changed-=SaveDisplayShortcut;CancelConnection();ReleaseSession();if(PreferencesPath.StartsWith("user://qa-"))DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(PreferencesPath));}
}

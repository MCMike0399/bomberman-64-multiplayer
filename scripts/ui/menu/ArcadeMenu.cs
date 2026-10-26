using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Matches;
using Bomber.Game.Tutorial;
using System.Text.Json;
using BlastCircuit.Arena;
using BlastCircuit.InputDevices;
using BlastCircuit.Rendering;
namespace BlastCircuit.UI.Menu;

/// <summary>Mode, setup, connection, lobby and result screens; emits intent to the composition root.</summary>
public partial class ArcadeMenu : Control
{
    public event Action<string>? Navigation;
    public event Action<string>? TutorialRequested;
    private readonly Dictionary<string,Button> _lessonButtons=[];
    private readonly HashSet<string> _completedLessons=[];
    private TutorialLesson _selectedLesson=TutorialCourse.Lessons[0];
    private Label _lessonTitle=null!,_lessonCopy=null!,_lessonTip=null!,_lessonObjectives=null!,_lessonProgress=null!;
    private Button _lessonStart=null!;
    public event Action<int>? AppearanceChanged;
    private readonly Dictionary<string,int> _lobbyAppearances=[];
    private readonly Dictionary<string,CharacterPreview> _lobbyPreviews=[];
    public event Action? LocalRequested, ReplayRequested, BackRequested, StartRoomRequested, ResumeRequested, SettingsChanged, TestVibrationRequested, CheckUpdatesRequested, InstallUpdateRequested;
    private Button _retryLesson=null!,_pauseLeave=null!;
    private string? _pausedTutorial;
    private Button _startRoom=null!,_copyCode=null!,_roomRules=null!;
    private string? _roomCode;
    public event Action? IdentityChanged;
    private ArcadeModeButton _replay=null!;
    private Label _partyHint=null!,_pauseHint=null!,_settingsNotice=null!,_volumeLabel=null!;
    private CheckButton _vibration=null!;
    private HSlider _volume=null!;
    private CheckButton _automaticUpdates=null!;
    private Label _updateStatus=null!;
    private Button _checkUpdates=null!,_installUpdate=null!,_settingsButton=null!;
    public bool AutomaticUpdates=>_automaticUpdates.ButtonPressed;
    private JsonElement? _party;
    private string? _localPlayer;
    public bool VibrationEnabled=>_vibration.ButtonPressed;
    public float SoundVolume=>(float)_volume.Value/100;
    public StageTime LocalStageTime {get;private set;}=StageTime.Midday;
    public event Action<NetworkMode>? OnlineRequested;
    private Control _content=null!;
    private ArcadeBackdrop _background=null!;
    private Label _notice=null!,_lobbyStatus=null!,_joinCode=null!;
    private GridContainer _roster=null!;
    private OptionButton _stage=null!,_difficulty=null!;
    private Button _launch=null!;
    private Label _setupTitle=null!,_stageDescription=null!;
    private readonly List<ArcadeRuleDial> _ruleDials=[];
    private ArcadeRuleDial _stageDial=null!;
    private SpinBox _bots=null!,_wins=null!,_rounds=null!;
    private RosterSelection _localPlayers=null!;
    private Label _rosterSummary=null!;
    private Control _hostBotBox=null!;
    private Button _editPlayers=null!;
    private ArcadeRuleDial _onlineAppearanceButton=null!;
    private int _hostingPreference;
    private StagePreview? _onlineStage;
    private VBoxContainer _standingsBoard=null!;
    private float _standingsRowHeight;
    private CharacterPreview _onlinePreview=null!;
    private CharacterPreview _winnerPreview=null!;
    private VBoxContainer _resultRows=null!;
    private Label _resultTitle=null!;
    private int _onlineAppearance;
    private CheckButton _sudden=null!,_ghosts=null!;
    private LineEdit _server=null!,_name=null!,_code=null!;
    private readonly Dictionary<string,Control> _screens=[];
    private bool _practice;
    private AdvancedRules _advanced=new();
    private string _advancedReturn="Setup";
    private string _screen="Modes";
    private readonly Dictionary<string,FocusLayout> _focusLayouts=[];
    private readonly Dictionary<string,Control> _savedFocus=[];
    private readonly Dictionary<string,int> _activeColumn=[];
    private readonly Dictionary<(string,int),Control> _columnFocus=[];
    private string _page="Modes";
    private Control? _connectionFocus;
    private int _focusRevision;

    private DateTimeOffset? _countdown;
    private readonly List<Button> _onlineButtons=[];
    private PromptBar _inputFooter=null!;
    public int HumanCount => _practice?1:_localPlayers.Roster.Humans;
    public int[] PlayerAppearances => _practice?new[]{_localPlayers.Roster.AuthorityAppearances().FirstOrDefault()}:_localPlayers.Roster.AuthorityAppearances();
    public object RosterState=>_localPlayers.DiagnosticState;
    public int OnlineAppearance=>_onlineAppearance;
    public string ServerUrl=>_server.Text.Trim();
    public string PlayerName=>_name.Text.Trim();
    public string Code=>_code.Text.Trim();
    public bool Practice=>_practice;
    public SessionSettings Settings => BuildSettings(_practice?0:_localPlayers.Roster.CpuPlayers);
    public SessionSettings OnlineSettings=>BuildSettings((int)_bots.Value);
    private SessionSettings BuildSettings(int bots)=>new(StageIds[_stage.Selected],bots,(int)_wins.Value,(int)_rounds.Value,_sudden.ButtonPressed,_ghosts.ButtonPressed,new[]{"easy","normal","hard"}[_difficulty.Selected]){Advanced=_advanced,Hosting=new[]{"auto","server","peer"}[_hostingPreference]};
    private static readonly string[] StageIds=["sunken-temple","rock-garden","up-and-down","top-rules","blizzard-battle","greedy-trap"];
    private static readonly Color Coral=new("f06a55"),Lagoon=new("2a8c92");
    public override void _Ready()
    {
        Theme=ArcadeTheme.Create();SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _background=new ArcadeBackdrop();AddChild(_background);_background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _content=new Control();AddChild(_content);_content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildModes();BuildTutorial();BuildControls();BuildRoster();BuildSetup();BuildAdvanced();BuildOnline();BuildLobby();BuildResults();BuildSettings();BuildPause();
        var footer=new HBoxContainer {AnchorTop=1,AnchorBottom=1,AnchorRight=1,OffsetTop=-60,OffsetBottom=-12,OffsetLeft=58,OffsetRight=-58,Alignment=BoxContainer.AlignmentMode.Center};AddChild(footer);
        _inputFooter=new PromptBar();footer.AddChild(_inputFooter);UpdateInputFooter();ControllerHub.Current.PromptChanged+=UpdateInputFooter;
        var spacer=new Control {SizeFlagsHorizontal=SizeFlags.ExpandFill};footer.AddChild(spacer);
        ArcadeTheme.Text(footer,"Burbuja",15,ArcadeTheme.Muted).VerticalAlignment=VerticalAlignment.Center;
        ShowScreen("Modes");
    }
    private void UpdateInputFooter(){_inputFooter.SetPrompt(ControllerProfile.Menu(ControllerHub.Current.ActiveFamily, _focusLayouts.TryGetValue(_screen,out var layout)&&layout.Columns.Length>1));RefreshTutorialPrompts();}
    public override void _ExitTree(){ControllerHub.Current.PromptChanged-=UpdateInputFooter;}
    private Control Screen(string name)
    {var control=new Control();_content.AddChild(control);control.AnchorLeft=control.AnchorRight=.5f;control.AnchorTop=control.AnchorBottom=.5f;control.OffsetLeft=-720;control.OffsetRight=720;control.OffsetTop=-450;control.OffsetBottom=450;_screens[name]=control;return control;}
    private static VBoxContainer Card(Node parent,Vector2 position,Vector2 size)
    {
        var panel=new PanelContainer {Position=position,Size=size};panel.AddThemeStyleboxOverride("panel",ArcadeTheme.Plate());parent.AddChild(panel);return ArcadeTheme.Column(panel,14);
    }
    private static Label Section(Node parent,string text)=>ArcadeTheme.Heading(parent,text,24);
    private static Label Copy(Node parent,string text,int size=16)
    {var label=ArcadeTheme.Text(parent,text,size);label.AutowrapMode=TextServer.AutowrapMode.WordSmart;return label;}
    /// <summary>Secondary explanatory copy: wrapped, smaller and muted so it never competes with controls.</summary>
    private static Label Hint(Node parent,string text,int size=16)
    {var label=Copy(parent,text,size);label.AddThemeColorOverride("font_color",ArcadeTheme.Muted);return label;}
    private static MarginContainer Inset(Node parent,int left,int top=0)
    {
        var margin=new MarginContainer();margin.AddThemeConstantOverride("margin_left",left);margin.AddThemeConstantOverride("margin_top",top);
        parent.AddChild(margin);return margin;
    }
    private VBoxContainer _results=null!;
    public override void _Process(double delta)
    {
        if(_screen=="Results"&&_standingsBoard.IsVisibleInTree())FitStandings();
    }
    public void ShowScreen(string screen,bool restoreFocus=false)
    {
        _background.Visible=screen is not ("Pause" or "Settings") || !_overlayBackground;
        _background.Page=screen;_background.Quiet=screen=="Roster";_background.Results=screen=="Results";_background.QueueRedraw();
        _page=screen;
        _screen=screen=="Practice"||screen=="HostSetup"?"Setup":screen;
        if(screen!="Advanced")_practice=screen=="Practice";
        _setupTitle.Text=_practice?"Practice Setup":screen=="HostSetup"?"Private Room Settings":"Match Setup";
        _launch.Text=screen=="HostSetup"?"Save Room Settings":_practice?"Start Practice":"Start Battle";
        foreach(var dial in _ruleDials)dial.Disabled=_practice;
        _difficulty.Disabled=_practice;_bots.Editable=!_practice;_wins.Editable=!_practice;_rounds.Editable=!_practice;_sudden.Disabled=_practice;_ghosts.Disabled=_practice;
        _hostBotBox.Visible=screen=="HostSetup";_editPlayers.Visible=screen=="Setup";
        int humans=_localPlayers.Roster.Humans,cpu=_localPlayers.Roster.CpuPlayers;
        _rosterSummary.Text=_practice?"One bomber, free training":screen=="HostSetup"?"Online room: you plus CPU rivals":$"{_localPlayers.Roster.Participants} bombers: {humans} {(humans==1?"human":"humans")}, {cpu} CPU";
        if(_practice)_notice.Text="Collect the training power-ups. There is no timer and no win target.";
        else if(screen=="Setup")_notice.Text="Your roster and suits carry into the battle. The last bomber standing takes the round.";
        else if(screen=="HostSetup")_notice.Text="These settings apply to the server. Each online player controls one bomber.";
        if(screen=="Roster"){_localPlayers.BotDifficulty=_difficulty.Selected+1;_localPlayers.Refresh();}
        foreach(var pair in _screens)pair.Value.Visible=pair.Key==_screen;
        int focusRevision=++_focusRevision;
        Callable.From(()=>{if(IsVisibleInTree()&&_focusRevision==focusRevision)FocusScreen(restoreFocus);}).CallDeferred();
        UpdateInputFooter();
    }
    /// <summary>Screens whose first button in tree order is Back/Leave start on their first real choice instead.</summary>
    private Control? DefaultFocus(string screen)=>screen switch
    {
        "Tutorial"=>_lessonButtons[_selectedLesson.Id],
        "Roster"=>_screens["Roster"].FindChild("Slot0Role",true,false) as Control,
        "Setup"=>_stageDial,
        "Settings"=>_volume,
        "Online"=>_screens["Online"].FindChild("CancelConnect",true,false) is Control cancel&&CanFocus(cancel)?cancel:_onlineAppearanceButton,
        "Lobby"=>_startRoom.Visible&&!_startRoom.Disabled?_startRoom:!_copyCode.Disabled?_copyCode:_screens["Lobby"].FindChild("LobbySettings",true,false) as Control,
        _=>null
    };
    public void Notice(string message)
    {
        _notice.Text=message;
        var node=_screens["Online"].FindChild("OnlineNotice",true,false) as Label;if(node!=null)node.Text=message;
    }
    public void SetBusy(bool busy,string? status=null)
    {
        if(busy&&_screen=="Online")_connectionFocus=GetViewport().GuiGetFocusOwner();
        foreach(var button in _onlineButtons)button.Disabled=busy;
        var cancel=_screens["Online"].FindChild("CancelConnect",true,false) as Button;if(cancel!=null)cancel.Visible=busy;
        RefreshNavigation();
        if(busy&&_screen=="Online")FocusStart();
        else if(_screen=="Online"&&CanFocus(_connectionFocus))_connectionFocus!.GrabFocus();
        if(status is not null)Notice(status);
        else if(!busy&&_screens["Online"].FindChild("OnlineNotice",true,false) is Label notice&&
            (notice.Text.StartsWith("Connecting",StringComparison.Ordinal)||notice.Text.StartsWith("Searching for players",StringComparison.Ordinal)))
            Notice("Choose a room or find other players.");
    }
    private bool _overlayBackground;
    public GraphicsPreset Graphics {get;private set;}=GraphicsPreset.Medium;
    private Label _graphicsDescription=null!;
    public void SetOverlay(bool overlay)=>_overlayBackground=overlay;
}

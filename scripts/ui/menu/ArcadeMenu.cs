using Godot;
using Bomber.Client;
using Bomber.Game;
using System.Text.Json;
using BlastCircuit.App;
using BlastCircuit.Arena;
using BlastCircuit.InputDevices;
using BlastCircuit.Rendering;
namespace BlastCircuit.UI.Menu;

/// <summary>Mode, setup, connection, lobby and result screens; emits intent to the composition root.</summary>
public partial class ArcadeMenu : Control
{
    public event Action<string>? Navigation;
    public event Action<int>? AppearanceChanged;
    private readonly Dictionary<string,int> _lobbyAppearances=[];
    private readonly Dictionary<string,CharacterPreview> _lobbyPreviews=[];
    public event Action? LocalRequested, ReplayRequested, BackRequested, StartRoomRequested, ResumeRequested, SettingsChanged, TestVibrationRequested, CheckUpdatesRequested, InstallUpdateRequested;
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
    private SessionSettings BuildSettings(int bots)=>new(StageIds[_stage.Selected],bots,(int)_wins.Value,(int)_rounds.Value,_sudden.ButtonPressed,_ghosts.ButtonPressed,new[]{"easy","normal","hard"}[_difficulty.Selected]){Advanced=_advanced};
    private static readonly string[] StageIds=["sunken-temple","rock-garden","up-and-down"];
    private static readonly Color Coral=new("f06a55"),Lagoon=new("2a8c92");
    public override void _Ready()
    {
        Theme=ArcadeTheme.Create();SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _background=new ArcadeBackdrop();AddChild(_background);_background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _content=new Control();AddChild(_content);_content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildModes();BuildControls();BuildRoster();BuildSetup();BuildAdvanced();BuildOnline();BuildLobby();BuildResults();BuildSettings();BuildPause();
        var footer=new HBoxContainer {AnchorTop=1,AnchorBottom=1,AnchorRight=1,OffsetTop=-60,OffsetBottom=-12,OffsetLeft=58,OffsetRight=-58,Alignment=BoxContainer.AlignmentMode.Center};AddChild(footer);
        _inputFooter=new PromptBar();footer.AddChild(_inputFooter);UpdateInputFooter();ControllerHub.Current.PromptChanged+=UpdateInputFooter;
        var spacer=new Control {SizeFlagsHorizontal=SizeFlags.ExpandFill};footer.AddChild(spacer);
        ArcadeTheme.Text(footer,"Burbuja",15,ArcadeTheme.Muted).VerticalAlignment=VerticalAlignment.Center;
        ShowScreen("Modes");
    }
    private void UpdateInputFooter()=>_inputFooter.SetPrompt(ControllerProfile.Menu(ControllerHub.Current.ActiveFamily));
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
    private void BuildModes()
    {
        var screen=Screen("Modes");
        // The wordmark tilts slightly, with a lit bomb hanging off "Blast" as its one moving part.
        var wordmark=new Control {Position=new(104,78),Rotation=-.045f,MouseFilter=MouseFilterEnum.Ignore};screen.AddChild(wordmark);
        var title=ArcadeTheme.Heading(wordmark,"Blast\nCircuit",100);title.AddThemeConstantOverride("line_spacing",-30);
        float blastWidth=ArcadeTheme.Display.GetStringSize("Blast",HorizontalAlignment.Left,-1,100).X;
        wordmark.AddChild(new LitBomb {Position=new(blastWidth+4,-14),Size=new(116,116)});
        var menu=new VBoxContainer {Position=new(748,229),Size=new(560,500)};menu.AddThemeConstantOverride("separation",9);screen.AddChild(menu);
        Mode(menu,"Local Battle",()=>Navigation?.Invoke("Roster"),Coral,ArcadeTheme.Gold);
        Mode(menu,"Online Battle",()=>Navigation?.Invoke("Online"),Lagoon,ArcadeTheme.Cream);
        Mode(menu,"Practice",()=>Navigation?.Invoke("Practice"),ArcadeTheme.Indigo,ArcadeTheme.Pink);
        var controls=ArcadeTheme.Quiet(ArcadeTheme.Button(menu,"Controls",()=>Navigation?.Invoke("Controls")));controls.Name="OpenControls";controls.CustomMinimumSize=new(0,48);
        controls.SizeFlagsHorizontal=SizeFlags.ShrinkCenter;
        var settings=ArcadeTheme.Quiet(ArcadeTheme.Button(menu,"Settings",()=>Navigation?.Invoke("Settings")));settings.Name="OpenSettings";settings.CustomMinimumSize=new(0,48);settings.SizeFlagsHorizontal=SizeFlags.ShrinkCenter;_settingsButton=settings;
    }
    private void BuildControls()
    {
        var screen=Screen("Controls");var box=Card(screen,new(64,54),new(1312,746));
        var panel=new ControllerPanel();box.AddChild(panel);panel.BackRequested+=()=>BackRequested?.Invoke();
    }
    private static ArcadeModeButton Mode(Node parent,string title,Action action,Color face,Color accent)
    {
        var button=new ArcadeModeButton {Text=title,CustomMinimumSize=new(0,116),FaceColor=face,Accent=accent};
        button.Pressed+=action;parent.AddChild(button);return button;
    }
    private void BuildRoster()
    {
        var screen=Screen("Roster");var panel=new PanelContainer {Position=new(64,54),Size=new(1312,746)};panel.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(new Color(0,0,0,0),ArcadeTheme.Ink,0,0,12));screen.AddChild(panel);
        _localPlayers=new RosterSelection();panel.AddChild(_localPlayers);_localPlayers.LivePose=_background.Pose;_localPlayers.LineupChanged+=(slot)=>_background.ConfigureRoster(_localPlayers.Roster,slot);_localPlayers.ContinueRequested+=()=>Navigation?.Invoke("Setup");_localPlayers.BackRequested+=()=>BackRequested?.Invoke();
    }
    public void ConfigureRosterSlot(int slot,RosterRole role,int appearance)=>_localPlayers.Configure(slot,role,appearance);
    public void SetOnlineAppearance(int appearance)
    {
        if(appearance is <0 or >7)throw new ArgumentOutOfRangeException(nameof(appearance));
        _onlineAppearance=appearance;_onlinePreview.SetAppearance(appearance);
        AppearanceChanged?.Invoke(appearance);
    }
    private void BuildSetup()
    {
        var screen=Screen("Setup");
        _setupTitle=ArcadeTheme.Heading(screen,"Match Setup",48);_setupTitle.Position=new(80,58);
        var back=ArcadeTheme.Quiet(ArcadeTheme.Button(screen,"Back",()=>BackRequested?.Invoke()));back.Name="SetupBack";back.Position=new(1195,73);back.Size=new(140,48);
        // These native settings nodes remain the single source of truth for configure/API/harness callers.
        var state=new VBoxContainer {Name="SetupSettings",Visible=false};screen.AddChild(state);
        _stage=new OptionButton {Name="Stage"};foreach(var title in new[]{"Sunken Temple","Rock Garden","Up & Down"})_stage.AddItem(title);state.AddChild(_stage);
        _wins=new SpinBox {Name="Wins",MinValue=1,MaxValue=5,Value=2};state.AddChild(_wins);
        _bots=new SpinBox {Name="Bots",MinValue=0,MaxValue=7,Value=3};state.AddChild(_bots);
        _rounds=new SpinBox {Name="RoundSeconds",MinValue=60,MaxValue=300,Step=30,Value=120};state.AddChild(_rounds);
        _difficulty=new OptionButton {Name="Difficulty"};foreach(var title in new[]{"Easy","Medium","Hard"})_difficulty.AddItem(title);_difficulty.Select(1);state.AddChild(_difficulty);
        _sudden=new CheckButton {Name="SuddenDeath",ButtonPressed=true};state.AddChild(_sudden);
        _ghosts=new CheckButton {Name="Ghosts",ButtonPressed=true};state.AddChild(_ghosts);
        _stageDial=new ArcadeRuleDial {Name="StageSelector",Position=new(80,602),Size=new(640,76),Caption="Arena",ValueWidth=300,ReadValue=()=>_stage.GetItemText(_stage.Selected),ChangeValue=direction=>{_stage.Select((_stage.Selected+direction+3)%3);UpdateStageDescription();}};
        screen.AddChild(_stageDial);
        _stageDescription=ArcadeTheme.Text(screen,"",18);_stageDescription.Position=new(104,694);_stageDescription.Size=new(620,70);
        _stage.ItemSelected+=_=>UpdateStageDescription();
        _rosterSummary=ArcadeTheme.Text(screen,"",18,ArcadeTheme.Muted);_rosterSummary.Position=new(822,140);
        var rail=new VBoxContainer {Position=new(800,184),Size=new(535,415)};rail.AddThemeConstantOverride("separation",3);screen.AddChild(rail);
        Dial(rail,"Wins to victory",()=>((int)_wins.Value).ToString(),direction=>_wins.Value=Cycle((int)_wins.Value,direction,1,5));
        var roundTime=Dial(rail,"Normal + sudden",()=>_sudden.ButtonPressed?$@"{TimeSpan.FromSeconds(_rounds.Value-60):m\:ss} + 1:00":TimeSpan.FromSeconds(_rounds.Value).ToString(@"m\:ss"),direction=>_rounds.Value=Cycle((int)_rounds.Value,direction*30,60,300));
        roundTime.Name="Roundtime";_sudden.Toggled+=enabled=>roundTime.Caption=enabled?"Normal + sudden":"Round time";
        Dial(rail,"CPU level",()=>_difficulty.GetItemText(_difficulty.Selected),direction=>_difficulty.Select((_difficulty.Selected+direction+3)%3));
        var bots=Dial(rail,"CPU rivals",()=>((int)_bots.Value).ToString(),direction=>_bots.Value=Cycle((int)_bots.Value,direction,0,7));_hostBotBox=bots;
        Dial(rail,"Sudden death",()=>_sudden.ButtonPressed?"On":"Off",_=>_sudden.ButtonPressed=!_sudden.ButtonPressed);
        Dial(rail,"Ghost comeback",()=>_ghosts.ButtonPressed?"On":"Off",_=>_ghosts.ButtonPressed=!_ghosts.ButtonPressed);
        _editPlayers=ArcadeTheme.Quiet(ArcadeTheme.Button(rail,"Change players",()=>Navigation?.Invoke("Roster")));_editPlayers.Name="EditPlayers";_editPlayers.CustomMinimumSize=new(0,43);_editPlayers.AddThemeFontSizeOverride("font_size",16);
        _editPlayers.SizeFlagsHorizontal=SizeFlags.ShrinkCenter;
        var advanced=ArcadeTheme.Quiet(ArcadeTheme.Button(rail,"Advanced settings",()=>Navigation?.Invoke("Advanced")));advanced.Name="AdvancedSettings";advanced.CustomMinimumSize=new(0,43);advanced.AddThemeFontSizeOverride("font_size",16);
        _notice=ArcadeTheme.Text(screen,"",16);_notice.Position=new(824,625);_notice.Size=new(490,58);_notice.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        _launch=new ArcadeModeButton {Name="LaunchMatch",Text="Start Battle",Position=new(797,699),Size=new(550,86),FaceColor=Coral,Accent=ArcadeTheme.Gold};screen.AddChild(_launch);_launch.Pressed+=()=>LocalRequested?.Invoke();
        UpdateStageDescription();
    }
    private void BuildAdvanced()
    {
        var screen=Screen("Advanced");var box=Card(screen,new(64,42),new(1312,758));
        ArcadeTheme.Heading(box,"Advanced match settings",38);
        Copy(box,"Each destroyed block rolls independently. Uncollected items disappear after their lifetime.",18);
        var columns=ArcadeTheme.Row(box,40);columns.SizeFlagsVertical=SizeFlags.ExpandFill;
        var rules=ArcadeTheme.Column(columns,4);rules.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var items=ArcadeTheme.Column(columns,10);items.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        void Rule(Node parent,string name,Func<string> read,Action<int> change)
        {parent.AddChild(new ArcadeRuleDial{Name=name.Replace(" ",""),Caption=name,ReadValue=read,ChangeValue=change,ValueWidth=125});}
        Rule(rules,"Item drop chance",()=>$"{_advanced.DropPercent}%",d=>_advanced=_advanced with{DropPercent=Cycle(_advanced.DropPercent,d*5,0,100)});
        Rule(rules,"Block amount",()=>$"{_advanced.BlockPercent}%",d=>_advanced=_advanced with{BlockPercent=Cycle(_advanced.BlockPercent,d*5,0,100)});
        Rule(rules,"Item lifetime",()=>$"{_advanced.ItemSeconds}s",d=>_advanced=_advanced with{ItemSeconds=Cycle(_advanced.ItemSeconds,d*5,5,60)});
        Rule(rules,"Blast stack cap",()=>_advanced.MaxFire.ToString(),d=>_advanced=_advanced with{MaxFire=Cycle(_advanced.MaxFire,d,1,5)});
        Rule(rules,"Bomb stack cap",()=>_advanced.MaxBombs.ToString(),d=>_advanced=_advanced with{MaxBombs=Cycle(_advanced.MaxBombs,d,1,8)});
        Rule(rules,"Buff / debuff duration",()=>$"{_advanced.EffectSeconds}s",d=>_advanced=_advanced with{EffectSeconds=Cycle(_advanced.EffectSeconds,d*5,5,30)});
        // Notes line up with the dial captions, not the dial hit area.
        Hint(Inset(rules,22,12),"Tiny: smaller and faster. Big: larger and slower. Haste and Slow change movement speed only. A new effect replaces the previous one.");
        Section(items,"Allowed items");
        var grid=new GridContainer{Columns=2};grid.AddThemeConstantOverride("h_separation",14);grid.AddThemeConstantOverride("v_separation",10);items.AddChild(grid);
        foreach(var type in AdvancedRules.ItemTypes)
        {
            string label=type switch{"fire"=>"Blast up","bomb"=>"Bomb up","remote"=>"Remote","red"=>"Power bomb","bounce"=>"Bounce","fast"=>"Fast bomb","heart"=>"Heart","focus"=>"Focus","tiny"=>"Tiny","big"=>"Big","haste"=>"Haste",_=>"Slow"};
            var toggle=new ArcadeToggle{Name="Allow_"+type,Text=label,CustomMinimumSize=new(0,52),SizeFlagsHorizontal=SizeFlags.ExpandFill,ButtonPressed=true};grid.AddChild(toggle);
            toggle.Toggled+=enabled=>{var disabled=AdvancedRules.ItemTypes.Where(t=>t==type?!enabled:!_advanced.Allows(t));_advanced=_advanced with{DisabledItems=string.Join(',',disabled)};};
        }
        Hint(Inset(items,0,4),"Disabled items cannot appear in blocks, training pickups or equipment drops. With every item off, blocks drop nothing.");
        var actions=ArcadeTheme.Row(box,18);
        var reset=ArcadeTheme.Quiet(ArcadeTheme.Button(actions,"Restore defaults",()=>{_advanced=new();foreach(string type in AdvancedRules.ItemTypes)((CheckButton)_screens["Advanced"].FindChild("Allow_"+type,true,false)).SetPressedNoSignal(true);}));
        reset.Name="ResetAdvanced";reset.CustomMinimumSize=new(240,52);
        actions.AddChild(new Control {SizeFlagsHorizontal=SizeFlags.ExpandFill});
        var back=ArcadeTheme.Button(actions,"Back to match settings",()=>BackRequested?.Invoke());back.Name="AdvancedBack";back.CustomMinimumSize=new(420,52);
    }
    /// <summary>Secondary explanatory copy: wrapped, smaller and muted so it never competes with controls.</summary>
    private static Label Hint(Node parent,string text,int size=16)
    {var label=Copy(parent,text,size);label.AddThemeColorOverride("font_color",ArcadeTheme.Muted);return label;}
    private static MarginContainer Inset(Node parent,int left,int top=0)
    {
        var margin=new MarginContainer();margin.AddThemeConstantOverride("margin_left",left);margin.AddThemeConstantOverride("margin_top",top);
        parent.AddChild(margin);return margin;
    }
    public void ShowAdvanced()
    {
        _advancedReturn=_practice?"Practice":_hostBotBox.Visible?"HostSetup":"Setup";
        ShowScreen("Advanced");
        foreach(string type in AdvancedRules.ItemTypes)
            ((CheckButton)_screens["Advanced"].FindChild("Allow_"+type,true,false)).SetPressedNoSignal(_advanced.Allows(type));
    }
    public void CloseAdvanced()=>ShowScreen(_advancedReturn);
    private ArcadeRuleDial Dial(Node parent,string caption,Func<string> value,Action<int> change)
    {
        var dial=new ArcadeRuleDial {Name=caption.Replace(" ",""),Caption=caption,ReadValue=value,ChangeValue=change};parent.AddChild(dial);_ruleDials.Add(dial);return dial;
    }
    private static int Cycle(int value,int direction,int min,int max)=>value+direction>max?min:value+direction<min?max:value+direction;
    private void BuildOnline()
    {
        var screen=Screen("Online");var box=Card(screen,new(74,62),new(1292,724));
        var header=ArcadeTheme.Row(box);ArcadeTheme.Heading(header,"Online Battle",40).SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var back=ArcadeTheme.Quiet(ArcadeTheme.Button(header,"Back",()=>BackRequested?.Invoke()));back.Name="OnlineBack";back.SizeFlagsVertical=SizeFlags.ShrinkCenter;back.CustomMinimumSize=new(120,48);
        var columns=ArcadeTheme.Row(box,36);columns.SizeFlagsVertical=SizeFlags.ExpandFill;
        var identity=ArcadeTheme.Column(columns,8);identity.CustomMinimumSize=new(330,0);
        Section(identity,"Your bomber");
        _onlinePreview=new CharacterPreview {CustomMinimumSize=new(300,196),Selected=true};identity.AddChild(_onlinePreview);
        // A dial like the setup rules: left/right walks the suits both ways, confirm steps forward.
        _onlineAppearanceButton=new ArcadeRuleDial {Name="OnlineAppearance",Caption="Suit",ValueWidth=150,
            ReadValue=()=>ArcadeTheme.Word(RosterSelection.AppearanceNames[_onlineAppearance]),ChangeValue=direction=>SetOnlineAppearance((_onlineAppearance+direction+8)%8)};
        identity.AddChild(_onlineAppearanceButton);
        _name=ArcadeTheme.Field(identity,"Your name — click to edit","","Type your name");_name.Name="PlayerName";_name.MaxLength=24;
        Hint(identity,"Shown above your bomber. Leave it blank to appear as P1, P2 and so on.",15);
        _name.TextChanged+=_=>IdentityChanged?.Invoke();
        _server=ArcadeTheme.Field(identity,"Server address",NetworkSession.DefaultServerUrl);_server.Name="ServerUrl";
        // Full LAN URLs ("http://192.168.x.y:port") fit the column; longer hosts scroll and show in the tooltip.
        _server.AddThemeFontSizeOverride("font_size",16);_server.TooltipText=_server.Text;
        _server.TextChanged+=text=>{_server.TooltipText=text;IdentityChanged?.Invoke();};
        var host=ArcadeTheme.Column(columns,14);host.CustomMinimumSize=new(400,0);
        Section(host,"Host a room");
        Copy(host,"Invite friends with a private room code. Pick the arena, CPU rivals and win target first.");
        _onlineStage=new StagePreview {CustomMinimumSize=new(400,206),StageIndex=_stage.Selected};host.AddChild(_onlineStage);
        ArcadeTheme.Button(host,"Room settings",()=>Navigation?.Invoke("HostSetup"));
        _onlineButtons.Add(ArcadeTheme.Button(host,"Create private room",()=>OnlineRequested?.Invoke(NetworkMode.CreatePrivate),true));
        Hint(host,"Share the code while friends connect. The room leader chooses when to start.",15);
        var join=ArcadeTheme.Column(columns,14);join.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        Section(join,"Join a friend");
        _code=ArcadeTheme.Field(join,"Room code","","Enter code");_code.MaxLength=16;
        _onlineButtons.Add(ArcadeTheme.Button(join,"Join room",()=>OnlineRequested?.Invoke(NetworkMode.JoinCode),true));
        join.AddChild(new Control {CustomMinimumSize=new(0,10)});
        Section(join,"Quick play");
        Copy(join,"Get matched with other players waiting in the queue. It needs at least two people.");
        _onlineButtons.Add(ArcadeTheme.Button(join,"Quick play",()=>OnlineRequested?.Invoke(NetworkMode.QuickPlay)));
        var status=Copy(box,"Pick a suit, then host or join a room on a reachable Burbuja server.",15);status.Name="OnlineNotice";status.AddThemeColorOverride("font_color",ArcadeTheme.Gold);
        var cancel=ArcadeTheme.Button(box,"Cancel connection",()=>BackRequested?.Invoke());cancel.Name="CancelConnect";cancel.Visible=false;cancel.SizeFlagsHorizontal=SizeFlags.ShrinkBegin;
    }
    private void BuildLobby()
    {
        var screen=Screen("Lobby");var box=Card(screen,new(220,48),new(1000,744));
        var header=ArcadeTheme.Row(box);ArcadeTheme.Heading(header,"Battle Room",40).SizeFlagsHorizontal=SizeFlags.ExpandFill;ArcadeTheme.Button(header,"Leave room",()=>BackRequested?.Invoke());
        var code=ArcadeTheme.Row(box,16);
        var codeCaption=ArcadeTheme.Text(code,"Room code",18,ArcadeTheme.Muted);codeCaption.VerticalAlignment=VerticalAlignment.Center;
        var codePlate=new PanelContainer();var codeStyle=ArcadeTheme.Key(ArcadeTheme.Sand,false,false,12,18);codePlate.AddThemeStyleboxOverride("panel",codeStyle);code.AddChild(codePlate);
        _joinCode=new Label {Text="..."};_joinCode.AddThemeFontOverride("font",ArcadeTheme.Display);_joinCode.AddThemeFontSizeOverride("font_size",36);_joinCode.AddThemeColorOverride("font_color",ArcadeTheme.Ink);codePlate.AddChild(_joinCode);
        _copyCode=ArcadeTheme.Button(code,"Copy code",()=>{if(_roomCode is {} room){DisplayServer.ClipboardSet(room);_copyCode.Text="Copied!";}});_copyCode.Name="CopyRoomCode";_copyCode.Disabled=true;_copyCode.FocusMode=FocusModeEnum.None;
        _lobbyStatus=ArcadeTheme.Text(box,"Connecting to your room...",24);
        _roster=new GridContainer {Columns=2,CustomMinimumSize=new(900,255)};_roster.AddThemeConstantOverride("h_separation",16);_roster.AddThemeConstantOverride("v_separation",10);box.AddChild(_roster);
        _partyHint=Copy(box,"Share the room code with friends. The leader starts each battle.",17);_partyHint.AddThemeColorOverride("font_color",ArcadeTheme.Muted);
        var actions=ArcadeTheme.Row(box,18);
        _startRoom=ArcadeTheme.Button(actions,"Start game",()=>StartRoomRequested?.Invoke(),true);_startRoom.Name="StartRoom";_startRoom.Disabled=true;
        _roomRules=ArcadeTheme.Button(actions,"Room settings",()=>Navigation?.Invoke("HostSetup"));_roomRules.Name="EditRoomSettings";_roomRules.Visible=false;
        ArcadeTheme.Button(actions,"Settings",()=>Navigation?.Invoke("Settings")).Name="LobbySettings";
    }
    private VBoxContainer _results=null!;
    private void BuildResults()
    {
        var screen=Screen("Results");

        _results=new VBoxContainer {Position=new(100,170),Size=new(1215,585)};screen.AddChild(_results);
        _resultTitle=ArcadeTheme.Heading(screen,"Battle Complete!",58);
        _resultTitle.Position=new(100,48);_resultTitle.Size=new(1240,80);
        _resultTitle.HorizontalAlignment=HorizontalAlignment.Center;
        _resultTitle.ClipText=true;_resultTitle.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        var body=ArcadeTheme.Row(_results,55);body.SizeFlagsVertical=SizeFlags.ExpandFill;
        var champion=ArcadeTheme.Column(body,0);champion.CustomMinimumSize=new(475,0);
        _winnerPreview=new CharacterPreview {Name="WinnerPreview",Platform=false,ResultsPresentation=true,CustomMinimumSize=new(475,440),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};champion.AddChild(_winnerPreview);
        ArcadeTheme.Text(champion,"Last one standing",19,ArcadeTheme.Gold).HorizontalAlignment=HorizontalAlignment.Center;
        // The standings are printed onto the 3D board: FitStandings keeps them inside its projected face.
        _standingsBoard=ArcadeTheme.Column(screen,6);_standingsBoard.Name="StandingsBoard";_standingsBoard.Position=new(660,200);_standingsBoard.Size=new(600,400);
        var heading=ArcadeTheme.Row(_standingsBoard);ArcadeTheme.Heading(heading,"Final standings",28).SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var winsCaption=ArcadeTheme.Text(heading,"Round wins",16,ArcadeTheme.Muted);winsCaption.VerticalAlignment=VerticalAlignment.Center;winsCaption.SizeFlagsVertical=SizeFlags.Fill;
        _resultRows=ArcadeTheme.Column(_standingsBoard,4);_resultRows.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var scores=ArcadeTheme.Text(_results,"",1);scores.Name="Standings";scores.Visible=false;
        var row=ArcadeTheme.Row(_results,25);
        var replay=new ArcadeModeButton {Text="Play Again",CustomMinimumSize=new(590,88),FaceColor=Coral,Accent=ArcadeTheme.Gold,SizeFlagsHorizontal=SizeFlags.ExpandFill};replay.Pressed+=()=>ReplayRequested?.Invoke();row.AddChild(replay);_replay=replay;
        var modes=new ArcadeModeButton {Text="Mode Select",CustomMinimumSize=new(590,88),FaceColor=ArcadeTheme.Indigo,Accent=ArcadeTheme.Pink,SizeFlagsHorizontal=SizeFlags.ExpandFill};modes.Pressed+=()=>Navigation?.Invoke("Modes");row.AddChild(modes);
    }
    public override void _Process(double delta)
    {
        if(_screen=="Results"&&_standingsBoard.IsVisibleInTree())FitStandings();
    }
    /// <summary>Places the standings inside the board's velvet face as the parallax camera moves, and sizes rows to the player count.</summary>
    private void FitStandings()
    {
        if(_background.ResultsBoardFace() is not {} face)return;
        var toScreen=_screens["Results"].GetGlobalTransform().AffineInverse()*_background.GetGlobalTransform();
        var topLeft=toScreen*face.Position;var bottomRight=toScreen*face.End;
        var inner=new Rect2(topLeft,bottomRight-topLeft).GrowIndividual(-28,-16,-30,-18);
        _standingsBoard.Position=inner.Position.Round();_standingsBoard.Size=inner.Size.Round();
        int count=Math.Max(1,_resultRows.GetChildCount());
        float row=Mathf.Floor(Math.Clamp((inner.Size.Y-46-4*(count-1))/count,26,52));
        if(Mathf.IsEqualApprox(row,_standingsRowHeight))return;
        _standingsRowHeight=row;
        foreach(var child in _resultRows.GetChildren())SizeResultRow((Control)child,row);
    }
    private static void SizeResultRow(Control row,float height)
    {
        row.CustomMinimumSize=new(0,height);
        foreach(var part in row.GetChildren().OfType<Control>())
        {
            if(part is RankBadge)part.CustomMinimumSize=new(height-6,height-6);
            else if(part is BomberPortrait)part.CustomMinimumSize=new(height-4,height);
            else if(part is ResultMedals)part.CustomMinimumSize=new(170,Math.Min(44,height));
            // Text shrinks with dense eight-player boards so the row, not its glyphs, sets the height.
            else if(part is Label label)
            {
                bool score=label.GetThemeFont("font")==ArcadeTheme.Display;
                label.AddThemeFontSizeOverride("font_size",(int)Math.Min(score?26:22,height*(score?.6f:.58f)));
            }
        }
    }
    public void ShowResults(JsonElement? state,JsonElement? result,bool party=false)
    {
        _replay.Text=party?"Return to lobby":"Play Again";
        var text="The battle has ended.";
        JsonElement? rows=null;
        if(result is { } final && final.TryGetProperty("details",out var details) && details.ValueKind==JsonValueKind.Object && details.TryGetProperty("scoreboard",out var scoreboard)) rows=scoreboard;
        else if(state is { } data && data.TryGetProperty("players",out var players)) rows=players;
        _winnerPreview.Visible=false;_resultTitle.Text="Battle Complete!";
        if(rows is { } scores)
        {
            var ordered=scores.EnumerateArray().OrderByDescending(p=>p.GetProperty("wins").GetInt32()).ToArray();
            foreach(var child in _resultRows.GetChildren()){_resultRows.RemoveChild(child);child.QueueFree();}
            _background.SetResultCount(ordered.Length);_standingsRowHeight=0;
            foreach(var (player,rank) in ordered.Select((p,i)=>(p,i)))AddResultRow(player,rank);
            text=string.Join("\n",ordered.Select((p,i)=>$"{i+1:00}    {p.GetProperty("name").GetString()}     {p.GetProperty("wins").GetInt32()} "+(p.GetProperty("wins").GetInt32()==1?"WIN":"WINS")));
            if(ordered.Length>0&&result is {} completed&&completed.TryGetProperty("reason",out var won)&&won.GetString() is "won" or "bot_won")
            {
                var leader=ordered[0];int appearance=leader.TryGetProperty("appearance",out var suit)?suit.GetInt32():leader.GetProperty("slot").GetInt32();
                _winnerPreview.SetAppearance(appearance);_winnerPreview.Celebrate();_winnerPreview.Visible=true;
                _resultTitle.Text=(leader.GetProperty("name").GetString()??"Player")+" Wins!";
            }
        }
        if(result is { } r && r.TryGetProperty("reason",out var reason)) text+="\n\n"+(reason.GetString() switch {"bot_won"=>"A CPU rival takes the match!","won"=>"Thanks for playing!",_=>reason.GetString()});
        _results.GetNode<Label>("Standings").Text=text;ShowScreen("Results");
    }
    private void AddResultRow(JsonElement player,int rank)
    {
        int slot=player.GetProperty("slot").GetInt32();
        int appearance=player.TryGetProperty("appearance",out var suit)?suit.GetInt32():slot;
        var row=ArcadeTheme.Row(_resultRows,12);
        row.AddChild(new RankBadge {Rank=rank+1,SizeFlagsVertical=SizeFlags.ShrinkCenter});
        var character=new BomberPortrait {Suit=ArcadeTheme.Players[Math.Clamp(appearance,0,7)],SizeFlagsVertical=SizeFlags.ShrinkCenter};row.AddChild(character);
        var name=ArcadeTheme.Text(row,player.GetProperty("name").GetString()??"Player",22,rank==0?ArcadeTheme.Gold:ArcadeTheme.Cream);name.SizeFlagsHorizontal=SizeFlags.ExpandFill;name.VerticalAlignment=VerticalAlignment.Center;
        name.ClipText=true;name.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        int wins=player.GetProperty("wins").GetInt32();
        row.AddChild(new ResultMedals {Wins=wins,SizeFlagsVertical=SizeFlags.ShrinkCenter});
        // A zero recedes so the eye lands on actual round wins.
        var count=ArcadeTheme.Text(row,wins.ToString(),26,wins>0?ArcadeTheme.Gold:ArcadeTheme.Muted,true);count.CustomMinimumSize=new(38,0);count.HorizontalAlignment=HorizontalAlignment.Right;count.VerticalAlignment=VerticalAlignment.Center;
        SizeResultRow(row,_standingsRowHeight>0?_standingsRowHeight:48);
    }
    public void ShowScreen(string screen)
    {
        _background.Visible=screen is not ("Pause" or "Settings") || !_overlayBackground;
        _background.Page=screen;_background.Quiet=screen=="Roster";_background.Results=screen=="Results";_background.QueueRedraw();
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
        if(screen=="Roster")_localPlayers.CallDeferred(nameof(RosterSelection.FocusFirst));
        else if(_screen=="Setup")_stageDial.CallDeferred(Control.MethodName.GrabFocus);
        else {var first=DefaultFocus(_screen)??FirstFocusable(_screens[_screen]);first?.CallDeferred(Control.MethodName.GrabFocus);}
    }
    /// <summary>Screens whose first button in tree order is Back/Leave start on their first real choice instead.</summary>
    private Control? DefaultFocus(string screen)=>screen switch
    {
        "Online"=>_onlineAppearanceButton,
        "Lobby"=>_startRoom.Visible&&!_startRoom.Disabled?_startRoom:!_copyCode.Disabled?_copyCode:_screens["Lobby"].FindChild("LobbySettings",true,false) as Control,
        _=>null
    };
    private static Control? FirstFocusable(Node node)
    {foreach(var child in node.GetChildren()){if(child is Button button&&button.Visible&&!button.Disabled)return button;var found=FirstFocusable(child);if(found!=null)return found;}return null;}
    public void Notice(string message)
    {
        _notice.Text=message;
        var node=_screens["Online"].FindChild("OnlineNotice",true,false) as Label;if(node!=null)node.Text=message;
    }
    public void SetBusy(bool busy,string? status=null)
    {
        foreach(var button in _onlineButtons)button.Disabled=busy;
        var cancel=_screens["Online"].FindChild("CancelConnect",true,false) as Button;if(cancel!=null)cancel.Visible=busy;
        if(status is not null)Notice(status);
    }
    public void ShowLobby(JsonElement? lobby,string? code,string connection)
    {
        _roomCode=string.IsNullOrWhiteSpace(code)?null:code;_joinCode.Text=_roomCode??"Matchmaking";
        _copyCode.Disabled=_roomCode is null;_copyCode.FocusMode=_roomCode is null?FocusModeEnum.None:FocusModeEnum.All;_copyCode.Text="Copy code";
        _lobbyStatus.Text=connection;
        if(lobby is not { } data)return;
        var status=LobbyStatus.Parse(data);
        if(status.JoinCode is { } roomCode){_roomCode=roomCode;_joinCode.Text=roomCode;_copyCode.Disabled=false;_copyCode.FocusMode=FocusModeEnum.All;}
        foreach(var child in _roster.GetChildren()){_roster.RemoveChild(child);child.QueueFree();}_lobbyPreviews.Clear();
        foreach(var member in status.Players)
        {
            var seatStyle=ArcadeTheme.Panel(new Color("28305e"),new Color("394a8f"),2,14,6);seatStyle.ContentMarginLeft=8;seatStyle.ContentMarginRight=16;
            var panel=new PanelContainer {CustomMinimumSize=new(440,70)};panel.AddThemeStyleboxOverride("panel",seatStyle);_roster.AddChild(panel);
            var row=ArcadeTheme.Row(panel,12);var preview=new CharacterPreview {CustomMinimumSize=new(60,66),Selected=member.Connected};row.AddChild(preview);preview.SetAppearance(_lobbyAppearances.TryGetValue(member.Id,out int appearance)?appearance:member.Slot%8);_lobbyPreviews[member.Id]=preview;
            var seat=ArcadeTheme.Text(row,$"P{member.Slot+1}",18,ArcadeTheme.Gold,true);seat.VerticalAlignment=VerticalAlignment.Center;
            var name=ArcadeTheme.Text(row,member.Name,18);name.SizeFlagsHorizontal=SizeFlags.ExpandFill;name.ClipText=true;name.VerticalAlignment=VerticalAlignment.Center;
            var state=ArcadeTheme.Text(row,member.Connected?"Connected":"Reconnecting",14,member.Connected?ArcadeTheme.Aqua:ArcadeTheme.Red);state.VerticalAlignment=VerticalAlignment.Center;
        }
        _countdown=_party is null?status.CountdownEndsAt:null;
        _lobbyStatus.Text=status.State switch {"countdown"=>"The battle is about to begin!","running"=>"Entering the arena...",_=>"Waiting for players..."};
        ApplyParty();
    }
    public void ResetLobby()
    {
        _party=null;_localPlayer=null;_countdown=null;_roomCode=null;_copyCode.Disabled=true;_copyCode.FocusMode=FocusModeEnum.None;_startRoom.Disabled=true;_startRoom.Visible=false;_roomRules.Visible=false;
        _lobbyAppearances.Clear();_lobbyPreviews.Clear();
        foreach(var child in _roster.GetChildren()){_roster.RemoveChild(child);child.QueueFree();}
    }
    public void SetLobbyAppearance(string playerId,int appearance)
    {
        if(appearance is <0 or >7)return;
        _lobbyAppearances[playerId]=appearance;
        if(_lobbyPreviews.TryGetValue(playerId,out var preview)&&GodotObject.IsInstanceValid(preview))preview.SetAppearance(appearance);
    }
    public void UpdateConnection(string connection)
    {
        if(_countdown is { } end)_lobbyStatus.Text=$"Battle begins in {Math.Max(0,(int)Math.Ceiling((end-DateTimeOffset.UtcNow).TotalSeconds))}";
        else if(connection.StartsWith("Reconnecting")||connection.StartsWith("Error"))_lobbyStatus.Text=connection;
    }
    public void Configure(SessionSettings settings,int humans=1)
    {
        _advanced=settings.Advanced;
        _difficulty.Select(Array.IndexOf(new[]{"easy","normal","hard"},settings.BotDifficulty=="medium"?"normal":settings.BotDifficulty));_stage.Select(Math.Max(0,Array.IndexOf(StageIds,settings.Stage)));_bots.Value=settings.Bots;_wins.Value=settings.Wins;_rounds.Value=settings.RoundSeconds;_sudden.ButtonPressed=settings.SuddenDeath;_ghosts.ButtonPressed=settings.Ghosts;_localPlayers.ConfigureCounts(humans,settings.Bots);UpdateStageDescription();
    }
    private bool _overlayBackground;
    public GraphicsPreset Graphics {get;private set;}=GraphicsPreset.Medium;
    private Label _graphicsDescription=null!;
    public void SetOverlay(bool overlay)=>_overlayBackground=overlay;
    private void BuildSettings()
    {
        var screen=Screen("Settings");var box=Card(screen,new(150,95),new(1140,690));
        ArcadeTheme.Heading(box,"Settings",42);
        Copy(box,"Your preferences are saved on this device.",18);
        var columns=ArcadeTheme.Row(box,40);
        var player=ArcadeTheme.Column(columns,14);player.CustomMinimumSize=new(500,440);
        var updates=ArcadeTheme.Column(columns,14);updates.CustomMinimumSize=new(500,440);
        _volumeLabel=ArcadeTheme.Text(player,"Sound volume: 80%",22);
        _volume=new HSlider {Name="SoundVolume",MinValue=0,MaxValue=100,Step=5,Value=80,CustomMinimumSize=new(460,48)};player.AddChild(_volume);
        _volume.ValueChanged+=value=>{_volumeLabel.Text=$"Sound volume: {value:0}%";SettingsChanged?.Invoke();};
        _vibration=new ArcadeToggle {Name="Vibration",Text="Controller vibration",CustomMinimumSize=new(460,56)};player.AddChild(_vibration);
        _vibration.Toggled+=_=>SettingsChanged?.Invoke();
        Copy(player,"Vibration is off by default. Enable it for bomb and impact feedback on supported controllers.",18);
        ArcadeTheme.Button(player,"Test vibration",()=>TestVibrationRequested?.Invoke()).Name="TestVibration";
        _settingsNotice=Copy(player,"",16);
        var lighting=new ArcadeRuleDial {Name="StageLighting",Caption="Local lighting",ValueWidth=160,
            ReadValue=()=>StageLighting.For(LocalStageTime).Label,
            ChangeValue=direction=>{LocalStageTime=(StageTime)(((int)LocalStageTime+direction+4)%4);SettingsChanged?.Invoke();}};
        player.AddChild(lighting);
        Copy(player,"Morning, midday, afternoon or night. Online rounds use the server's lighting.",16);
        var graphics=new ArcadeRuleDial {Name="GraphicsPreset",Caption="Graphics",ValueWidth=160,
            ReadValue=()=>Graphics.ToString(),ChangeValue=direction=>
            {Graphics=(GraphicsPreset)(((int)Graphics+direction+3)%3);_graphicsDescription.Text=GraphicsQuality.Description(Graphics);SettingsChanged?.Invoke();}};
        updates.AddChild(graphics);
        _graphicsDescription=Copy(updates,GraphicsQuality.Description(Graphics),16);
        Section(updates,"Game updates");
        _automaticUpdates=new ArcadeToggle {Name="AutomaticUpdates",Text="Check automatically",CustomMinimumSize=new(0,56),ButtonPressed=true};updates.AddChild(_automaticUpdates);
        _automaticUpdates.Toggled+=_=>SettingsChanged?.Invoke();
        _updateStatus=Copy(updates,"",17);
        Copy(updates,"New versions download in the background. Finish your game, then restart to install.",18);
        _checkUpdates=ArcadeTheme.Button(updates,"Check for updates",()=>CheckUpdatesRequested?.Invoke());_checkUpdates.Name="CheckUpdates";
        _installUpdate=ArcadeTheme.Button(updates,"Update and restart",()=>InstallUpdateRequested?.Invoke(),true);_installUpdate.Name="InstallUpdate";_installUpdate.Visible=false;
        ArcadeTheme.Button(box,"Back",()=>BackRequested?.Invoke()).Name="SettingsBack";
    }
    private void BuildPause()
    {
        var screen=Screen("Pause");var box=Card(screen,new(420,150),new(600,520));
        ArcadeTheme.Heading(box,"Game menu",42);
        _pauseHint=Copy(box,"",18);
        ArcadeTheme.Button(box,"Resume game",()=>ResumeRequested?.Invoke(),true).Name="ResumeGame";
        ArcadeTheme.Button(box,"Settings",()=>Navigation?.Invoke("Settings")).Name="PauseSettings";
        ArcadeTheme.Button(box,"Leave game",()=>Navigation?.Invoke("Modes")).Name="LeaveGame";
    }
    public void ConfigurePreferences(PlayerPreferences preferences)
    {
        _name.Text=preferences.PlayerName;_server.Text=preferences.ServerUrl;
        _automaticUpdates.SetPressedNoSignal(preferences.AutomaticUpdates);
        _vibration.SetPressedNoSignal(preferences.Vibration);_volume.SetValueNoSignal(preferences.Volume*100);
        _volumeLabel.Text=$"Sound volume: {preferences.Volume*100:0}%";
        LocalStageTime=preferences.StageLighting;
        Graphics=preferences.Graphics;_graphicsDescription.Text=GraphicsQuality.Description(Graphics);
    }
    public void UpdateStatus(string version,string status,bool busy,bool ready,bool canInstall)
    {
        _updateStatus.Text=$"{version} · {status}";
        _settingsButton.Text=ready?"Settings · Update ready":"Settings";
        _checkUpdates.Disabled=busy||ready;_installUpdate.Visible=ready;_installUpdate.Disabled=busy||!canInstall;
        _installUpdate.TooltipText=canInstall?"Restart to install the verified update.":"Leave your current room before updating.";
    }
    public void SettingsNotice(string text)=>_settingsNotice.Text=text;
    public void ShowPause(bool online)
    { _pauseHint.Text=online?"The online match continues while this menu is open.":"The local game is paused.";ShowScreen("Pause"); }
    public void FocusStart()
    {
        Control? target=_screen switch {"Lobby"=>_startRoom.Visible&&!_startRoom.Disabled?_startRoom:null,"Setup"=>_launch,"Pause"=>_screens["Pause"].FindChild("ResumeGame",true,false) as Control,_=>FirstFocusable(_screens[_screen])};
        target?.GrabFocus();
    }
    public void ShowParty(JsonElement data,string? localPlayer)
    {
        _party=data.Clone();_localPlayer=localPlayer;_countdown=null;
        var lobby=JsonSerializer.SerializeToElement(new {state="open",players=data.GetProperty("players")});
        ShowLobby(lobby,_roomCode,"Connected");
    }
    private void ApplyParty()
    {
        _startRoom.Visible=_party!=null;_roomRules.Visible=_party!=null;
        if(_party is not {} data)return;
        var status=PartyStatus.Parse(data);bool leader=status.LeaderId==_localPlayer;
        _startRoom.Disabled=!leader||!status.CanStart;_roomRules.Disabled=!leader||status.Phase!="lobby";
        string leaderName=data.GetProperty("players").EnumerateArray().FirstOrDefault(p=>p.GetProperty("playerId").GetString()==status.LeaderId) is var player&&player.ValueKind==JsonValueKind.Object?player.GetProperty("displayName").GetString()??"Leader":"Leader";
        _lobbyStatus.Text=status.Phase switch {"starting"=>"Starting the battle...","results"=>"Battle complete — returning to the room...","playing"=>"Battle in progress",_=>leader?status.CanStart?"You are the leader. Start when everyone is here.":"Waiting for another player or CPU rival.":$"Waiting for {leaderName} to start."};
        _partyHint.Text=$"Leader: {leaderName}  •  CPU rivals: {status.Bots}\nShare the room code. Start / + focuses Start game; confirm to begin.";
    }
    private void UpdateStageDescription()
    {
        _background.SelectArena(StageIds[_stage.Selected]);
        if(_onlineStage!=null){_onlineStage.StageIndex=_stage.Selected;_onlineStage.QueueRedraw();}
        _stageDescription.Text=_stage.Selected switch {0=>"Climb the outer walkways. Send a pumped bomb\nover the ledge. Watch the water below.",1=>"An open arena with pillars and breakable blocks.\nFind an escape before lighting the fuse.",_=>"Fight across three connected terraces.\nUse ramps to move between elevations."};
    }
}

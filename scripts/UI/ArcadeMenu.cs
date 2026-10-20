using Godot;
using Bomber.Client;
using System.Text.Json;
using BlastCircuit.InputDevices;
namespace BlastCircuit.UI;

/// <summary>Mode, setup, connection, lobby and result screens; emits intent to the composition root.</summary>
public partial class ArcadeMenu : Control
{
    public event Action<string>? Navigation;
    public event Action<int>? AppearanceChanged;
    private readonly Dictionary<string,int> _lobbyAppearances=[];
    private readonly Dictionary<string,CharacterPreview> _lobbyPreviews=[];
    public event Action? LocalRequested, ReplayRequested, BackRequested;
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
    private Button _editPlayers=null!,_onlineAppearanceButton=null!;
    private CharacterPreview _onlinePreview=null!;
    private CharacterPreview _winnerPreview=null!;
    private VBoxContainer _resultRows=null!;
    private Label _resultTitle=null!;
    private int _onlineAppearance;
    private CheckButton _sudden=null!,_ghosts=null!;
    private LineEdit _server=null!,_name=null!,_code=null!;
    private readonly Dictionary<string,Control> _screens=[];
    private bool _practice;
    private string _screen="Modes";
    private DateTimeOffset? _countdown;
    private readonly List<Button> _onlineButtons=[];
    private Label _inputFooter=null!;
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
    private SessionSettings BuildSettings(int bots)=>new(StageIds[_stage.Selected],bots,(int)_wins.Value,(int)_rounds.Value,_sudden.ButtonPressed,_ghosts.ButtonPressed,new[]{"easy","normal","hard"}[_difficulty.Selected]);
    private static readonly string[] StageIds=["sunken-temple","rock-garden","up-and-down"];
    public override void _Ready()
    {
        Theme=ArcadeTheme.Create();SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _background=new ArcadeBackdrop();AddChild(_background);_background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _content=new Control();AddChild(_content);_content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildModes();BuildControls();BuildRoster();BuildSetup();BuildOnline();BuildLobby();BuildResults();
        var footer=new HBoxContainer {AnchorTop=1,AnchorBottom=1,AnchorRight=1,OffsetTop=-62,OffsetBottom=-8,OffsetLeft=58,OffsetRight=-58,Alignment=BoxContainer.AlignmentMode.Center};AddChild(footer);
        _inputFooter=ArcadeTheme.Text(footer,"",16);UpdateInputFooter();ControllerHub.Current.PromptChanged+=UpdateInputFooter;
        var spacer=new Control {SizeFlagsHorizontal=SizeFlags.ExpandFill};footer.AddChild(spacer);
        ArcadeTheme.Text(footer,"BURBUJA",15);
        ShowScreen("Modes");
    }
    private void UpdateInputFooter()=>_inputFooter.Text=ControllerProfile.Menu(ControllerHub.Current.ActiveFamily);
    public override void _ExitTree(){ControllerHub.Current.PromptChanged-=UpdateInputFooter;}
    private Control Screen(string name)
    {var control=new Control();_content.AddChild(control);control.AnchorLeft=control.AnchorRight=.5f;control.AnchorTop=control.AnchorBottom=.5f;control.OffsetLeft=-720;control.OffsetRight=720;control.OffsetTop=-450;control.OffsetBottom=450;_screens[name]=control;return control;}
    private VBoxContainer Card(Node parent,Vector2 position,Vector2 size,Color? fill=null)
    {
        var panel=new PanelContainer {Position=position,Size=size};panel.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(fill??new Color(.045f,.065f,.14f,.82f),new Color("6884b6"),0,12,26));parent.AddChild(panel);return ArcadeTheme.Column(panel,14);
    }
    private void BuildModes()
    {
        var screen=Screen("Modes");
        var title=ArcadeTheme.Text(screen,"BLAST\nCIRCUIT",91,ArcadeTheme.Gold);title.Position=new(108,85);title.AddThemeConstantOverride("outline_size",7);title.AddThemeConstantOverride("line_spacing",-18);
        title.AddThemeColorOverride("font_shadow_color",new Color("14132f"));title.AddThemeConstantOverride("shadow_offset_x",5);title.AddThemeConstantOverride("shadow_offset_y",7);
        var menu=new VBoxContainer {Position=new(748,229),Size=new(560,500)};menu.AddThemeConstantOverride("separation",9);screen.AddChild(menu);
        Mode(menu,"LOCAL BATTLE","",()=>Navigation?.Invoke("Roster"),true);
        Mode(menu,"ONLINE BATTLE","",()=>Navigation?.Invoke("Online"));
        Mode(menu,"PRACTICE","",()=>Navigation?.Invoke("Practice"));
        var controls=ArcadeTheme.Button(menu,"CONTROLS",()=>Navigation?.Invoke("Controls"));controls.Name="OpenControls";controls.CustomMinimumSize=new(0,48);
        foreach(var state in new[]{"normal","hover","pressed"})controls.AddThemeStyleboxOverride(state,ArcadeTheme.Panel(new Color(0,0,0,0),new Color(0,0,0,0),0,0,4));
    }
    private void BuildControls()
    {
        var screen=Screen("Controls");var box=Card(screen,new(64,54),new(1312,746));
        var panel=new ControllerPanel();box.AddChild(panel);panel.BackRequested+=()=>BackRequested?.Invoke();
    }
    private static void Mode(Node parent,string title,string description,Action action,bool primary=false)
    {
        var button=new ArcadeModeButton {Text=title,CustomMinimumSize=new(0,116),FaceColor=primary?new Color("a34d76"):title.Contains("ONLINE")?new Color("287e74"):new Color("4f62a2"),Accent=primary?new Color("da94ba"):title.Contains("ONLINE")?new Color("85c8b3"):new Color("d3b66c")};
        button.Pressed+=action;parent.AddChild(button);
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
        _onlineAppearance=appearance;_onlinePreview.SetAppearance(appearance);_onlineAppearanceButton.Text="SUIT: "+RosterSelection.AppearanceNames[appearance]+"  →";AppearanceChanged?.Invoke(appearance);
    }
    private void BuildSetup()
    {
        var screen=Screen("Setup");
        _setupTitle=ArcadeTheme.Text(screen,"MATCH SETUP",44,ArcadeTheme.Gold);_setupTitle.Position=new(80,64);
        var back=ArcadeTheme.Button(screen,"BACK",()=>BackRequested?.Invoke());back.Name="SetupBack";back.Position=new(1195,73);back.Size=new(140,48);QuietSetupButton(back);
        // These native settings nodes remain the single source of truth for configure/API/harness callers.
        var state=new VBoxContainer {Name="SetupSettings",Visible=false};screen.AddChild(state);
        _stage=new OptionButton {Name="Stage"};foreach(var title in new[]{"Sunken Temple","Rock Garden","Up & Down"})_stage.AddItem(title);state.AddChild(_stage);
        _wins=new SpinBox {Name="Wins",MinValue=1,MaxValue=5,Value=3};state.AddChild(_wins);
        _bots=new SpinBox {Name="Bots",MinValue=0,MaxValue=7,Value=3};state.AddChild(_bots);
        _rounds=new SpinBox {Name="RoundSeconds",MinValue=60,MaxValue=300,Step=30,Value=120};state.AddChild(_rounds);
        _difficulty=new OptionButton {Name="Difficulty"};foreach(var title in new[]{"Easy","Normal","Hard"})_difficulty.AddItem(title);_difficulty.Select(1);state.AddChild(_difficulty);
        _sudden=new CheckButton {Name="SuddenDeath",ButtonPressed=true};state.AddChild(_sudden);
        _ghosts=new CheckButton {Name="Ghosts",ButtonPressed=true};state.AddChild(_ghosts);
        _stageDial=new ArcadeRuleDial {Name="StageSelector",Position=new(80,602),Size=new(640,76),Caption="ARENA",ReadValue=()=>_stage.GetItemText(_stage.Selected).ToUpperInvariant(),ChangeValue=direction=>{_stage.Select((_stage.Selected+direction+3)%3);UpdateStageDescription();}};
        screen.AddChild(_stageDial);
        _stageDescription=ArcadeTheme.Text(screen,"",18);_stageDescription.Position=new(104,691);_stageDescription.Size=new(620,70);
        _stage.ItemSelected+=_=>UpdateStageDescription();
        _rosterSummary=ArcadeTheme.Text(screen,"",18);_rosterSummary.Position=new(822,143);
        var rail=new VBoxContainer {Position=new(800,188),Size=new(535,415)};rail.AddThemeConstantOverride("separation",3);screen.AddChild(rail);
        Dial(rail,"Wins to victory",()=>((int)_wins.Value).ToString(),direction=>_wins.Value=Cycle((int)_wins.Value,direction,1,5));
        Dial(rail,"Round time",()=>TimeSpan.FromSeconds(_rounds.Value).ToString(@"m\:ss"),direction=>_rounds.Value=Cycle((int)_rounds.Value,direction*30,60,300));
        Dial(rail,"CPU level",()=>_difficulty.GetItemText(_difficulty.Selected).ToUpperInvariant(),direction=>_difficulty.Select((_difficulty.Selected+direction+3)%3));
        var bots=Dial(rail,"CPU rivals",()=>((int)_bots.Value).ToString(),direction=>_bots.Value=Cycle((int)_bots.Value,direction,0,7));_hostBotBox=bots;
        Dial(rail,"Sudden death",()=>_sudden.ButtonPressed?"ON":"OFF",_=>_sudden.ButtonPressed=!_sudden.ButtonPressed);
        Dial(rail,"Ghost comeback",()=>_ghosts.ButtonPressed?"ON":"OFF",_=>_ghosts.ButtonPressed=!_ghosts.ButtonPressed);
        _editPlayers=ArcadeTheme.Button(rail,"CHANGE PLAYERS",()=>Navigation?.Invoke("Roster"));_editPlayers.Name="EditPlayers";_editPlayers.CustomMinimumSize=new(0,43);_editPlayers.AddThemeFontSizeOverride("font_size",17);QuietSetupButton(_editPlayers);
        _notice=ArcadeTheme.Text(screen,"",16);_notice.Position=new(824,625);_notice.Size=new(490,58);_notice.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        _launch=new ArcadeModeButton {Name="LaunchMatch",Text="LET’S GO!",Position=new(797,699),Size=new(550,86),FaceColor=new Color("a34d76"),Accent=new Color("f3bc66")};screen.AddChild(_launch);_launch.Pressed+=()=>LocalRequested?.Invoke();
        UpdateStageDescription();
    }
    private ArcadeRuleDial Dial(Node parent,string caption,Func<string> value,Action<int> change)
    {
        var dial=new ArcadeRuleDial {Name=caption.Replace(" ",""),Caption=caption,ReadValue=value,ChangeValue=change};parent.AddChild(dial);_ruleDials.Add(dial);return dial;
    }
    private static int Cycle(int value,int direction,int min,int max)=>value+direction>max?min:value+direction<min?max:value+direction;
    private static void QuietSetupButton(Button button)
    {
        foreach(var state in new[]{"normal","hover","pressed"})button.AddThemeStyleboxOverride(state,new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("focus",ArcadeTheme.Panel(new Color(0,0,0,0),ArcadeTheme.Gold,2,0,4));
    }
    private void BuildOnline()
    {
        var screen=Screen("Online");var box=Card(screen,new(74,62),new(1292,724));
        var header=ArcadeTheme.Row(box);ArcadeTheme.Text(header,"ONLINE BATTLE",34).SizeFlagsHorizontal=SizeFlags.ExpandFill;ArcadeTheme.Text(header,"YOUR LOOK. YOUR ROOM. YOUR RIVALS.",15);ArcadeTheme.Button(header,"← BACK",()=>BackRequested?.Invoke());
        var columns=ArcadeTheme.Row(box,24);columns.SizeFlagsVertical=SizeFlags.ExpandFill;
        var identity=ArcadeTheme.Column(columns,8);identity.CustomMinimumSize=new(300,0);
        ArcadeTheme.Text(identity,"01  /  YOUR CONTENDER",16);
        _onlinePreview=new CharacterPreview {CustomMinimumSize=new(280,238),Selected=true};identity.AddChild(_onlinePreview);
        _onlineAppearanceButton=ArcadeTheme.Button(identity,"SUIT: WHITE  →",()=>SetOnlineAppearance((_onlineAppearance+1)%8));_onlineAppearanceButton.Name="OnlineAppearance";_onlineAppearanceButton.AddThemeFontSizeOverride("font_size",17);
        _name=ArcadeTheme.Field(identity,"PLAYER NAME","Player");_name.MaxLength=24;
        _server=ArcadeTheme.Field(identity,"SERVER ADDRESS","http://127.0.0.1:18496");
        var host=ArcadeTheme.Column(columns,14);host.CustomMinimumSize=new(408,0);
        ArcadeTheme.Text(host,"02  /  HOST A ROOM",16);
        ArcadeTheme.Text(host,"PLAY YOUR RULES",26);
        ArcadeTheme.Text(host,"Invite friends with a private room code.\nChoose your arena, CPU rivals and win target.",16);
        var preview=new StagePreview {CustomMinimumSize=new(360,185)};host.AddChild(preview);
        ArcadeTheme.Button(host,"ROOM SETTINGS",()=>Navigation?.Invoke("HostSetup"));
        _onlineButtons.Add(ArcadeTheme.Button(host,"CREATE PRIVATE ROOM   →",()=>OnlineRequested?.Invoke(NetworkMode.CreatePrivate),true));
        ArcadeTheme.Text(host,"Rooms with CPU rivals can begin\nwith one human. Share the code while players connect.",15);
        var join=ArcadeTheme.Column(columns,14);join.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        ArcadeTheme.Text(join,"03  /  FIND YOUR RIVALS",16);
        ArcadeTheme.Text(join,"JOIN A FRIEND",26);
        _code=ArcadeTheme.Field(join,"ROOM CODE","","Enter code");_code.MaxLength=16;
        _onlineButtons.Add(ArcadeTheme.Button(join,"JOIN ROOM   →",()=>OnlineRequested?.Invoke(NetworkMode.JoinCode),true));
        ArcadeTheme.Text(join,"QUICK PLAY",25);
        ArcadeTheme.Text(join,"Match with real queued players.\nAt least two humans are needed.",16);
        _onlineButtons.Add(ArcadeTheme.Button(join,"QUICK PLAY   →",()=>OnlineRequested?.Invoke(NetworkMode.QuickPlay)));
        var status=ArcadeTheme.Text(box,"Choose a suit, then host or join a reachable Burbuja server.",15);status.Name="OnlineNotice";status.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var cancel=ArcadeTheme.Button(box,"CANCEL CONNECTION",()=>BackRequested?.Invoke());cancel.Name="CancelConnect";cancel.Visible=false;
    }
    private void BuildLobby()
    {
        var screen=Screen("Lobby");var box=Card(screen,new(220,90),new(1000,660));
        var header=ArcadeTheme.Row(box);ArcadeTheme.Text(header,"BATTLE ROOM",36).SizeFlagsHorizontal=SizeFlags.ExpandFill;ArcadeTheme.Button(header,"LEAVE ROOM",()=>BackRequested?.Invoke());
        _joinCode=ArcadeTheme.Text(box,"ROOM CODE  —",39);
        _lobbyStatus=ArcadeTheme.Text(box,"Connecting to your room…",24);
        _roster=new GridContainer {Columns=2,CustomMinimumSize=new(900,255)};_roster.AddThemeConstantOverride("h_separation",16);_roster.AddThemeConstantOverride("v_separation",8);box.AddChild(_roster);
        ArcadeTheme.Text(box,"The match starts automatically when enough players connect.\nShare the room code with friends. CPU seats are assigned by the server.",18);
    }
    private VBoxContainer _results=null!;
    private void BuildResults()
    {
        var screen=Screen("Results");

        _results=Card(screen,new(132,128),new(1176,620),new Color(0,0,0,0));
        _resultTitle=ArcadeTheme.Text(_results,"BATTLE COMPLETE!",38,ArcadeTheme.Gold);
        _resultTitle.HorizontalAlignment=HorizontalAlignment.Center;
        _resultTitle.ClipText=true;_resultTitle.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        var body=ArcadeTheme.Row(_results,30);body.SizeFlagsVertical=SizeFlags.ExpandFill;
        var champion=ArcadeTheme.Column(body,4);champion.CustomMinimumSize=new(305,0);
        _winnerPreview=new CharacterPreview {Name="WinnerPreview",Platform=false,CustomMinimumSize=new(305,335),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};champion.AddChild(_winnerPreview);
        ArcadeTheme.Text(champion,"THE LAST ONE STANDING",17,ArcadeTheme.Gold).HorizontalAlignment=HorizontalAlignment.Center;
        _resultRows=ArcadeTheme.Column(body,2);_resultRows.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var scores=ArcadeTheme.Text(_results,"",1);scores.Name="Standings";scores.Visible=false;
        var row=ArcadeTheme.Row(_results);ArcadeTheme.Button(row,"PLAY AGAIN   →",()=>ReplayRequested?.Invoke(),true).SizeFlagsHorizontal=SizeFlags.ExpandFill;ArcadeTheme.Button(row,"MODE SELECT",()=>Navigation?.Invoke("Modes")).SizeFlagsHorizontal=SizeFlags.ExpandFill;
    }
    public void ShowResults(JsonElement? state,JsonElement? result)
    {
        var text="The battle has ended.";
        JsonElement? rows=null;
        if(result is { } final && final.TryGetProperty("details",out var details) && details.ValueKind==JsonValueKind.Object && details.TryGetProperty("scoreboard",out var scoreboard)) rows=scoreboard;
        else if(state is { } data && data.TryGetProperty("players",out var players)) rows=players;
        _winnerPreview.Visible=false;_resultTitle.Text="BATTLE COMPLETE!";
        if(rows is { } scores)
        {
            var ordered=scores.EnumerateArray().OrderByDescending(p=>p.GetProperty("wins").GetInt32()).ToArray();
            foreach(var child in _resultRows.GetChildren()){_resultRows.RemoveChild(child);child.QueueFree();}
            foreach(var (player,rank) in ordered.Select((p,i)=>(p,i)))AddResultRow(player,rank);
            text=string.Join("\n",ordered.Select((p,i)=>$"{i+1:00}    {p.GetProperty("name").GetString()}     {p.GetProperty("wins").GetInt32()} "+(p.GetProperty("wins").GetInt32()==1?"WIN":"WINS")));
            if(ordered.Length>0&&result is {} completed&&completed.TryGetProperty("reason",out var won)&&won.GetString() is "won" or "bot_won")
            {
                var leader=ordered[0];int appearance=leader.TryGetProperty("appearance",out var suit)?suit.GetInt32():leader.GetProperty("slot").GetInt32();
                _winnerPreview.SetAppearance(appearance);_winnerPreview.Celebrate();_winnerPreview.Visible=true;
                _resultTitle.Text=(leader.GetProperty("name").GetString()??"PLAYER").ToUpperInvariant()+" WINS!";
            }
        }
        if(result is { } r && r.TryGetProperty("reason",out var reason)) text+="\n\n"+(reason.GetString() switch {"bot_won"=>"CPU rival takes the match!","won"=>"Thanks for playing!",_=>reason.GetString()});
        _results.GetNode<Label>("Standings").Text=text;ShowScreen("Results");
    }
    private void AddResultRow(JsonElement player,int rank)
    {
        int slot=player.GetProperty("slot").GetInt32();
        int appearance=player.TryGetProperty("appearance",out var suit)?suit.GetInt32():slot;
        var panel=new PanelContainer {CustomMinimumSize=new(670,52)};
        panel.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(new Color(.06f,.2f,.43f,rank==0?.54f:.24f),new Color(1,1,1,.24f),1,2,3));_resultRows.AddChild(panel);
        var row=ArcadeTheme.Row(panel,14);
        var position=ArcadeTheme.Text(row,(rank+1).ToString("00"),22,ArcadeTheme.Gold);position.CustomMinimumSize=new(42,0);
        var character=new BomberPortrait {CustomMinimumSize=new(51,50),Suit=ArcadeTheme.Players[Math.Clamp(appearance,0,7)]};row.AddChild(character);
        var name=ArcadeTheme.Text(row,player.GetProperty("name").GetString()??"PLAYER",21);name.SizeFlagsHorizontal=SizeFlags.ExpandFill;name.ClipText=true;
        row.AddChild(new ResultMedals {Wins=player.GetProperty("wins").GetInt32(),CustomMinimumSize=new(190,45)});
    }
    public void ShowScreen(string screen)
    {
        _background.Page=screen;_background.Quiet=screen=="Roster";_background.Results=screen=="Results";_background.QueueRedraw();
        _screen=screen=="Practice"||screen=="HostSetup"?"Setup":screen;
        _practice=screen=="Practice";
        _setupTitle.Text=_practice?"PRACTICE SETUP":screen=="HostSetup"?"PRIVATE ROOM SETTINGS":"MATCH SETUP";
        _launch.Text=screen=="HostSetup"?"SAVE ROOM SETTINGS":_practice?"ENTER PRACTICE":"LET’S GO!";
        foreach(var dial in _ruleDials)dial.Disabled=_practice;
        _difficulty.Disabled=_practice;_bots.Editable=!_practice;_wins.Editable=!_practice;_rounds.Editable=!_practice;_sudden.Disabled=_practice;_ghosts.Disabled=_practice;
        _hostBotBox.Visible=screen=="HostSetup";_editPlayers.Visible=screen=="Setup";
        _rosterSummary.Text=_practice?"ONE CONTENDER  /  FREE TRAINING":screen=="HostSetup"?"ONLINE ROOM  /  1 HUMAN + CPU RIVALS":$"{_localPlayers.Roster.Humans} HUMAN + {_localPlayers.Roster.CpuPlayers} CPU  /  {_localPlayers.Roster.Participants} CONTENDERS";
        if(_practice)_notice.Text="Practice: collect the training power-ups. No timer or wins target.";
        else if(screen=="Setup")_notice.Text="Your roster and suits carry into the battle. Last survivor takes the round.";
        else if(screen=="HostSetup")_notice.Text="Room settings apply to the server. Each online client controls one human.";
        if(screen=="Roster"){_localPlayers.BotDifficulty=_difficulty.Selected+1;_localPlayers.Refresh();}
        foreach(var pair in _screens)pair.Value.Visible=pair.Key==_screen;
        if(screen=="Roster")_localPlayers.CallDeferred(nameof(RosterSelection.FocusFirst));
        else if(_screen=="Setup")_stageDial.CallDeferred(Control.MethodName.GrabFocus);
        else {var first=FirstFocusable(_screens[_screen]);first?.CallDeferred(Control.MethodName.GrabFocus);}
    }
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
        _joinCode.Text="ROOM CODE  "+(code??"MATCHMAKING");
        _lobbyStatus.Text=connection;
        if(lobby is not { } data)return;
        var status=LobbyStatus.Parse(data);
        if(status.JoinCode is { } roomCode)_joinCode.Text="ROOM CODE  "+roomCode;
        foreach(var child in _roster.GetChildren()){_roster.RemoveChild(child);child.QueueFree();}_lobbyPreviews.Clear();
        foreach(var member in status.Players)
        {
            var panel=new PanelContainer {CustomMinimumSize=new(440,55)};panel.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(new Color("202d57"),new Color("5c74a1"),1,2,5));_roster.AddChild(panel);
            var row=ArcadeTheme.Row(panel,10);var preview=new CharacterPreview {CustomMinimumSize=new(54,60),Selected=member.Connected};row.AddChild(preview);preview.SetAppearance(_lobbyAppearances.TryGetValue(member.Id,out int appearance)?appearance:member.Slot%8);_lobbyPreviews[member.Id]=preview;
            var name=ArcadeTheme.Text(row,$"P{member.Slot+1}  {member.Name}",17);name.SizeFlagsHorizontal=SizeFlags.ExpandFill;name.ClipText=true;
            ArcadeTheme.Text(row,member.Connected?"CONNECTED":"RECONNECTING",12,member.Connected?new Color("257766"):ArcadeTheme.Red);
        }
        _countdown=status.CountdownEndsAt;
        _lobbyStatus.Text=status.State switch {"countdown"=>"The battle is about to begin!","running"=>"Entering the arena…",_=>"Waiting for players…"};
    }
    public void ResetLobby()
    {
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
        if(_countdown is { } end)_lobbyStatus.Text=$"BATTLE BEGINS IN  {Math.Max(0,(int)Math.Ceiling((end-DateTimeOffset.UtcNow).TotalSeconds))}";
        else if(connection.StartsWith("Reconnecting")||connection.StartsWith("Error"))_lobbyStatus.Text=connection;
    }
    public void Configure(SessionSettings settings,int humans=1)
    {
        _difficulty.Select(Array.IndexOf(new[]{"easy","normal","hard"},settings.BotDifficulty));_stage.Select(Math.Max(0,Array.IndexOf(StageIds,settings.Stage)));_bots.Value=settings.Bots;_wins.Value=settings.Wins;_rounds.Value=settings.RoundSeconds;_sudden.ButtonPressed=settings.SuddenDeath;_ghosts.ButtonPressed=settings.Ghosts;_localPlayers.ConfigureCounts(humans,settings.Bots);UpdateStageDescription();
    }
    private void UpdateStageDescription()
    {
        _background.SelectArena(StageIds[_stage.Selected]);
        _stageDescription.Text=_stage.Selected switch {0=>"Climb the outer walkways. Send a pumped bomb\nover the ledge. Watch the water below.",1=>"An open arena with pillars and breakable blocks.\nFind an escape before lighting the fuse.",_=>"Fight across three connected terraces.\nUse ramps to move between elevations."};
    }
}

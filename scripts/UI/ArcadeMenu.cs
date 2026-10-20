using Godot;
using Bomber.Client;
using System.Text.Json;
namespace BlastCircuit.UI;

/// <summary>Mode, setup, connection, lobby and result screens; emits intent to the composition root.</summary>
public partial class ArcadeMenu : Control
{
    public event Action<string>? Navigation;
    public event Action? LocalRequested, ReplayRequested, BackRequested;
    public event Action<NetworkMode>? OnlineRequested;
    private Control _content=null!;
    private Label _notice=null!,_lobbyStatus=null!,_joinCode=null!;
    private GridContainer _roster=null!;
    private OptionButton _stage=null!;
    private Button _launch=null!;
    private Label _setupTitle=null!,_stageDescription=null!;
    private SpinBox _humans=null!,_bots=null!,_wins=null!,_rounds=null!;
    private CheckButton _sudden=null!,_ghosts=null!;
    private LineEdit _server=null!,_name=null!,_code=null!;
    private readonly Dictionary<string,Control> _screens=[];
    private bool _practice;
    private string _screen="Modes";
    private DateTimeOffset? _countdown;
    private readonly List<Button> _onlineButtons=[];
    public int HumanCount => (int)_humans.Value;
    public string ServerUrl=>_server.Text.Trim();
    public string PlayerName=>_name.Text.Trim();
    public string Code=>_code.Text.Trim();
    public bool Practice=>_practice;
    public SessionSettings Settings => new(StageIds[_stage.Selected],(int)_bots.Value,(int)_wins.Value,(int)_rounds.Value,_sudden.ButtonPressed,_ghosts.ButtonPressed);
    private static readonly string[] StageIds=["sunken-temple","rock-garden","up-and-down"];
    public override void _Ready()
    {
        Theme=ArcadeTheme.Create();SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background=new ArcadeBackdrop();AddChild(background);background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _content=new Control();AddChild(_content);_content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildModes();BuildSetup();BuildOnline();BuildLobby();BuildResults();
        var footer=new HBoxContainer {AnchorTop=1,AnchorBottom=1,AnchorRight=1,OffsetTop=-62,OffsetBottom=-8,OffsetLeft=58,OffsetRight=-58,Alignment=BoxContainer.AlignmentMode.Center};AddChild(footer);
        ArcadeTheme.Text(footer,"↑ ↓  SELECT     ENTER / A  CONFIRM     ESC / B  BACK",17);
        var spacer=new Control {SizeFlagsHorizontal=SizeFlags.ExpandFill};footer.AddChild(spacer);
        ArcadeTheme.Text(footer,"BURBUJA  •  LOCAL + ONLINE  •  2–8 PLAYERS",15);
        ShowScreen("Modes");
    }
    private Control Screen(string name)
    {var control=new Control();_content.AddChild(control);control.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);_screens[name]=control;return control;}
    private VBoxContainer Card(Node parent,Vector2 position,Vector2 size,Color? fill=null)
    {
        var panel=new PanelContainer {Position=position,Size=size};panel.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(fill??ArcadeTheme.Cream,ArcadeTheme.Ink,3,22,26));parent.AddChild(panel);return ArcadeTheme.Column(panel,14);
    }
    private void BuildModes()
    {
        var screen=Screen("Modes");
        var series=ArcadeTheme.Text(screen,"✦  BATTLE SERIES / 64",19);series.Position=new(78,70);
        var title=ArcadeTheme.Text(screen,"BLAST\nCIRCUIT",89);title.Position=new(70,112);title.AddThemeConstantOverride("outline_size",8);title.AddThemeColorOverride("font_outline_color",ArcadeTheme.Ink);title.AddThemeColorOverride("font_color",ArcadeTheme.Gold);title.AddThemeConstantOverride("line_spacing",-18);
        var tagline=ArcadeTheme.Text(screen,"SMALL ARENA. BIG BOOM.",22);tagline.Position=new(79,345);
        var left=new BomberPortrait {Position=new(90,410),Size=new(190,265),Suit=ArcadeTheme.Players[1],Rotation=-.12f};screen.AddChild(left);
        var right=new BomberPortrait {Position=new(430,420),Size=new(180,240),Suit=ArcadeTheme.Players[2],Rotation=.12f};screen.AddChild(right);
        var hero=new BomberPortrait {Position=new(235,365),Size=new(230,330),Suit=ArcadeTheme.Players[0],Hero=true};screen.AddChild(hero);
        var legend=ArcadeTheme.Text(screen,"LIFT  •  PUMP  •  THROW  •  SURVIVE",18);legend.Position=new(105,728);
        var card=Card(screen,new(744,112),new(610,644));
        ArcadeTheme.Text(card,"LET’S BATTLE!",38);
        ArcadeTheme.Text(card,"Pick a mode and jump into the arena.",18);
        Mode(card,"01     LOCAL BATTLE","Friends, controllers & CPU rivals",()=>Navigation?.Invoke("Setup"),true);
        Mode(card,"02     ONLINE BATTLE","Host a room or find your next rival",()=>Navigation?.Invoke("Online"));
        Mode(card,"03     PRACTICE","Explore the temple. Master every throw.",()=>Navigation?.Invoke("Practice"));
        ArcadeTheme.Text(card,"One keyboard supports two players.\nAdd up to six pads for eight local players.",16);
    }
    private static void Mode(Node parent,string title,string description,Action action,bool primary=false)
    {
        var button=ArcadeTheme.Button(parent,title+"\n"+description,action,primary);button.CustomMinimumSize=new(0,112);button.Alignment=HorizontalAlignment.Left;button.AddThemeFontSizeOverride("font_size",22);
    }
    private void BuildSetup()
    {
        var screen=Screen("Setup");var box=Card(screen,new(64,62),new(1312,724));
        var header=ArcadeTheme.Row(box);_setupTitle=ArcadeTheme.Text(header,"MATCH SETUP",38);_setupTitle.SizeFlagsHorizontal=SizeFlags.ExpandFill;ArcadeTheme.Button(header,"← BACK",()=>BackRequested?.Invoke());
        var columns=ArcadeTheme.Row(box,26);columns.SizeFlagsVertical=SizeFlags.ExpandFill;
        var left=ArcadeTheme.Column(columns,14);left.CustomMinimumSize=new(566,0);
        ArcadeTheme.Text(left,"01  /  CHOOSE YOUR ARENA",17);
        _stage=new OptionButton {CustomMinimumSize=new(0,58)};_stage.AddItem("SUNKEN TEMPLE  •  ramps & balconies");_stage.AddItem("ROCK GARDEN  •  classic arena");_stage.AddItem("UP & DOWN  •  terraced arena");left.AddChild(_stage);
        var preview=new StagePreview {CustomMinimumSize=new(550,285)};left.AddChild(preview);_stage.ItemSelected+=i=>{preview.StageIndex=(int)i;preview.QueueRedraw();UpdateStageDescription();};
        _stageDescription=ArcadeTheme.Text(left,"Climb the outer walkways. Send a pumped bomb\nover the ledge. Watch the water below.",18);
        var right=ArcadeTheme.Column(columns,14);right.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        ArcadeTheme.Text(right,"02  /  SET THE RULES",17);
        var row=ArcadeTheme.Row(right,16);_humans=Number(row,"HUMANS",1,8,1);_bots=Number(row,"CPU RIVALS",0,7,3);_wins=Number(row,"WINS",1,5,3);
        _rounds=Number(right,"ROUND TIME / SECONDS",60,300,120);_rounds.Step=30;
        _sudden=new CheckButton {Text="SUDDEN DEATH",ButtonPressed=true};right.AddChild(_sudden);
        _ghosts=new CheckButton {Text="GHOST COMEBACKS",ButtonPressed=true};right.AddChild(_ghosts);
        _notice=ArcadeTheme.Text(right,"2–8 total participants. CPU players fill empty seats.",16);_notice.AutowrapMode=TextServer.AutowrapMode.WordSmart;_notice.CustomMinimumSize=new(520,54);
        _launch=ArcadeTheme.Button(right,"LET’S GO!   →",()=>LocalRequested?.Invoke(),true);_launch.CustomMinimumSize=new(0,65);
        ArcadeTheme.Text(box,"P1  WASD + SPACE / SHIFT / R      P2  ARROWS + ENTER / CTRL / BACKSPACE      GAMEPAD  LEFT STICK + A / X / B",14);
    }
    private void BuildOnline()
    {
        var screen=Screen("Online");var box=Card(screen,new(116,62),new(1208,724));
        var header=ArcadeTheme.Row(box);ArcadeTheme.Text(header,"ONLINE BATTLE",38).SizeFlagsHorizontal=SizeFlags.ExpandFill;ArcadeTheme.Button(header,"← BACK",()=>BackRequested?.Invoke());
        ArcadeTheme.Text(box,"Your next rival is one room away.",20);
        var columns=ArcadeTheme.Row(box,30);
        var left=ArcadeTheme.Column(columns);left.CustomMinimumSize=new(525,0);
        _name=ArcadeTheme.Field(left,"PLAYER NAME","Player");_name.MaxLength=32;
        _server=ArcadeTheme.Field(left,"SERVER ADDRESS","http://127.0.0.1:18496");
        ArcadeTheme.Text(left,"PRIVATE ROOM",22);
        ArcadeTheme.Text(left,"Pick the stage, CPU count and rules in Match Setup.\nA room with CPU rivals can start with one human.",16);
        var configure=ArcadeTheme.Button(left,"MATCH SETUP",()=>Navigation?.Invoke("HostSetup"));
        _onlineButtons.Add(ArcadeTheme.Button(left,"CREATE PRIVATE ROOM   →",()=>OnlineRequested?.Invoke(NetworkMode.CreatePrivate),true));
        var right=ArcadeTheme.Column(columns);right.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        ArcadeTheme.Text(right,"JOIN A FRIEND",24);
        _code=ArcadeTheme.Field(right,"ROOM CODE","","Enter code");_code.MaxLength=16;
        _onlineButtons.Add(ArcadeTheme.Button(right,"JOIN ROOM   →",()=>OnlineRequested?.Invoke(NetworkMode.JoinCode),true));
        ArcadeTheme.Text(right,"OR FIND A MATCH",24);
        ArcadeTheme.Text(right,"Quick play matches real players using server rules.\nIt starts when at least two humans connect.",16);
        _onlineButtons.Add(ArcadeTheme.Button(right,"QUICK PLAY   →",()=>OnlineRequested?.Invoke(NetworkMode.QuickPlay)));
        var status=ArcadeTheme.Text(box,"Online play requires a reachable Burbuja server.",16);status.Name="OnlineNotice";status.AutowrapMode=TextServer.AutowrapMode.WordSmart;
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
        var screen=Screen("Results");_results=Card(screen,new(240,70),new(960,700));
        ArcadeTheme.Text(_results,"BATTLE COMPLETE!",43);
        var mascot=new BomberPortrait {CustomMinimumSize=new(0,170),Suit=ArcadeTheme.Gold,Hero=true};_results.AddChild(mascot);
        var scores=ArcadeTheme.Text(_results,"",23);scores.Name="Standings";scores.CustomMinimumSize=new(800,180);
        var row=ArcadeTheme.Row(_results);ArcadeTheme.Button(row,"PLAY AGAIN   →",()=>ReplayRequested?.Invoke(),true).SizeFlagsHorizontal=SizeFlags.ExpandFill;ArcadeTheme.Button(row,"MODE SELECT",()=>Navigation?.Invoke("Modes")).SizeFlagsHorizontal=SizeFlags.ExpandFill;
    }
    public void ShowResults(JsonElement? state,JsonElement? result)
    {
        var text="The battle has ended.";
        JsonElement? rows=null;
        if(result is { } final && final.TryGetProperty("details",out var details) && details.ValueKind==JsonValueKind.Object && details.TryGetProperty("scoreboard",out var scoreboard)) rows=scoreboard;
        else if(state is { } data && data.TryGetProperty("players",out var players)) rows=players;
        if(rows is { } scores)
            text=string.Join("\n",scores.EnumerateArray().OrderByDescending(p=>p.GetProperty("wins").GetInt32()).Select((p,i)=>$"{i+1:00}    {p.GetProperty("name").GetString()}     {p.GetProperty("wins").GetInt32()} WINS"));
        if(result is { } r && r.TryGetProperty("reason",out var reason)) text+="\n\n"+(reason.GetString() switch {"bot_won"=>"CPU rival takes the match!","won"=>"Thanks for playing!",_=>reason.GetString()});
        _results.GetNode<Label>("Standings").Text=text;ShowScreen("Results");
    }
    public void ShowScreen(string screen)
    {
        _screen=screen=="Practice"||screen=="HostSetup"?"Setup":screen;
        _practice=screen=="Practice";
        _setupTitle.Text=_practice?"PRACTICE SETUP":screen=="HostSetup"?"PRIVATE ROOM SETTINGS":"MATCH SETUP";
        _launch.Text=screen=="HostSetup"?"SAVE ROOM SETTINGS   →":_practice?"ENTER PRACTICE   →":"LET’S GO!   →";
        _humans.Editable=!_practice;_bots.Editable=!_practice;_wins.Editable=!_practice;_rounds.Editable=!_practice;_sudden.Disabled=_practice;_ghosts.Disabled=_practice;
        if(!_practice && (screen=="Setup"||screen=="HostSetup") && _humans.Value+_bots.Value<2)_bots.Value=3;
        if(_practice){_humans.Value=1;_bots.Value=0;_notice.Text="Practice: one player, clear arena, no time limit or wins target.";}
        else if(screen=="Setup"||screen=="HostSetup")_notice.Text="2–8 total participants. CPU players fill empty seats.";
        foreach(var pair in _screens)pair.Value.Visible=pair.Key==_screen;
        var first=FirstFocusable(_screens[_screen]);first?.CallDeferred(Control.MethodName.GrabFocus);
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
        foreach(var child in _roster.GetChildren()){_roster.RemoveChild(child);child.QueueFree();}
        foreach(var member in status.Players)
        {
            var panel=new PanelContainer {CustomMinimumSize=new(440,55)};panel.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(Colors.White,ArcadeTheme.Ink,2,12,5));_roster.AddChild(panel);
            var row=ArcadeTheme.Row(panel,10);row.AddChild(new BomberPortrait {CustomMinimumSize=new(35,48),Suit=ArcadeTheme.Players[member.Slot%8]});
            var name=ArcadeTheme.Text(row,$"P{member.Slot+1}  {member.Name}",17);name.SizeFlagsHorizontal=SizeFlags.ExpandFill;name.ClipText=true;
            ArcadeTheme.Text(row,member.Connected?"CONNECTED":"RECONNECTING",12,member.Connected?new Color("257766"):ArcadeTheme.Red);
        }
        _countdown=status.CountdownEndsAt;
        _lobbyStatus.Text=status.State switch {"countdown"=>"The battle is about to begin!","running"=>"Entering the arena…",_=>"Waiting for players…"};
    }
    public void UpdateConnection(string connection)
    {
        if(_countdown is { } end)_lobbyStatus.Text=$"BATTLE BEGINS IN  {Math.Max(0,(int)Math.Ceiling((end-DateTimeOffset.UtcNow).TotalSeconds))}";
        else if(connection.StartsWith("Reconnecting")||connection.StartsWith("Error"))_lobbyStatus.Text=connection;
    }
    public void Configure(SessionSettings settings,int humans=1)
    {
        _stage.Select(Math.Max(0,Array.IndexOf(StageIds,settings.Stage)));_bots.Value=settings.Bots;_wins.Value=settings.Wins;_rounds.Value=settings.RoundSeconds;_sudden.ButtonPressed=settings.SuddenDeath;_ghosts.ButtonPressed=settings.Ghosts;_humans.Value=humans;UpdateStageDescription();
    }
    private void UpdateStageDescription()
    {
        _stageDescription.Text=_stage.Selected switch {0=>"Climb the outer walkways. Send a pumped bomb\nover the ledge. Watch the water below.",1=>"An open arena with pillars and breakable blocks.\nFind an escape before lighting the fuse.",_=>"Fight across three connected terraces.\nUse ramps to move between elevations."};
    }
    private static SpinBox Number(Node parent,string title,int min,int max,int value)
    {
        var column=ArcadeTheme.Column(parent,6);column.SizeFlagsHorizontal=SizeFlags.ExpandFill;ArcadeTheme.Text(column,title,15);var spin=new SpinBox {MinValue=min,MaxValue=max,Value=value,CustomMinimumSize=new(150,50)};column.AddChild(spin);return spin;
    }
}

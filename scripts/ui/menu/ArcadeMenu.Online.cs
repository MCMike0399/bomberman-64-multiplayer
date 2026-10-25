using Godot;
using Bomber.Client;
using System.Text.Json;
namespace BlastCircuit.UI.Menu;

/// <summary>Online connection, lobby and party screens driven by actual server events.</summary>
public partial class ArcadeMenu
{
    public void SetOnlineAppearance(int appearance)
    {
        if(appearance is <0 or >7)throw new ArgumentOutOfRangeException(nameof(appearance));
        _onlineAppearance=appearance;_onlinePreview.SetAppearance(appearance);
        AppearanceChanged?.Invoke(appearance);
    }

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
        Section(host,"Play with friends");
        Copy(host,"Pick your rules and share a room code. Hosting is chosen automatically.");
        _onlineStage=new StagePreview {CustomMinimumSize=new(400,150),StageIndex=_stage.Selected};host.AddChild(_onlineStage);
        ArcadeTheme.Button(host,"Room settings",()=>Navigation?.Invoke("HostSetup"));
        host.AddChild(new ArcadeRuleDial {Name="PrivateHosting",Caption="Private hosting",ValueWidth=180,
            ReadValue=()=>new[]{"Automatic","Server only","Player hosted"}[_hostingPreference],
            ChangeValue=direction=>_hostingPreference=(_hostingPreference+direction+3)%3});
        _onlineButtons.Add(ArcadeTheme.Button(host,"Create private room",()=>OnlineRequested?.Invoke(NetworkMode.CreatePrivate),true));
        Hint(host,"Your room stays together between matches. You choose when to start.",15);
        var join=ArcadeTheme.Column(columns,14);join.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        Section(join,"Join a friend");
        _code=ArcadeTheme.Field(join,"Room code","","Enter code");_code.MaxLength=16;
        _onlineButtons.Add(ArcadeTheme.Button(join,"Join with code",()=>OnlineRequested?.Invoke(NetworkMode.JoinCode),true));
        join.AddChild(new Control {CustomMinimumSize=new(0,10)});
        Section(join,"Find players");
        Copy(join,"Meet other players. The battle starts automatically with at least two people and a suitable host.");
        _onlineButtons.Add(ArcadeTheme.Button(join,"Play online",()=>OnlineRequested?.Invoke(NetworkMode.QuickPlay)));
        var status=Copy(box,"Casual matches may run on a player's device. Peer connections share network addresses; a relay is available when needed.",15);status.Name="OnlineNotice";status.AddThemeColorOverride("font_color",ArcadeTheme.Gold);
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
        _startRoom.Disabled=!leader||!status.CanStart;_roomRules.Disabled=!leader||status.Phase!="lobby"||status.PublicRoom&&status.StartAt is not null;
        string leaderName=data.GetProperty("players").EnumerateArray().FirstOrDefault(p=>p.GetProperty("playerId").GetString()==status.LeaderId) is var player&&player.ValueKind==JsonValueKind.Object?player.GetProperty("displayName").GetString()??"Leader":"Leader";
        _lobbyStatus.Text=status.Phase switch {"starting"=>"Starting the battle...","results"=>"Battle complete — returning to the room...","playing"=>"Battle in progress",_=>leader?status.CanStart?"You are the host. Start when everyone is here.":status.MinimumHumans>=2?"Waiting for a second connected player.":"Waiting for another player or CPU rival.":$"Waiting for {leaderName} to start."};
        if(status.PublicRoom&&status.Phase=="lobby")_lobbyStatus.Text=status.CanStart?status.StartAt is not null?"The battle starts shortly...":"Finding a suitable host...":"Waiting for a second player.";
        _partyHint.Text=$"Room owner: {leaderName}  •  CPU rivals: {status.Bots}\n"+
            (status.Notice.Length>0?status.Notice:status.Hosting=="peer"?"If the host leaves, the next player hosts and everyone returns here.":"The host starts each match. Leadership transfers if they leave.");
    }
}

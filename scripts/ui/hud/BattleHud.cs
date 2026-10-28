using Godot;
using System.Text.Json;
using BlastCircuit.InputDevices;
using Bomber.Game.Matches;
namespace BlastCircuit.UI.Hud;

/// <summary>Fuse clock flanked by participant tokens; game state remains owned by the session.</summary>
public partial class BattleHud : Control
{
    public event Action? LeaveRequested;
    public event Action? RestartRequested;
    private Button _restart=null!;
    public bool Practice {get;set;}
    private HBoxContainer _leftTeam=null!,_rightTeam=null!;
    private FuseClock _clock=null!;
    private Label _phase=null!,_phaseDetail=null!,_connection=null!,_recovery=null!;
    private PanelContainer _hint=null!;
    private PromptBar _controls=null!;
    private readonly Dictionary<int,PlayerToken> _tokens=[];
    private string _signature="";
    private float _hurryAge,_bannerAge;
    private int _phaseNumber=-1,_round,_winsTarget=2;
    private float _roundSeconds;
    public override void _Ready()
    {
        Theme=ArcadeTheme.Create();MouseFilter=MouseFilterEnum.Ignore;SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _clock=new FuseClock {AnchorLeft=.5f,AnchorRight=.5f,OffsetLeft=-112,OffsetRight=112,OffsetTop=6,OffsetBottom=112};AddChild(_clock);
        _leftTeam=new HBoxContainer {AnchorLeft=.5f,AnchorRight=.5f,OffsetLeft=-720,OffsetRight=-120,OffsetTop=10,OffsetBottom=110,Alignment=BoxContainer.AlignmentMode.End,MouseFilter=MouseFilterEnum.Ignore};
        _rightTeam=new HBoxContainer {AnchorLeft=.5f,AnchorRight=.5f,OffsetLeft=120,OffsetRight=720,OffsetTop=10,OffsetBottom=110,Alignment=BoxContainer.AlignmentMode.Begin,MouseFilter=MouseFilterEnum.Ignore};
        foreach(var team in new[]{_leftTeam,_rightTeam}){team.AddThemeConstantOverride("separation",8);AddChild(team);}
        _phase=new Label {Name="RoundAnnouncement",AnchorLeft=.1f,AnchorRight=.9f,OffsetTop=150,OffsetBottom=240,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        _phase.AddThemeFontOverride("font",ArcadeTheme.Display);_phase.AddThemeFontSizeOverride("font_size",60);_phase.AddThemeColorOverride("font_color",ArcadeTheme.Gold);
        _phase.AddThemeColorOverride("font_outline_color",ArcadeTheme.Ink);_phase.AddThemeConstantOverride("outline_size",14);
        _phase.AddThemeColorOverride("font_shadow_color",ArcadeTheme.Ink);_phase.AddThemeConstantOverride("shadow_outline_size",14);_phase.AddThemeConstantOverride("shadow_offset_y",7);_phase.AddThemeConstantOverride("shadow_offset_x",0);
        AddChild(_phase);
        _phaseDetail=new Label {Name="RoundAnnouncementDetail",AnchorLeft=.1f,AnchorRight=.9f,OffsetTop=236,OffsetBottom=272,HorizontalAlignment=HorizontalAlignment.Center,ClipText=true,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};
        _phaseDetail.AddThemeFontSizeOverride("font_size",24);_phaseDetail.AddThemeConstantOverride("outline_size",7);_phaseDetail.AddThemeColorOverride("font_outline_color",ArcadeTheme.Ink);AddChild(_phaseDetail);
        _hint=new PanelContainer {AnchorLeft=.5f,AnchorRight=.5f,AnchorTop=1,AnchorBottom=1,OffsetLeft=-560,OffsetRight=560,OffsetTop=-116,OffsetBottom=-76,GrowHorizontal=GrowDirection.Both,MouseFilter=MouseFilterEnum.Ignore};
        var hintStyle=ArcadeTheme.Panel(new Color(ArcadeTheme.Night,.86f),new Color("394a8f"),2,20,0);hintStyle.ContentMarginLeft=hintStyle.ContentMarginRight=20;hintStyle.ContentMarginTop=5;hintStyle.ContentMarginBottom=7;
        _hint.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());AddChild(_hint);
        var hintCenter=new CenterContainer {MouseFilter=MouseFilterEnum.Ignore};_hint.AddChild(hintCenter);
        var hintPlate=new PanelContainer {MouseFilter=MouseFilterEnum.Ignore};hintPlate.AddThemeStyleboxOverride("panel",hintStyle);hintCenter.AddChild(hintPlate);
        _recovery=ArcadeTheme.Text(hintPlate,"",18,ArcadeTheme.Gold);_recovery.HorizontalAlignment=HorizontalAlignment.Center;
        var footer=new PanelContainer {AnchorTop=1,AnchorBottom=1,AnchorRight=1,OffsetLeft=24,OffsetRight=-24,OffsetTop=-66,OffsetBottom=-12};
        footer.AddThemeStyleboxOverride("panel",new StyleBoxEmpty {ContentMarginLeft=8,ContentMarginRight=8,ContentMarginTop=6,ContentMarginBottom=6});AddChild(footer);
        var row=ArcadeTheme.Row(footer,18);_connection=ArcadeTheme.Text(row,"Local battle",15,ArcadeTheme.Muted);_connection.SizeFlagsHorizontal=SizeFlags.ExpandFill;_connection.VerticalAlignment=VerticalAlignment.Center;
        _controls=new PromptBar {KeySize=13};row.AddChild(_controls);UpdateControls();ControllerHub.Current.PromptChanged+=UpdateControls;
        _restart=ArcadeTheme.Quiet(ArcadeTheme.Button(row,"Reset practice",()=>RestartRequested?.Invoke()));_restart.Visible=false;
        var escape=new HBoxContainer {MouseFilter=MouseFilterEnum.Ignore};escape.AddThemeConstantOverride("separation",0);row.AddChild(escape);
        escape.AddChild(PromptBar.Keycap("Esc",13));
        var leave=ArcadeTheme.Quiet(ArcadeTheme.Button(escape,"Menu",()=>LeaveRequested?.Invoke()));
        foreach(var button in new[]{_restart,leave}){button.CustomMinimumSize=new(0,40);button.AddThemeFontSizeOverride("font_size",15);button.FocusMode=FocusModeEnum.None;}
    }
    private void UpdateControls()=>_controls.SetPrompt(ControllerProfile.Actions(ControllerHub.Current.ActiveFamily,ControllerHub.Current.ActiveKeyboardSeat));
    public override void _ExitTree(){ControllerHub.Current.PromptChanged-=UpdateControls;}
    /// <summary>Round length and win target arrive with each stage snapshot.</summary>
    public void Stage(JsonElement stage)
    {
        if(stage.TryGetProperty("winsTarget",out var wins))_winsTarget=wins.GetInt32();
        if(stage.TryGetProperty("roundSeconds",out var seconds))_roundSeconds=seconds.GetSingle();
    }
    public void State(JsonElement state)
    {
        var players=state.GetProperty("players").EnumerateArray().OrderBy(p=>p.GetProperty("slot").GetInt32()).ToArray();
        var signature=string.Join(',',players.Select(p=>p.GetProperty("slot").GetInt32()));
        if(signature!=_signature){_signature=signature;BuildTokens(players);}
        foreach(var p in players)
        {
            var slot=p.GetProperty("slot").GetInt32();if(!_tokens.TryGetValue(slot,out var token))continue;
            var alive=p.GetProperty("alive").GetBoolean();var ghost=p.GetProperty("ghost").GetBoolean();
            bool held=p.TryGetProperty("carriedBy",out var carrier)&&carrier.ValueKind==JsonValueKind.String;
            int appearance=p.TryGetProperty("appearance",out var suit)?suit.GetInt32():slot;
            token.Suit=BlastCircuit.Rendering.ArtMaterials.PlayerColors[Math.Clamp(appearance,0,7)];
            token.Tag=Bomber.Game.Matches.PlayerNames.Display(p.GetProperty("name").GetString(),slot);
            token.PlayerName=p.GetProperty("bot").GetBoolean()?"CPU":"";
            if(p.TryGetProperty("statusEffect",out var status)&&status.GetString() is {} effect&&effect!="none")token.PlayerName=$"{effect} {Math.Ceiling(p.GetProperty("statusRemaining").GetSingle())}s";
            token.Status=ghost?TokenStatus.Ghost:!alive?TokenStatus.Out:held?TokenStatus.Held:p.GetProperty("stunned").GetBoolean()?TokenStatus.Dizzy:TokenStatus.Alive;
            token.Bombs=Math.Max(0,p.GetProperty("bombs").GetInt32()-Bomber.Game.Simulation.Rules.PowerUpRules.StartingBombs);token.Fire=Math.Max(0,p.GetProperty("fire").GetInt32()-1);
            token.Red=p.GetProperty("red").GetBoolean();token.Remote=p.GetProperty("remote").GetBoolean();token.Heart=p.GetProperty("heart").GetBoolean();
            token.BombType=p.TryGetProperty("bombType",out var type)?type.GetString()!:"normal";
            token.Focus=p.TryGetProperty("ability",out var ability)&&ability.GetString()=="focus";
            token.WinTarget=_winsTarget;token.Wins=p.GetProperty("wins").GetInt32();
            token.Refresh();
        }
        int phase=state.GetProperty("phase").GetInt32(),round=state.GetProperty("round").GetInt32();
        float timeLeft=state.GetProperty("timeLeft").GetSingle();int seconds=Math.Max(0,(int)Math.Ceiling(timeLeft));
        if(phase!=_phaseNumber){_hurryAge=0;_bannerAge=0;_phaseNumber=phase;}
        if(round!=_round){_round=round;if(_roundSeconds<=0)_roundSeconds=timeLeft;}
        _roundSeconds=Math.Max(_roundSeconds,timeLeft);
        bool flood=state.TryGetProperty("hurryMode",out var mode)&&mode.GetString()=="water";
        int waterDelay=state.TryGetProperty("waterStartsIn",out var delay)?(int)Math.Ceiling(delay.GetSingle()):0;
        _clock.Infinite=Practice;_clock.Hurry=phase==2;
        _clock.Time=Practice?"∞":$"{seconds/60}:{seconds%60:00}";
        _clock.Fuse=Practice||_roundSeconds<=0?1:Math.Clamp(timeLeft/_roundSeconds,0,1);
        bool meteors=state.TryGetProperty("hurryMode",out var hazardMode)&&hazardMode.GetString()=="meteors";
        bool avalanche=state.TryGetProperty("hurryMode",out var snowMode)&&snowMode.GetString()=="avalanche";
        _clock.Caption=Practice?"Practice":phase==2&&flood?(waterDelay>0?$"Flood in {waterDelay}":"Water rising"):phase==2&&meteors?"Fireballs falling":phase==2&&avalanche?"Avalanche closing":"Round "+round;
        _clock.QueueRedraw();
        // Accepted outcome, rather than score order: the leader may have lost this round.
        var ending=state.GetProperty("events").EnumerateArray().LastOrDefault(e=>e.GetProperty("round").GetInt32()==round&&e.GetProperty("kind").GetString()=="round-over");
        string? winnerId=ending.ValueKind==JsonValueKind.Object?ending.GetProperty("playerId").GetString():null;
        string? reason=ending.ValueKind==JsonValueKind.Object?ending.GetProperty("reason").GetString():null;
        var winner=players.FirstOrDefault(p=>p.GetProperty("id").GetString()==winnerId);
        string banner=phase switch{0=>"Get Ready!",2 when _hurryAge<3=>"Hurry Up!",3 when reason=="time_up"=>"Time's Up!",3 when winnerId is null=>"Draw!",3=>"Game!",_=>""};
        _phase.Text=banner;_phase.AddThemeColorOverride("font_color",phase==2?ArcadeTheme.Red:ArcadeTheme.Gold);
        _phaseDetail.Text=phase==2&&_hurryAge<3&&flood?"Get to high ground":phase==2&&_hurryAge<3&&avalanche?"Move away from the side walls":"";
        if(phase==3)
            _phaseDetail.Text=winner.ValueKind==JsonValueKind.Object
                ? $"{PlayerNames.Display(winner.GetProperty("name").GetString(),winner.GetProperty("slot").GetInt32())} wins the {(winner.GetProperty("wins").GetInt32()>=_winsTarget?"match":"round")}!"
                : reason=="time_up"?"Draw · No point awarded":"No survivors · No point awarded";
        _recovery.Text=phase==3?"":Hint(state,players);_hint.Visible=_recovery.Text.Length>0;
    }
    private static string Hint(JsonElement state,JsonElement[] players)
    {
        var recovering=players.Where(p=>p.GetProperty("alive").GetBoolean()&&!p.GetProperty("ghost").GetBoolean()&&p.GetProperty("stunned").GetBoolean()).ToArray();
        if(recovering.Length>0)return string.Join(", ",recovering.Select(p=>Bomber.Game.Matches.PlayerNames.Display(p.GetProperty("name").GetString(),p.GetProperty("slot").GetInt32())))+" dizzy: wiggle the stick and tap buttons to recover";
        var family=ControllerHub.Current.ActiveFamily;
        string primary=family==ControllerFamily.Keyboard?"J":ControllerProfile.BombName(family);
        string carry=family==ControllerFamily.Keyboard?"K":ControllerProfile.ButtonName(family,JoyButton.LeftShoulder)+" / "+ControllerProfile.ButtonName(family,ControllerProfile.CarryButton(family));
        string remote=family==ControllerFamily.Keyboard?"I / R":ControllerProfile.ButtonName(family,ControllerProfile.RemoteButton(family));
        foreach(var p in players.Where(p=>!p.GetProperty("bot").GetBoolean()&&p.GetProperty("alive").GetBoolean()&&!p.GetProperty("ghost").GetBoolean()))
        {
            int seat=p.GetProperty("slot").GetInt32();
            if(family==ControllerFamily.Keyboard){primary=seat==1?"N / ENTER":"J";carry=seat==1?"M":"K";remote=seat==1?", / BACKSPACE":"I / R";}
            string who=Bomber.Game.Matches.PlayerNames.Display(p.GetProperty("name").GetString(),seat);
            if(p.GetProperty("carrying").GetString()=="bomb")
            {
                bool latched=p.TryGetProperty("carryFromPrimary",out var primaryCarry)&&primaryCarry.GetBoolean();
                string aim=family==ControllerFamily.Keyboard?(seat==1?"Ctrl":"L"):ControllerProfile.AimName(family);
                string release=latched?$"{PromptBar.Spoken(carry)} throws":$"release {PromptBar.Spoken(carry)} to throw";
                var held=state.GetProperty("bombs").EnumerateArray().FirstOrDefault(b=>b.TryGetProperty("carrierId",out var id)&&id.GetString()==p.GetProperty("id").GetString());
                if(held.ValueKind!=JsonValueKind.Undefined&&held.TryGetProperty("charging",out var charging)&&charging.GetBoolean())
                    return $"{who}: charging {held.GetProperty("pumps").GetInt32()+1}/3 · {held.GetProperty("charge").GetSingle():P0} · {release}";
                if(p.TryGetProperty("aiming",out var aiming)&&aiming.GetBoolean())
                    return $"{who}: {(family==ControllerFamily.Keyboard?(seat==1?"arrows":"WASD"):"stick / D-pad")} moves the target · nudge to fine-tune · {release}";
                string point=family==ControllerFamily.Keyboard?(seat==1?"":"mouse or "):"right stick or ";
                return $"{who}: {PromptBar.Spoken(primary)} pumps · {point}hold {aim} to aim · {release}";
            }
            if(state.GetProperty("bombs").EnumerateArray().Any(b=>b.GetProperty("remote").GetBoolean()&&b.GetProperty("ownerId").GetString()==p.GetProperty("id").GetString()))
                return $"{who}'s remote bombs are waiting. {PromptBar.Spoken(remote)} sets them off";
        }
        return "";
    }
    private void BuildTokens(JsonElement[] players)
    {
        foreach(var team in new[]{_leftTeam,_rightTeam})foreach(var child in team.GetChildren()){team.RemoveChild(child);child.QueueFree();}
        _tokens.Clear();
        int leftCount=(players.Length+1)/2;
        for(int i=0;i<players.Length;i++)
        {
            int slot=players[i].GetProperty("slot").GetInt32();
            var token=new PlayerToken {Suit=ArcadeTheme.Players[slot%8]};(i<leftCount?_leftTeam:_rightTeam).AddChild(token);_tokens[slot]=token;
        }
    }
    public void Connection(string status,string? code,bool practice)
    {_restart.Visible=practice;_connection.Text=(practice?"Practice, ":"")+status+(code is null?"":", room "+code);}
    public override void _Process(double delta)
    {
        if(_phaseNumber==2)_hurryAge+=(float)delta;
        _bannerAge+=(float)delta;
        _clock.PivotOffset=_clock.Size/2;
        _clock.Scale=Vector2.One*(_phaseNumber==2?1+Mathf.Sin(_hurryAge*8)*.035f:1);
        // The banner slams in once per phase change rather than breathing constantly.
        _phase.PivotOffset=_phase.Size/2;
        float slam=Mathf.Clamp(_bannerAge/.28f,0,1);
        _phase.Scale=Vector2.One*(1+(1-slam)*(1-slam)*.6f);
        _phase.Modulate=new Color(1,1,1,Mathf.Clamp(_bannerAge/.12f,0,1));
    }
    public void Reset(){_signature="";_hurryAge=0;_phaseNumber=-1;_round=0;_roundSeconds=0;}
}

public enum TokenStatus {Alive,Dizzy,Held,Ghost,Out}

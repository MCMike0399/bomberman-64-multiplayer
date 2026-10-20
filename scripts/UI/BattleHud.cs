using Godot;
using System.Text.Json;
using BlastCircuit.InputDevices;
namespace BlastCircuit.UI;

/// <summary>Compact participant strip and timer; game state remains owned by the session.</summary>
public partial class BattleHud : Control
{
    public event Action? LeaveRequested;
    public event Action? RestartRequested;
    private Button _restart=null!;
    public bool Practice {get;set;}
    private HBoxContainer _strip=null!;
    private Label _timer=null!,_round=null!,_phase=null!,_connection=null!;
    private readonly Dictionary<int,(BomberPortrait Portrait,Label Name,Label Score,Label Stats,PanelContainer Panel)> _cards=[];
    private readonly Dictionary<int,WinMedals> _medals=[];
    private string _signature="";
    private JsonElement? _lastState;
    private float _hurryAge;
    private int _phaseNumber;
    private Label _controls=null!,_recovery=null!;
    public override void _Ready()
    {
        Theme=ArcadeTheme.Create();MouseFilter=MouseFilterEnum.Ignore;SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme.SetColor("font_outline_color","Label",ArcadeTheme.Ink);
        Theme.SetConstant("outline_size","Label",3);
        Theme.SetColor("font_outline_color","Button",ArcadeTheme.Ink);
        Theme.SetConstant("outline_size","Button",3);
        _strip=new HBoxContainer {AnchorRight=1,OffsetLeft=22,OffsetRight=-22,OffsetTop=7,OffsetBottom=110,Alignment=BoxContainer.AlignmentMode.Begin};_strip.AddThemeConstantOverride("separation",12);AddChild(_strip);
        _phase=new Label {AnchorLeft=.2f,AnchorRight=.8f,OffsetTop=124,OffsetBottom=176,HorizontalAlignment=HorizontalAlignment.Center};_phase.AddThemeFontSizeOverride("font_size",30);_phase.AddThemeColorOverride("font_color",Colors.White);_phase.AddThemeColorOverride("font_outline_color",ArcadeTheme.Ink);_phase.AddThemeConstantOverride("outline_size",8);AddChild(_phase);
        _recovery=new Label {AnchorLeft=.1f,AnchorRight=.9f,AnchorTop=1,AnchorBottom=1,OffsetTop=-109,OffsetBottom=-73,HorizontalAlignment=HorizontalAlignment.Center};
        _recovery.AddThemeFontSizeOverride("font_size",20);_recovery.AddThemeColorOverride("font_color",ArcadeTheme.Gold);_recovery.AddThemeConstantOverride("outline_size",6);AddChild(_recovery);
        var footer=new PanelContainer {AnchorTop=1,AnchorBottom=1,AnchorRight=1,OffsetLeft=24,OffsetRight=-24,OffsetTop=-69,OffsetBottom=-15};
        footer.AddThemeStyleboxOverride("panel",new StyleBoxEmpty {ContentMarginLeft=8,ContentMarginRight=8,ContentMarginTop=8,ContentMarginBottom=8});AddChild(footer);
        var row=ArcadeTheme.Row(footer,20);_connection=ArcadeTheme.Text(row,"LOCAL BATTLE",14);_connection.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        _controls=ArcadeTheme.Text(row,"",13);UpdateControls();ControllerHub.Current.PromptChanged+=UpdateControls;
        _restart=ArcadeTheme.Button(row,"RESET PRACTICE",()=>RestartRequested?.Invoke());_restart.Visible=false;_restart.CustomMinimumSize=new(140,36);_restart.AddThemeFontSizeOverride("font_size",14);
        _restart.FocusMode=FocusModeEnum.None;
        var leave=ArcadeTheme.Button(row,"LEAVE  [ESC]",()=>LeaveRequested?.Invoke());leave.CustomMinimumSize=new(140,36);leave.AddThemeFontSizeOverride("font_size",14);
        leave.FocusMode=FocusModeEnum.None;
        foreach(var button in new[]{_restart,leave})
        {
            button.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());
            button.AddThemeStyleboxOverride("hover",ArcadeTheme.Panel(new Color(1,1,1,.15f),Colors.White,1,5,8));
            button.AddThemeStyleboxOverride("pressed",ArcadeTheme.Panel(new Color(0,0,0,.12f),Colors.White,1,5,8));
        }
    }
    private void UpdateControls()=>_controls.Text=ControllerProfile.Actions(ControllerHub.Current.ActiveFamily,ControllerHub.Current.ActiveKeyboardSeat);
    public override void _ExitTree(){ControllerHub.Current.PromptChanged-=UpdateControls;}
    public void State(JsonElement state)
    {
        _lastState=state.Clone();
        var players=state.GetProperty("players").EnumerateArray().OrderBy(p=>p.GetProperty("slot").GetInt32()).ToArray();
        var signature=string.Join(',',players.Select(p=>p.GetProperty("slot").GetInt32()));
        if(signature!=_signature){_signature=signature;BuildCards(players);}
        foreach(var p in players)
        {
            var slot=p.GetProperty("slot").GetInt32();if(!_cards.TryGetValue(slot,out var card))continue;
            var alive=p.GetProperty("alive").GetBoolean();var ghost=p.GetProperty("ghost").GetBoolean();
            var eliminated=!alive&&!ghost;
            int appearance=p.TryGetProperty("appearance",out var suit)?suit.GetInt32():slot;
            card.Portrait.Suit=BlastCircuit.Rendering.ArtMaterials.PlayerColors[Math.Clamp(appearance,0,7)];
            card.Portrait.Eliminated=eliminated;card.Portrait.Ghost=ghost;card.Portrait.QueueRedraw();
            card.Name.Text="P"+(slot+1)+(p.GetProperty("bot").GetBoolean()?" CPU":" "+p.GetProperty("name").GetString());
            bool held=p.TryGetProperty("carriedBy",out var carrier)&&carrier.ValueKind==JsonValueKind.String;
            bool down=p.GetProperty("stunned").GetBoolean();
            card.Score.Text=ghost?"↻ GHOST":eliminated?"× OUT":held?"HELD":down?"DOWN":"● ALIVE";card.Score.AddThemeColorOverride("font_color",ghost?new Color("7cd8f1"):eliminated?ArcadeTheme.Red:down?ArcadeTheme.Gold:new Color("74e69f"));
            _medals[slot].Wins=p.GetProperty("wins").GetInt32();
            card.Stats.Text=$"B {p.GetProperty("bombs").GetInt32()}  F {p.GetProperty("fire").GetInt32()}"+(p.GetProperty("red").GetBoolean()?"  P":"")+(p.GetProperty("remote").GetBoolean()?"  R":"")+(p.GetProperty("heart").GetBoolean()?"  ♥":"");
            card.Stats.Modulate=eliminated?new Color(.6f,.6f,.6f):Colors.White;
        }
        int phase=state.GetProperty("phase").GetInt32(),seconds=Math.Max(0,(int)Math.Ceiling(state.GetProperty("timeLeft").GetSingle()));
        if(phase!=_phaseNumber){_hurryAge=0;_phaseNumber=phase;}
        bool flood=state.TryGetProperty("hurryMode",out var mode)&&mode.GetString()=="water";
        _timer.Text=Practice?"∞":$"{seconds/60}:{seconds%60:00}";
        int waterDelay=state.TryGetProperty("waterStartsIn",out var delay)?(int)Math.Ceiling(delay.GetSingle()):0;
        _round.Text=Practice?"PRACTICE":phase==2&&flood?(waterDelay>0?$"FLOOD IN {waterDelay}":"WATER RISING"):"ROUND "+state.GetProperty("round").GetInt32();
        _timer.AddThemeColorOverride("font_color",phase==2?ArcadeTheme.Red:ArcadeTheme.Gold);
        _phase.Text=phase switch{0=>"GET READY!",1=>"",2 when _hurryAge<3=>flood?"HURRY UP!  GET TO HIGH GROUND":"HURRY UP!",2=>"",3=>"ROUND OVER",_=>""};
        var recovering=players.Where(p=>p.GetProperty("alive").GetBoolean()&&!p.GetProperty("ghost").GetBoolean()&&p.GetProperty("stunned").GetBoolean()).ToArray();
        _recovery.Text=recovering.Length==0?"":string.Join(", ",recovering.Select(p=>"P"+(p.GetProperty("slot").GetInt32()+1)))+"  •  WIGGLE STICK / ALTERNATE MOVEMENT + TAP ACTIONS TO RECOVER";
    }
    private void BuildCards(JsonElement[] players)
    {
        foreach(var child in _strip.GetChildren()){_strip.RemoveChild(child);child.QueueFree();}_cards.Clear();_medals.Clear();
        for(int i=0;i<=players.Length;i++)
        {
            if(i==0)
            {
                var column=ArcadeTheme.Column(_strip,0);column.CustomMinimumSize=new(170,100);
                _round=ArcadeTheme.Text(column,"ROUND 1",13);_round.HorizontalAlignment=HorizontalAlignment.Center;
                var clockRow=ArcadeTheme.Row(column,4);clockRow.Alignment=BoxContainer.AlignmentMode.Center;
                clockRow.AddChild(new BattleClockIcon {CustomMinimumSize=new(33,65)});
                _timer=ArcadeTheme.Text(clockRow,"2:00",45,ArcadeTheme.Gold);_timer.HorizontalAlignment=HorizontalAlignment.Center;
                _timer.Resized+=()=>_timer.PivotOffset=_timer.Size/2;
                _timer.AddThemeColorOverride("font_outline_color",ArcadeTheme.Ink);_timer.AddThemeConstantOverride("outline_size",9);
            }
            if(i==players.Length)continue;
            var p=players[i];int slot=p.GetProperty("slot").GetInt32();
            var card=new PanelContainer {CustomMinimumSize=new(130,98)};card.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(new Color(1,1,1,0),ArcadeTheme.Ink,0,0,0));_strip.AddChild(card);
            var column2=ArcadeTheme.Column(card,0);
            var name=ArcadeTheme.Text(column2,"",12);name.HorizontalAlignment=HorizontalAlignment.Center;name.CustomMinimumSize=new(126,0);name.ClipText=true;name.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
            var row=ArcadeTheme.Row(column2,0);row.Alignment=BoxContainer.AlignmentMode.Center;
            var portrait=new BomberPortrait {CustomMinimumSize=new(58,57),Suit=ArcadeTheme.Players[slot%8],HeadOnly=true};row.AddChild(portrait);
            var info=ArcadeTheme.Column(row,0);var medals=new WinMedals {CustomMinimumSize=new(64,40)};info.AddChild(medals);_medals[slot]=medals;
            var score=ArcadeTheme.Text(info,"",11);var stats=ArcadeTheme.Text(column2,"",10);stats.HorizontalAlignment=HorizontalAlignment.Center;
            _cards[slot]=(portrait,name,score,stats,card);
        }
    }
    public void Connection(string status,string? code,bool practice)
    {_restart.Visible=practice;_connection.Text=(practice?"PRACTICE  ·  ":"")+status+(code is null?"":"  ·  ROOM "+code);}
    public override void _Process(double delta)
    {
        if(_phaseNumber==2)_hurryAge+=(float)delta;
        if(_timer!=null)_timer.Scale=Vector2.One*(_phaseNumber==2?1+Mathf.Sin(_hurryAge*8)*.045f:1);
        _phase.PivotOffset=_phase.Size/2;
        _phase.Scale=Vector2.One*(_phaseNumber==2&&_hurryAge<3?1+Mathf.Abs(Mathf.Sin(_hurryAge*7))*.09f:1);
    }
    public void Reset(){_signature="";_lastState=null;_hurryAge=0;_phaseNumber=0;}
}

/// <summary>Small stamped victory medals keep the round score readable at a glance.</summary>
public partial class WinMedals : Control
{
    private int _wins;
    private bool _known;
    private float _pop;
    public int Wins {get=>_wins;set{if(_known&&value>_wins)_pop=1;_known=true;if(value!=_wins){_wins=value;QueueRedraw();}}}
    public override void _Process(double delta)
    {if(_pop>0){_pop=Mathf.MoveToward(_pop,0,(float)delta*1.8f);QueueRedraw();}}
    public override void _Ready()=>MouseFilter=MouseFilterEnum.Ignore;
    public override void _Draw()
    {
        float scale=1+Mathf.Sin(_pop*Mathf.Pi)*.32f;
        DrawSetTransform(new Vector2(32,20)*(1-scale),0,new(scale,scale));
        var font=ThemeDB.FallbackFont;
        DrawStringOutline(font,new(18,29),Wins.ToString(),HorizontalAlignment.Left,-1,31,6,ArcadeTheme.Ink);
        DrawString(font,new(18,29),Wins.ToString(),HorizontalAlignment.Left,-1,31,ArcadeTheme.Gold);
        for(int i=0;i<Math.Max(3,Math.Min(5,Wins));i++)
        {
            var center=new Vector2(7+i*12,36);bool won=i<Wins;
            DrawCircle(center,3,won?ArcadeTheme.Ink:new Color(ArcadeTheme.Ink,.2f));
            DrawCircle(center,2,won?ArcadeTheme.Gold:new Color(1,1,1,.4f));
            if(won)DrawLine(center+new Vector2(-1,-2),center+new Vector2(1,2),Colors.White,1.5f,true);
        }
    }
}

/// <summary>The red clock is a single readable symbol beside the remaining time.</summary>
public partial class BattleClockIcon : Control
{
    public override void _Ready()=>MouseFilter=MouseFilterEnum.Ignore;
    public override void _Draw()
    {
        var center=new Vector2(16,31);
        DrawRect(new Rect2(2,16,28,30),ArcadeTheme.Ink);
        DrawRect(new Rect2(4,18,24,26),ArcadeTheme.Red);
        DrawCircle(center,10,ArcadeTheme.Cream);DrawArc(center,10,0,Mathf.Tau,32,ArcadeTheme.Ink,2,true);
        DrawLine(center,center+new Vector2(0,-7),ArcadeTheme.Ink,2,true);DrawLine(center,center+new Vector2(6,2),ArcadeTheme.Ink,2,true);
        DrawRect(new Rect2(12,12,8,4),ArcadeTheme.Red);
    }
}

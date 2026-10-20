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
    private string _signature="";
    private JsonElement? _lastState;
    private float _hurryAge;
    private int _phaseNumber;
    private Label _controls=null!;
    public override void _Ready()
    {
        Theme=ArcadeTheme.Create();MouseFilter=MouseFilterEnum.Ignore;SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _strip=new HBoxContainer {AnchorRight=1,OffsetLeft=24,OffsetRight=-24,OffsetTop=18,OffsetBottom=112,Alignment=BoxContainer.AlignmentMode.Center};_strip.AddThemeConstantOverride("separation",8);AddChild(_strip);
        _phase=new Label {AnchorLeft=.2f,AnchorRight=.8f,OffsetTop=124,OffsetBottom=176,HorizontalAlignment=HorizontalAlignment.Center};_phase.AddThemeFontSizeOverride("font_size",30);_phase.AddThemeColorOverride("font_color",Colors.White);_phase.AddThemeColorOverride("font_outline_color",ArcadeTheme.Ink);_phase.AddThemeConstantOverride("outline_size",8);AddChild(_phase);
        var footer=new PanelContainer {AnchorTop=1,AnchorBottom=1,AnchorRight=1,OffsetLeft=24,OffsetRight=-24,OffsetTop=-69,OffsetBottom=-15};footer.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(ArcadeTheme.Cream,ArcadeTheme.Ink,2,14,8));AddChild(footer);
        var row=ArcadeTheme.Row(footer,20);_connection=ArcadeTheme.Text(row,"LOCAL BATTLE",14);_connection.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        _controls=ArcadeTheme.Text(row,"",13);UpdateControls();ControllerHub.Current.PromptChanged+=UpdateControls;
        _restart=ArcadeTheme.Button(row,"RESET PRACTICE",()=>RestartRequested?.Invoke());_restart.Visible=false;_restart.CustomMinimumSize=new(140,36);_restart.AddThemeFontSizeOverride("font_size",14);
        _restart.FocusMode=FocusModeEnum.None;
        var leave=ArcadeTheme.Button(row,"LEAVE  [ESC]",()=>LeaveRequested?.Invoke());leave.CustomMinimumSize=new(140,36);leave.AddThemeFontSizeOverride("font_size",14);
        leave.FocusMode=FocusModeEnum.None;
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
            card.Portrait.Eliminated=eliminated;card.Portrait.QueueRedraw();
            card.Name.Text=(p.GetProperty("bot").GetBoolean()?"CPU ":"P"+(slot+1)+" ")+p.GetProperty("name").GetString();
            card.Score.Text=$"★ {p.GetProperty("wins").GetInt32()}  ·  {(ghost?"GHOST":eliminated?"OUT":"IN PLAY")}";
            card.Stats.Text=$"B {p.GetProperty("bombs").GetInt32()}  F {p.GetProperty("fire").GetInt32()}"+(p.GetProperty("red").GetBoolean()?"  P":"")+(p.GetProperty("remote").GetBoolean()?"  R":"")+(p.GetProperty("heart").GetBoolean()?"  ♥":"");
            card.Panel.Modulate=eliminated?new Color(.68f,.68f,.68f):Colors.White;
        }
        int phase=state.GetProperty("phase").GetInt32(),seconds=Math.Max(0,(int)Math.Ceiling(state.GetProperty("timeLeft").GetSingle()));
        if(phase!=_phaseNumber){_hurryAge=0;_phaseNumber=phase;}
        bool flood=state.TryGetProperty("hurryMode",out var mode)&&mode.GetString()=="water";
        _timer.Text=Practice?"∞":$"{seconds/60}:{seconds%60:00}";
        int waterDelay=state.TryGetProperty("waterStartsIn",out var delay)?(int)Math.Ceiling(delay.GetSingle()):0;
        _round.Text=Practice?"PRACTICE":phase==2&&flood?(waterDelay>0?$"FLOOD IN {waterDelay}":"WATER RISING"):"ROUND "+state.GetProperty("round").GetInt32();
        _timer.AddThemeColorOverride("font_color",seconds<=60&&phase==2?ArcadeTheme.Red:ArcadeTheme.Ink);
        _phase.Text=phase switch{0=>"GET READY!",1=>"",2 when _hurryAge<3=>flood?"HURRY UP!  GET TO HIGH GROUND":"HURRY UP!",2=>"",3=>"ROUND OVER",_=>""};
    }
    private void BuildCards(JsonElement[] players)
    {
        foreach(var child in _strip.GetChildren()){_strip.RemoveChild(child);child.QueueFree();}_cards.Clear();
        int halfway=(players.Length+1)/2;
        for(int i=0;i<=players.Length;i++)
        {
            if(i==halfway)
            {
                var panel=new PanelContainer {CustomMinimumSize=new(108,90)};panel.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(ArcadeTheme.Gold,ArcadeTheme.Ink,3,15,6));_strip.AddChild(panel);
                var column=ArcadeTheme.Column(panel,0);_round=ArcadeTheme.Text(column,"ROUND 1",12);_round.HorizontalAlignment=HorizontalAlignment.Center;_timer=ArcadeTheme.Text(column,"2:00",31);_timer.HorizontalAlignment=HorizontalAlignment.Center;
            }
            if(i==players.Length)continue;
            var p=players[i];int slot=p.GetProperty("slot").GetInt32();
            var card=new PanelContainer {CustomMinimumSize=new(146,90)};card.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(ArcadeTheme.Cream,ArcadeTheme.Ink,2,13,6));_strip.AddChild(card);
            var row=ArcadeTheme.Row(card,2);var portrait=new BomberPortrait {CustomMinimumSize=new(46,68),Suit=ArcadeTheme.Players[slot%8]};row.AddChild(portrait);
            var column2=ArcadeTheme.Column(row,3);column2.SizeFlagsHorizontal=SizeFlags.ExpandFill;var name=ArcadeTheme.Text(column2,"",12);name.CustomMinimumSize=new(80,0);name.ClipText=true;name.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
            var score=ArcadeTheme.Text(column2,"",11);var stats=ArcadeTheme.Text(column2,"",10);_cards[slot]=(portrait,name,score,stats,card);
        }
    }
    public void Connection(string status,string? code,bool practice)
    {_restart.Visible=practice;_connection.Text=(practice?"PRACTICE  ·  ":"")+status+(code is null?"":"  ·  ROOM "+code);}
    public override void _Process(double delta){if(_phaseNumber==2)_hurryAge+=(float)delta;}
    public void Reset(){_signature="";_lastState=null;_hurryAge=0;_phaseNumber=0;}
}

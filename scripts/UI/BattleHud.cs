using Godot;
using System.Text.Json;
using BlastCircuit.InputDevices;
namespace BlastCircuit.UI;

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
    private int _phaseNumber=-1,_round,_winsTarget=3;
    private float _roundSeconds;
    public override void _Ready()
    {
        Theme=ArcadeTheme.Create();MouseFilter=MouseFilterEnum.Ignore;SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _clock=new FuseClock {AnchorLeft=.5f,AnchorRight=.5f,OffsetLeft=-112,OffsetRight=112,OffsetTop=6,OffsetBottom=112};AddChild(_clock);
        _leftTeam=new HBoxContainer {AnchorLeft=.5f,AnchorRight=.5f,OffsetLeft=-720,OffsetRight=-120,OffsetTop=10,OffsetBottom=110,Alignment=BoxContainer.AlignmentMode.End,MouseFilter=MouseFilterEnum.Ignore};
        _rightTeam=new HBoxContainer {AnchorLeft=.5f,AnchorRight=.5f,OffsetLeft=120,OffsetRight=720,OffsetTop=10,OffsetBottom=110,Alignment=BoxContainer.AlignmentMode.Begin,MouseFilter=MouseFilterEnum.Ignore};
        foreach(var team in new[]{_leftTeam,_rightTeam}){team.AddThemeConstantOverride("separation",8);AddChild(team);}
        _phase=new Label {AnchorLeft=.1f,AnchorRight=.9f,OffsetTop=150,OffsetBottom=240,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        _phase.AddThemeFontOverride("font",ArcadeTheme.Display);_phase.AddThemeFontSizeOverride("font_size",60);_phase.AddThemeColorOverride("font_color",ArcadeTheme.Gold);
        _phase.AddThemeColorOverride("font_outline_color",ArcadeTheme.Ink);_phase.AddThemeConstantOverride("outline_size",14);
        _phase.AddThemeColorOverride("font_shadow_color",ArcadeTheme.Ink);_phase.AddThemeConstantOverride("shadow_outline_size",14);_phase.AddThemeConstantOverride("shadow_offset_y",7);_phase.AddThemeConstantOverride("shadow_offset_x",0);
        AddChild(_phase);
        _phaseDetail=new Label {AnchorLeft=.1f,AnchorRight=.9f,OffsetTop=236,OffsetBottom=272,HorizontalAlignment=HorizontalAlignment.Center};
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
        var leave=ArcadeTheme.Quiet(ArcadeTheme.Button(escape,"Leave",()=>LeaveRequested?.Invoke()));
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
            token.Tag="P"+(slot+1);
            token.PlayerName=p.GetProperty("bot").GetBoolean()?"CPU":p.GetProperty("name").GetString()??"";
            token.Status=ghost?TokenStatus.Ghost:!alive?TokenStatus.Out:held?TokenStatus.Held:p.GetProperty("stunned").GetBoolean()?TokenStatus.Dizzy:TokenStatus.Alive;
            token.Bombs=p.GetProperty("bombs").GetInt32();token.Fire=p.GetProperty("fire").GetInt32();
            token.Red=p.GetProperty("red").GetBoolean();token.Remote=p.GetProperty("remote").GetBoolean();token.Heart=p.GetProperty("heart").GetBoolean();
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
        _clock.Caption=Practice?"Practice":phase==2&&flood?(waterDelay>0?$"Flood in {waterDelay}":"Water rising"):"Round "+round;
        _clock.QueueRedraw();
        string banner=phase switch{0=>"Get Ready!",2 when _hurryAge<3=>"Hurry Up!",3=>"Round Over",_=>""};
        _phase.Text=banner;_phase.AddThemeColorOverride("font_color",phase==2?ArcadeTheme.Red:ArcadeTheme.Gold);
        _phaseDetail.Text=phase==2&&_hurryAge<3&&flood?"Get to high ground":"";
        _recovery.Text=Hint(state,players);_hint.Visible=_recovery.Text.Length>0;
    }
    private static string Hint(JsonElement state,JsonElement[] players)
    {
        var recovering=players.Where(p=>p.GetProperty("alive").GetBoolean()&&!p.GetProperty("ghost").GetBoolean()&&p.GetProperty("stunned").GetBoolean()).ToArray();
        if(recovering.Length>0)return string.Join(", ",recovering.Select(p=>"P"+(p.GetProperty("slot").GetInt32()+1)))+" dizzy: wiggle the stick and tap buttons to recover";
        var family=ControllerHub.Current.ActiveFamily;
        string primary=family==ControllerFamily.Keyboard?"J":ControllerProfile.BombName(family);
        string carry=family==ControllerFamily.Keyboard?"K":ControllerProfile.ButtonName(family,JoyButton.LeftShoulder)+" / "+ControllerProfile.ButtonName(family,ControllerProfile.CarryButton(family));
        string remote=family==ControllerFamily.Keyboard?"I / R":ControllerProfile.ButtonName(family,ControllerProfile.RemoteButton(family));
        foreach(var p in players.Where(p=>!p.GetProperty("bot").GetBoolean()&&p.GetProperty("alive").GetBoolean()&&!p.GetProperty("ghost").GetBoolean()))
        {
            int seat=p.GetProperty("slot").GetInt32();
            if(family==ControllerFamily.Keyboard){primary=seat==1?"N / ENTER":"J";carry=seat==1?"M":"K";remote=seat==1?", / BACKSPACE":"I / R";}
            string who="P"+(seat+1);
            if(p.GetProperty("carrying").GetString()=="bomb")
            {
                bool latched=p.TryGetProperty("carryFromPrimary",out var primaryCarry)&&primaryCarry.GetBoolean();
                string aim=family==ControllerFamily.Keyboard?(seat==1?"Ctrl":"L"):ControllerProfile.AimName(family);
                string release=latched?$"{PromptBar.Spoken(carry)} throws":$"release {PromptBar.Spoken(carry)} to throw";
                if(p.TryGetProperty("aiming",out var aiming)&&aiming.GetBoolean())
                    return $"{who}: {(family==ControllerFamily.Keyboard?(seat==1?"arrows":"WASD"):"either stick")} moves the target · nudge to fine-tune · {release}";
                return $"{who}: {PromptBar.Spoken(primary)} pumps · hold {aim} to aim · {release}";
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

/// <summary>One participant: helmet portrait, number, wins, current upgrades and condition.</summary>
public partial class PlayerToken : Control
{
    public Color Suit {get;set;}=Colors.White;
    public string Tag {get;set;}="P1";
    public string PlayerName {get;set;}="";
    public int Bombs {get;set;}
    public int Fire {get;set;}
    public bool Red {get;set;}
    public bool Remote {get;set;}
    public bool Heart {get;set;}
    public int WinTarget {get;set;}=3;
    public TokenStatus Status {get;set;}
    private int _wins=-1;
    private float _pop;
    private BomberPortrait _portrait=null!;
    public int Wins {get=>Math.Max(0,_wins);set{if(_wins>=0&&value>_wins)_pop=1;_wins=value;}}
    public override void _Ready()
    {
        CustomMinimumSize=new(128,98);MouseFilter=MouseFilterEnum.Ignore;
        _portrait=new BomberPortrait {HeadOnly=true,Position=new(0,0),Size=new(54,52),Suit=Suit};AddChild(_portrait);
    }
    public void Refresh()
    {
        if(_portrait!=null)
        {
            _portrait.Suit=Suit;_portrait.Eliminated=Status==TokenStatus.Out;_portrait.Ghost=Status==TokenStatus.Ghost;_portrait.QueueRedraw();
        }
        QueueRedraw();
    }
    public override void _Process(double delta)
    {if(_pop>0){_pop=Mathf.MoveToward(_pop,0,(float)delta*1.6f);QueueRedraw();}}
    public override void _Draw()
    {
        var ink=ArcadeTheme.Ink;bool outOfRound=Status==TokenStatus.Out;
        var plate=ArcadeTheme.Panel(new Color(ArcadeTheme.Night,outOfRound?.55f:.86f),Status==TokenStatus.Ghost?ArcadeTheme.Aqua:new Color("394a8f"),Status==TokenStatus.Ghost?3:2,16,0);
        plate.ShadowColor=new Color(ink,.8f);plate.ShadowSize=1;plate.ShadowOffset=new(0,4);
        DrawStyleBox(plate,new Rect2(2,16,Size.X-4,Size.Y-20));
        var display=ArcadeTheme.Display;var body=ArcadeTheme.Body;
        Outlined(display,new(58,43),Tag,17,outOfRound?ArcadeTheme.Muted:ArcadeTheme.Gold,5);
        // Wins: one star per point toward the target; the newest star pops when earned.
        // Win stars are pinned along the plate's top edge, clear of the number and name.
        int stars=Math.Clamp(Math.Max(WinTarget,Wins),1,5);const float step=13;
        for(int i=0;i<stars;i++)
        {
            var center=new Vector2(Size.X-14-(stars-1-i)*step,16);
            float scale=i==Wins-1?1+Mathf.Sin(_pop*Mathf.Pi)*.8f:1;
            ArcadeIcons.Star(this,center,5.4f*scale,i<Wins?ArcadeTheme.Gold:new Color("4a5288"));
        }
        if(outOfRound){Stamp("Out",ArcadeTheme.Red,-.14f);return;}
        Outlined(body,new(58,61),Fit(body,PlayerName,13,Size.X-64),13,ArcadeTheme.Cream,4);
        float y=80;
        ArcadeIcons.Bomb(this,new(16,y),6.5f,new Color("6271c4"),.55f);
        Outlined(display,new(26,y+6),Bombs.ToString(),15,ArcadeTheme.Cream,4);
        ArcadeIcons.Flame(this,new(50,y+1),9);
        Outlined(display,new(59,y+6),Fire.ToString(),15,ArcadeTheme.Cream,4);
        float x=88;
        if(Red){ArcadeIcons.Bomb(this,new(x,y),6,ArcadeTheme.Red,.5f);x+=17;}
        if(Remote){ArcadeIcons.Bomb(this,new(x,y+1),6,new Color("3b62b8"),0,0,true);x+=17;}
        if(Heart&&x<Size.X-6)ArcadeIcons.Heart(this,new(x,y),6.5f);
        if(Status is TokenStatus.Dizzy or TokenStatus.Held)Badge(Status==TokenStatus.Held?"Held":"Dizzy",ArcadeTheme.Gold);
        else if(Status==TokenStatus.Ghost)Badge("Ghost",ArcadeTheme.Aqua);
    }
    private void Outlined(Font font,Vector2 at,string text,int size,Color color,int outline)
    {DrawStringOutline(font,at,text,HorizontalAlignment.Left,-1,size,outline,ArcadeTheme.Ink);DrawString(font,at,text,HorizontalAlignment.Left,-1,size,color);}
    private static string Fit(Font font,string text,int size,float width)
    {
        if(font.GetStringSize(text,HorizontalAlignment.Left,-1,size).X<=width)return text;
        while(text.Length>1&&font.GetStringSize(text+"…",HorizontalAlignment.Left,-1,size).X>width)text=text[..^1];
        return text.TrimEnd()+"…";
    }
    private void Stamp(string text,Color color,float angle)
    {
        var font=ArcadeTheme.Display;const int size=24;
        var width=font.GetStringSize(text,HorizontalAlignment.Left,-1,size).X;
        DrawSetTransform(new(Size.X/2+26,Size.Y/2+14),angle,Vector2.One);
        DrawStringOutline(font,new(-width/2,size*.36f),text,HorizontalAlignment.Left,-1,size,8,ArcadeTheme.Ink);
        DrawString(font,new(-width/2,size*.36f),text,HorizontalAlignment.Left,-1,size,color);
        DrawSetTransform(Vector2.Zero,0,Vector2.One);
    }
    private void Badge(string text,Color fill)
    {
        var font=ArcadeTheme.Body;const int size=12;
        var width=font.GetStringSize(text,HorizontalAlignment.Left,-1,size).X+14;
        var rect=new Rect2(27-width/2,44,width,18);
        var style=ArcadeTheme.Panel(fill,ArcadeTheme.Ink,2,9,0);DrawStyleBox(style,rect);
        DrawString(font,rect.Position+new Vector2(7,14),text,HorizontalAlignment.Left,-1,size,ArcadeTheme.Ink);
    }
}

/// <summary>The round timer as a lit bomb: its fuse burns toward the bomb as the round runs out.</summary>
public partial class FuseClock : Control
{
    public string Time {get;set;}="2:00";
    public string Caption {get;set;}="Round 1";
    public float Fuse {get;set;}=1;
    public bool Hurry {get;set;}
    public bool Infinite {get;set;}
    private float _clock;
    public override void _Ready()=>MouseFilter=MouseFilterEnum.Ignore;
    public override void _Process(double delta){if(!IsVisibleInTree())return;_clock+=(float)delta;QueueRedraw();}
    public override void _Draw()
    {
        var ink=ArcadeTheme.Ink;
        var plateRect=new Rect2(34,22,Size.X-40,Size.Y-34);
        var plate=ArcadeTheme.Panel(new Color(ArcadeTheme.Night,.9f),ink,3,24,0);plate.ShadowColor=ink;plate.ShadowSize=1;plate.ShadowOffset=new(0,5);
        DrawStyleBox(plate,plateRect);
        // Unburnt fuse runs along the plate's top edge from the bomb cap to the spark.
        var bomb=new Vector2(40,Size.Y/2+2)+(Hurry?new Vector2(Mathf.Sin(_clock*41)*1.6f,Mathf.Cos(_clock*37)*1.2f):Vector2.Zero);
        const float r=29;
        var start=bomb+new Vector2(.52f,-.62f)*r;
        float endX=plateRect.End.X-18,fuseY=plateRect.Position.Y;
        var path=new List<Vector2> {start,new(start.X+14,fuseY-2)};
        for(float x=start.X+24;x<=endX;x+=6)path.Add(new(x,fuseY+Mathf.Sin(x*.21f)*1.5f));
        float total=0;for(int i=1;i<path.Count;i++)total+=path[i].DistanceTo(path[i-1]);
        float keep=total*Mathf.Clamp(Fuse,0,1),run=0;var lit=new List<Vector2> {path[0]};
        for(int i=1;i<path.Count&&run<keep;i++)
        {
            float segment=path[i].DistanceTo(path[i-1]);
            if(run+segment<=keep)lit.Add(path[i]);else lit.Add(path[i-1].Lerp(path[i],(keep-run)/segment));
            run+=segment;
        }
        for(int i=1;i<path.Count;i+=2)if(path[i].X>lit[^1].X+4)DrawCircle(path[i],1.2f,new Color(ArcadeTheme.Cream,.18f));
        if(lit.Count>1){DrawPolyline(lit.ToArray(),ink,8,true);DrawPolyline(lit.ToArray(),new Color("c99a5b"),4,true);}
        if(!Infinite)ArcadeIcons.Spark(this,lit[^1],10+Mathf.Sin(_clock*23)*2.5f);
        bool flash=Hurry&&Mathf.PosMod(_clock,.5f)<.12f;
        ArcadeIcons.Bomb(this,bomb,r,flash?new Color("ffb3a8"):Hurry?ArcadeTheme.Red:new Color("2c3157"),0);
        var display=ArcadeTheme.Display;const int size=40;
        float textX=plateRect.Position.X+40,width=plateRect.End.X-12-textX;
        var digits=new Vector2(textX,plateRect.Position.Y+size+6);
        var color=Hurry?new Color("ff8d80"):ArcadeTheme.Gold;
        DrawStringOutline(display,digits+new Vector2(0,4),Time,HorizontalAlignment.Center,width,size,10,ink);
        DrawStringOutline(display,digits,Time,HorizontalAlignment.Center,width,size,10,ink);
        DrawString(display,digits,Time,HorizontalAlignment.Center,width,size,color);
        var body=ArcadeTheme.Body;
        var captionAt=new Vector2(textX,plateRect.End.Y-9);
        DrawStringOutline(body,captionAt,Caption,HorizontalAlignment.Center,width,14,4,ink);
        DrawString(body,captionAt,Caption,HorizontalAlignment.Center,width,14,Hurry?new Color("ffb3a8"):ArcadeTheme.Cream);
    }
}

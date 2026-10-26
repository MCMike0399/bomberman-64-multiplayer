using Godot;
namespace BlastCircuit.UI.Hud;

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
    public string BombType {get;set;}="normal";
    public bool Focus {get;set;}
    public int WinTarget {get;set;}=2;
    public TokenStatus Status {get;set;}
    private int _wins=-1;
    private float _pop;
    private BomberPortrait _portrait=null!;
    private DisplayState? _displayed;
    private readonly record struct DisplayState(Color Suit,string Tag,string Name,int Bombs,int Fire,bool Red,bool Remote,
        bool Heart,string BombType,bool Focus,int WinTarget,TokenStatus Status,int Wins);
    public int Wins {get=>Math.Max(0,_wins);set{if(_wins>=0&&value>_wins){_pop=1;SetProcess(true);}_wins=value;}}
    public override void _Ready()
    {
        CustomMinimumSize=new(128,98);MouseFilter=MouseFilterEnum.Ignore;
        _portrait=new BomberPortrait {HeadOnly=true,Position=new(0,0),Size=new(54,52),Suit=Suit};AddChild(_portrait);
        _displayed=null;Refresh();SetProcess(_pop>0);
    }
    public void Refresh()
    {
        var state=new DisplayState(Suit,Tag,PlayerName,Bombs,Fire,Red,Remote,Heart,BombType,Focus,WinTarget,Status,Wins);
        if(_displayed==state)return;
        _displayed=state;
        if(_portrait!=null)
        {
            bool eliminated=Status==TokenStatus.Out,ghost=Status==TokenStatus.Ghost;
            if(_portrait.Suit!=Suit||_portrait.Eliminated!=eliminated||_portrait.Ghost!=ghost)
            {_portrait.Suit=Suit;_portrait.Eliminated=eliminated;_portrait.Ghost=ghost;_portrait.QueueRedraw();}
        }
        QueueRedraw();
    }
    public override void _Process(double delta)
    {if(_pop>0){_pop=Mathf.MoveToward(_pop,0,(float)delta*1.6f);QueueRedraw();}if(_pop<=0)SetProcess(false);}
    public override void _Draw()
    {
        var ink=ArcadeTheme.Ink;bool outOfRound=Status==TokenStatus.Out;
        var plate=ArcadeTheme.Panel(new Color(ArcadeTheme.Night,outOfRound?.55f:.86f),Status==TokenStatus.Ghost?ArcadeTheme.Aqua:new Color("394a8f"),Status==TokenStatus.Ghost?3:2,16,0);
        plate.ShadowColor=new Color(ink,.8f);plate.ShadowSize=1;plate.ShadowOffset=new(0,4);
        DrawStyleBox(plate,new Rect2(2,16,Size.X-4,Size.Y-20));
        var display=ArcadeTheme.Display;var body=ArcadeTheme.Body;
        Outlined(display,new(8,61),Fit(display,Tag,13,Size.X-16),13,outOfRound?ArcadeTheme.Muted:ArcadeTheme.Gold,5);
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
        if(Status==TokenStatus.Alive)Outlined(body,new(58,43),Fit(body,PlayerName,13,Size.X-64),13,ArcadeTheme.Cream,4);
        float y=80;
        ArcadeIcons.Bomb(this,new(16,y),6.5f,new Color("6271c4"),.55f);
        Outlined(display,new(26,y+6),Bombs.ToString(),15,ArcadeTheme.Cream,4);
        ArcadeIcons.Flame(this,new(50,y+1),9);
        Outlined(display,new(59,y+6),Fire.ToString(),15,ArcadeTheme.Cream,4);
        float x=88;
        if(Red){ArcadeIcons.Bomb(this,new(x,y),6,ArcadeTheme.Red,.5f);x+=17;}
        if(Remote){ArcadeIcons.Bomb(this,new(x,y+1),6,new Color("3b62b8"),0,0,true);x+=17;}
        if(BombType=="bounce")
        {ArcadeIcons.Bomb(this,new(x,y),6,new Color("a365ea"),.5f);x+=17;}
        if(Focus){DrawArc(new(x,y),5,0,Mathf.Tau,20,ArcadeTheme.Aqua,2);DrawCircle(new(x,y),1.5f,ArcadeTheme.Cream);}
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
        DrawSetTransform(new(Size.X/2+26,38),angle,Vector2.One);
        DrawStringOutline(font,new(-width/2,size*.36f),text,HorizontalAlignment.Left,-1,size,8,ArcadeTheme.Ink);
        DrawString(font,new(-width/2,size*.36f),text,HorizontalAlignment.Left,-1,size,color);
        DrawSetTransform(Vector2.Zero,0,Vector2.One);
    }
    private void Badge(string text,Color fill)
    {
        var font=ArcadeTheme.Body;const int size=12;
        var width=font.GetStringSize(text,HorizontalAlignment.Left,-1,size).X+14;
        var rect=new Rect2(Size.X-width-6,28,width,18);
        var style=ArcadeTheme.Panel(fill,ArcadeTheme.Ink,2,9,0);DrawStyleBox(style,rect);
        DrawString(font,rect.Position+new Vector2(7,14),text,HorizontalAlignment.Left,-1,size,ArcadeTheme.Ink);
    }
}

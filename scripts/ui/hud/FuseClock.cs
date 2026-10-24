using Godot;
namespace BlastCircuit.UI.Hud;

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

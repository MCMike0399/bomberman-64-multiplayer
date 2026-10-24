using Godot;
namespace BlastCircuit.UI;

/// <summary>A native CheckButton whose switch is drawn as vector art, centered on the label rather than the key's lip.</summary>
public partial class ArcadeToggle : CheckButton
{
    private const float TrackWidth=46,TrackHeight=26;
    private static ImageTexture? _blank;
    private float _knob=-1;
    public override void _Ready()
    {
        MouseDefaultCursorShape=CursorShape.PointingHand;
        // Transparent icons reserve the switch's layout width; _Draw paints it at output resolution.
        _blank??=ImageTexture.CreateFromImage(Image.CreateEmpty((int)TrackWidth,(int)TrackHeight,false,Image.Format.Rgba8));
        foreach(var icon in new[]{"checked","unchecked","checked_disabled","unchecked_disabled","checked_mirrored","unchecked_mirrored","checked_disabled_mirrored","unchecked_disabled_mirrored"})
            AddThemeIconOverride(icon,_blank);
    }
    public override void _Process(double delta)
    {
        float target=ButtonPressed?1:0;
        if(_knob<0){_knob=target;QueueRedraw();return;}
        if(Mathf.IsEqualApprox(_knob,target))return;
        _knob=Mathf.MoveToward(_knob,target,(float)delta*7);QueueRedraw();
    }
    public override void _Draw()
    {
        var style=GetThemeStylebox("normal");
        float right=Size.X-style.GetMargin(Side.Right);
        // Godot centers label text inside the content margins; match that line, not the full rect.
        float y=(Size.Y+style.GetMargin(Side.Top)-style.GetMargin(Side.Bottom))/2;
        float radius=TrackHeight/2,left=right-TrackWidth;
        var a=new Vector2(left+radius,y);var b=new Vector2(right-radius,y);
        var on=Disabled?new Color("8f93b4"):ArcadeTheme.Aqua.Darkened(.12f);
        var off=new Color("7f84a8");
        Capsule(a,b,radius+2,ArcadeTheme.Ink);
        Capsule(a,b,radius-1,off.Lerp(on,Mathf.Clamp(_knob,0,1)));
        var knob=a.Lerp(b,Mathf.SmoothStep(0,1,Mathf.Clamp(_knob,0,1)));
        Disc(knob+new Vector2(0,1.5f),radius-3,new Color(ArcadeTheme.Ink,.35f));
        Disc(knob,radius-3,ArcadeTheme.Ink);Disc(knob,radius-5,ArcadeTheme.Cream);
    }
    private void Disc(Vector2 center,float radius,Color color)=>DrawCircle(center,radius,color,true,-1,true);
    private void Capsule(Vector2 a,Vector2 b,float radius,Color color)
    {
        Disc(a,radius,color);Disc(b,radius,color);
        DrawRect(new Rect2(a.X,a.Y-radius,b.X-a.X,radius*2),color);
    }
}

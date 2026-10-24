using Godot;
namespace BlastCircuit.UI.Menu;

/// <summary>The title's bomb: a short fuse with a spark that never quite reaches it.</summary>
public partial class LitBomb : Control
{
    private float _clock;
    public override void _Ready()=>MouseFilter=MouseFilterEnum.Ignore;
    public override void _Process(double delta){if(!IsVisibleInTree())return;_clock+=(float)delta;QueueRedraw();}
    public override void _Draw()
    {
        float flicker=1+Mathf.Sin(_clock*19)*.18f+Mathf.Sin(_clock*31)*.1f;
        float r=Size.X*.27f;var center=new Vector2(Size.X*.36f,Size.Y*.64f);
        var cap=center+new Vector2(.52f,-.62f)*r;
        // A long curled fuse, drawn under the bomb so the cap hides its root.
        var curve=new Curve2D();curve.AddPoint(cap,Vector2.Zero,new(10,-14));curve.AddPoint(cap+new Vector2(34,-28),new(-14,6),Vector2.Zero);
        var rope=curve.Tessellate(4,4);
        DrawPolyline(rope,ArcadeTheme.Ink,8,true);DrawPolyline(rope,new Color("c99a5b"),4,true);
        DrawCircle(rope[^1],15*flicker,new Color(ArcadeTheme.Gold,.22f));
        ArcadeIcons.Spark(this,rope[^1],15*flicker);
        ArcadeIcons.Bomb(this,center,r,new Color("2c3157"),0);
    }
}

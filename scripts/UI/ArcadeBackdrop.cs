using Godot;
namespace BlastCircuit.UI;

/// <summary>Print-like rays and confetti frame the menu without external bitmap assets.</summary>
public partial class ArcadeBackdrop : Control
{
    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;Resized+=QueueRedraw;}
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),ArcadeTheme.Cream);
        var origin=new Vector2(Size.X*.25f,Size.Y*.47f);
        for(int i=0;i<22;i++)
        {
            float angle=i*Mathf.Tau/22;
            DrawColoredPolygon([origin,origin+Vector2.FromAngle(angle)*Size.X*2,origin+Vector2.FromAngle(angle+.09f)*Size.X*2],new Color("ffebad"));
        }
        DrawCircle(new(Size.X*.2f,Size.Y*.53f),Size.Y*.25f,new Color("ffdc75"));
        for(int i=0;i<24;i++)
        {
            float x=((i*317+41)%1400)/1440f*Size.X,y=((i*193+111)%860)/900f*Size.Y;
            var color=i%3==0?ArcadeTheme.Pink:i%3==1?ArcadeTheme.Aqua:ArcadeTheme.Gold;
            DrawSetTransform(new(x,y),i*.64f);DrawRect(new Rect2(-5,-10,10,20),color);
        }
        DrawSetTransform(Vector2.Zero);
        DrawLine(new(0,Size.Y-70),new(Size.X,Size.Y-70),ArcadeTheme.Ink,3);
        DrawRect(new Rect2(0,Size.Y-67,Size.X,67),new Color("ffe6a1"));
    }
}

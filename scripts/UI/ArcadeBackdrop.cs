using Godot;
namespace BlastCircuit.UI;

/// <summary>Print-like rays and confetti frame the menu without external bitmap assets.</summary>
public partial class ArcadeBackdrop : Control
{
    public bool Quiet {get;set;}
    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;Resized+=QueueRedraw;}
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),ArcadeTheme.Cream);
        var origin=new Vector2(Size.X*.25f,Size.Y*.47f);
        for(int i=0;i<(Quiet?0:22);i++)
        {
            float angle=i*Mathf.Tau/22;
            DrawColoredPolygon([origin,origin+Vector2.FromAngle(angle)*Size.X*2,origin+Vector2.FromAngle(angle+.09f)*Size.X*2],new Color("ffebad"));
        }
        if(!Quiet)DrawCircle(new(Size.X*.2f,Size.Y*.53f),Size.Y*.25f,new Color("ffdc75"));
        for(int i=0;i<(Quiet?0:9);i++)
        {
            float x=((i*317+41)%1400)/1440f*Size.X,y=((i*193+111)%860)/900f*Size.Y;
            var color=i%3==0?ArcadeTheme.Pink:i%3==1?ArcadeTheme.Aqua:ArcadeTheme.Gold;
            DrawSetTransform(new(x,y),i*.64f);DrawRect(new Rect2(-5,-10,10,20),color);
        }
        DrawSetTransform(Vector2.Zero);
        // The broad sand-colored floor makes the characters read as a lineup on a stage.
        DrawColoredPolygon([new(0,Size.Y*.72f),new(Size.X,Size.Y*.72f),new(Size.X,Size.Y-70),new(0,Size.Y-70)],new Color("f1ce79"));
        DrawLine(new(0,Size.Y*.72f),new(Size.X,Size.Y*.72f),new Color("d7aa50"),3);
        for(int i=0;i<(Quiet?0:11);i++)DrawLine(new(Size.X*.5f+(i-5)*Size.X*.08f,Size.Y*.72f),new(Size.X*.5f+(i-5)*Size.X*.17f,Size.Y-70),new Color("dfb966"),1);
        DrawLine(new(0,Size.Y-70),new(Size.X,Size.Y-70),ArcadeTheme.Ink,3);
        DrawRect(new Rect2(0,Size.Y-67,Size.X,67),new Color("ffe6a1"));
    }
}

using Godot;
namespace BlastCircuit.UI;

/// <summary>Original ink-outlined vector icons shared by HUD, roster, rules and results.</summary>
public static class ArcadeIcons
{
    private static Color Ink=>ArcadeTheme.Ink;
    private static Vector2[] Circle(Vector2 center,float radius,int segments=28)=>
        Enumerable.Range(0,segments).Select(i=>center+Vector2.FromAngle(i*Mathf.Tau/segments)*radius).ToArray();
    /// <summary>Round bomb with cap and fuse. <paramref name="fuse"/> scales the fuse; a positive spark lights its tip.</summary>
    public static void Bomb(CanvasItem c,Vector2 center,float r,Color body,float fuse=1,float spark=0,bool antenna=false)
    {
        var cap=center+new Vector2(.52f,-.62f)*r;
        if(antenna)
        {
            c.DrawLine(cap,cap+new Vector2(.1f,-.75f)*r,Ink,Math.Max(2,r*.16f),true);
            c.DrawCircle(cap+new Vector2(.1f,-.8f)*r,r*.2f+1.5f,Ink);c.DrawCircle(cap+new Vector2(.1f,-.8f)*r,r*.2f,ArcadeTheme.Red);
        }
        else if(fuse>0)
        {
            var tip=FusePoint(cap,r,fuse);
            c.DrawPolyline(FusePath(cap,r,fuse),new Color("8a6a3a"),Math.Max(2,r*.13f),true);
            if(spark>0)Spark(c,tip,r*.42f*spark);
        }
        c.DrawCircle(center,r+Math.Max(2,r*.12f),Ink);
        c.DrawCircle(center,r,body);
        // Sheen and cap make a flat disc read as a glossy sphere at 14 px.
        c.DrawCircle(center+new Vector2(-.34f,-.36f)*r,r*.26f,new Color(1,1,1,.55f));
        c.DrawCircle(center+new Vector2(-.12f,-.52f)*r,r*.1f,new Color(1,1,1,.4f));
        c.DrawSetTransform(cap,Mathf.Pi/4,Vector2.One);
        c.DrawRect(new Rect2(new Vector2(-.32f,-.2f)*r,new Vector2(.64f,.4f)*r),Ink);
        c.DrawRect(new Rect2(new Vector2(-.22f,-.11f)*r,new Vector2(.44f,.22f)*r),new Color("c9a257"));
        c.DrawSetTransform(Vector2.Zero,0,Vector2.One);
    }
    private static Vector2[] FusePath(Vector2 cap,float r,float fuse)=>Enumerable.Range(0,9).Select(i=>FusePoint(cap,r,fuse*i/8f)).ToArray();
    private static Vector2 FusePoint(Vector2 cap,float r,float t)=>cap+new Vector2(t*.75f,-t*.25f-Mathf.Sin(t*Mathf.Pi)*.35f)*r;
    public static void Spark(CanvasItem c,Vector2 at,float size)
    {
        if(size<=0)return;
        for(int i=0;i<8;i++)
        {
            var direction=Vector2.FromAngle(i*Mathf.Tau/8+.2f);float length=i%2==0?size:size*.55f;
            c.DrawLine(at+direction*size*.18f,at+direction*length,i%2==0?ArcadeTheme.Gold:ArcadeTheme.Red,Math.Max(1.5f,size*.16f),true);
        }
        c.DrawCircle(at,size*.24f,Colors.White);
    }
    public static void Flame(CanvasItem c,Vector2 center,float s,Color? outer=null)
    {
        Vector2[] Shape(float scale,Vector2 offset)
        {
            var points=new List<Vector2>();
            for(int i=0;i<=16;i++){float a=Mathf.DegToRad(-35+i*250f/16);points.Add(offset+new Vector2(Mathf.Cos(a)*.55f,.28f+Mathf.Sin(a)*.55f)*s*scale);}
            points.Add(offset+new Vector2(-.18f,-.35f)*s*scale);points.Add(offset+new Vector2(.12f,-1.0f)*s*scale);points.Add(offset+new Vector2(.36f,-.32f)*s*scale);
            return points.ToArray();
        }
        var shell=Shape(1,center);
        c.DrawColoredPolygon(Shape(1.22f,center+new Vector2(0,-.02f)*s),Ink);
        c.DrawColoredPolygon(shell,outer??new Color("f2793c"));
        c.DrawColoredPolygon(Shape(.55f,center+new Vector2(0,.2f)*s),ArcadeTheme.Gold);
    }
    public static void Heart(CanvasItem c,Vector2 center,float s,Color? fill=null)
    {
        Vector2[] Shape(float scale)=>Enumerable.Range(0,32).Select(i=>
        {
            float t=i*Mathf.Tau/32;
            return center+new Vector2(16*Mathf.Pow(Mathf.Sin(t),3),-(13*Mathf.Cos(t)-5*Mathf.Cos(2*t)-2*Mathf.Cos(3*t)-Mathf.Cos(4*t)))*s*scale/17f;
        }).ToArray();
        c.DrawColoredPolygon(Shape(1.25f),Ink);c.DrawColoredPolygon(Shape(1),fill??ArcadeTheme.Pink);
        c.DrawCircle(center+new Vector2(-.42f,-.32f)*s,s*.16f,new Color(1,1,1,.6f));
    }
    /// <summary>A solid arrowhead with an ink edge; <paramref name="direction"/> is -1 left, 1 right, 2 down, -2 up.</summary>
    public static void Chevron(CanvasItem c,Vector2 center,float s,int direction,Color fill)
    {
        Vector2[] triangle=[new(.6f,0),new(-.45f,-.62f),new(-.45f,.62f)];
        float angle=direction switch {-1=>Mathf.Pi,2=>Mathf.Pi/2,-2=>-Mathf.Pi/2,_=>0};
        var points=triangle.Select(p=>center+p.Rotated(angle)*s).ToArray();
        c.DrawColoredPolygon(points,fill);
        c.DrawPolyline([..points,points[0]],Ink,Math.Max(2,s*.2f),true);
    }
    public static void Star(CanvasItem c,Vector2 center,float r,Color fill,bool outline=true)
    {
        Vector2[] Shape(float radius)=>Enumerable.Range(0,10).Select(i=>center+Vector2.FromAngle(-Mathf.Pi/2+i*Mathf.Pi/5)*(i%2==0?radius:radius*.48f)).ToArray();
        if(outline)c.DrawColoredPolygon(Shape(r+Math.Max(2,r*.28f)),Ink);
        c.DrawColoredPolygon(Shape(r),fill);
    }
    /// <summary>A small rasterized suit swatch, used as a native Button icon.</summary>
    public static ImageTexture Swatch(Color color,int size=22)
    {
        var image=Image.CreateEmpty(size,size,false,Image.Format.Rgba8);
        float center=(size-1)/2f,outer=size/2f-.5f,inner=outer-2.5f;
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            float d=new Vector2(x-center,y-center).Length();
            var pixel=d<=inner?color:d<=outer?Ink:new Color(0,0,0,0);
            if(d>inner-1&&d<=inner)pixel=pixel.Lerp(Ink,d-(inner-1));
            if(d>outer-1&&d<=outer)pixel=new Color(Ink,1-(d-(outer-1)));
            image.SetPixel(x,y,pixel);
        }
        return ImageTexture.CreateFromImage(image);
    }
}

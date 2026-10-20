using Godot;
namespace BlastCircuit.UI;

/// <summary>Original vector mascot, shared by mode selection, lobby seats and participant cards.</summary>
public partial class BomberPortrait : Control
{
    public Color Suit { get; set; } = Colors.White;
    public bool Eliminated { get; set; }
    public bool HeadOnly { get; set; }
    public bool Ghost { get; set; }
    public bool Hero { get; set; }
    public override void _Ready() { MouseFilter=MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        if(HeadOnly){DrawHead();return;}
        var scale=Math.Min(Size.X/120,Size.Y/150);
        DrawSetTransform(new Vector2((Size.X-120*scale)/2,(Size.Y-150*scale)/2),0,new Vector2(scale,scale));
        var ink=ArcadeTheme.Ink;var pink=ArcadeTheme.Pink;var suit=Eliminated?new Color("c9c8c3"):Suit;
        DrawEllipse(new(60,140),new(45,7),new Color(ink,.13f));
        // Boots and gloved arms make the silhouette readable at HUD size.
        Capsule(new(38,126),new(18,13),ink);Capsule(new(82,126),new(18,13),ink);Capsule(new(38,124),new(15,10),pink);Capsule(new(82,124),new(15,10),pink);
        Capsule(new(60,102),new(30,29),ink);Capsule(new(60,100),new(27,26),Suit.R>.9f&&Suit.G>.9f&&Suit.B>.85f?new Color("3b62b8"):suit);
        Capsule(new(27,103),new(12,13),ink);Capsule(new(93,103),new(12,13),ink);Capsule(new(27,101),new(9,10),pink);Capsule(new(93,101),new(9,10),pink);
        DrawRect(new Rect2(34,109,52,9),ink);DrawRect(new Rect2(53,108,14,11),ArcadeTheme.Gold);DrawRect(new Rect2(57,111,6,5),ink);
        Capsule(new(60,54),new(47,39),ink);Capsule(new(60,52),new(43,35),suit);
        DrawLine(new(60,18),new(60,10),ink,7,true);DrawCircle(new(60,8),11,ink);DrawCircle(new(60,7),8,pink);
        Capsule(new(60,60),new(35,23),ink);Capsule(new(60,59),new(32,20),new Color("ffdcad"));
        DrawLine(new(48,50),new(48,66),ink,6,true);DrawLine(new(72,50),new(72,66),ink,6,true);
        DrawLine(new(42,43),new(53,46),ink,3,true);DrawLine(new(67,46),new(78,43),ink,3,true);
        if(Eliminated){DrawLine(new(35,35),new(86,83),ArcadeTheme.Red,7,true);DrawLine(new(86,35),new(35,83),ArcadeTheme.Red,7,true);}
        if(Hero){DrawCircle(new(109,100),19,ink);DrawCircle(new(107,97),15,new Color("294d7e"));DrawArc(new(108,76),8,Mathf.Pi,Mathf.Tau,14,ArcadeTheme.Gold,4,true);DrawCircle(new(105,70),4,ArcadeTheme.Red);}
    }
    private void DrawHead()
    {
        float scale=Math.Min(Size.X/110,Size.Y/105);
        DrawSetTransform(new((Size.X-110*scale)/2,(Size.Y-105*scale)/2),0,new(scale,scale));
        var ink=ArcadeTheme.Ink;var color=Eliminated?new Color("a4a6aa"):Suit;
        DrawLine(new(55,22),new(55,10),ink,7,true);DrawCircle(new(55,10),10,ink);DrawCircle(new(55,9),7,ArcadeTheme.Pink);
        Capsule(new(55,58),new(48,38),ink);Capsule(new(55,56),new(44,34),color);
        Capsule(new(55,64),new(35,24),ink);Capsule(new(55,63),new(32,21),Ghost?new Color("b6eceb"):new Color("ffdcad"));
        if(Eliminated)
        {
            for(int x=42;x<=68;x+=26){DrawLine(new(x-5,55),new(x+5,69),ink,4,true);DrawLine(new(x+5,55),new(x-5,69),ink,4,true);}
        }
        else {DrawLine(new(42,54),new(42,71),ink,6,true);DrawLine(new(68,54),new(68,71),ink,6,true);}
        if(Ghost)DrawArc(new(55,7),16,0,Mathf.Tau,28,ArcadeTheme.Aqua,3,true);
    }
    private void Capsule(Vector2 center,Vector2 radii,Color color)=>DrawEllipse(center,radii,color);
    private void DrawEllipse(Vector2 center,Vector2 radii,Color color)
    {
        var points=Enumerable.Range(0,48).Select(i=>center+new Vector2(Mathf.Cos(i*Mathf.Tau/48)*radii.X,Mathf.Sin(i*Mathf.Tau/48)*radii.Y)).ToArray();DrawColoredPolygon(points,color);
    }
}

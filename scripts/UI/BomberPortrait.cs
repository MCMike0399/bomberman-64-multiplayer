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
        Capsule(new(60,54),new(47,39),ink);Capsule(new(60,54),new(43,35),suit.Darkened(.16f));Capsule(new(60,50),new(41,31),suit);Capsule(new(40,34),new(8,5),new Color(1,1,1,.5f));
        DrawLine(new(60,18),new(60,10),ink,7,true);DrawCircle(new(60,8),11,ink);DrawCircle(new(60,7),8,pink);DrawCircle(new(57,4),2.5f,new Color(1,1,1,.7f));
        Capsule(new(60,60),new(35,23),ink);Capsule(new(60,59),new(32,20),new Color("ffe1b5"));
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
        DrawLine(new(55,24),new(55,12),ink,7,true);DrawCircle(new(55,10),11,ink);DrawCircle(new(55,9),8,Eliminated?new Color("c7c3c9"):ArcadeTheme.Pink);DrawCircle(new(52,6),2.5f,new Color(1,1,1,.7f));
        // Helmet: ink rim, shaded lower shell, lit crown and a single specular glint.
        Capsule(new(55,58),new(49,40),ink);Capsule(new(55,58),new(45,36),color.Darkened(.16f));Capsule(new(55,54),new(43,31),color);
        Capsule(new(34,37),new(9,5.5f),new Color(1,1,1,.5f));
        Capsule(new(55,66),new(36,25),ink);Capsule(new(55,65),new(33,22),Ghost?new Color("bdf0ee"):new Color("ffe1b5"));
        if(Eliminated)
        {
            for(int x=44;x<=66;x+=22){DrawLine(new(x-5,58),new(x+5,70),ink,4,true);DrawLine(new(x+5,58),new(x-5,70),ink,4,true);}
        }
        else
        {
            DrawLine(new(44,57),new(44,71),ink,6.5f,true);DrawLine(new(66,57),new(66,71),ink,6.5f,true);
            Capsule(new(33,73),new(5,3),new Color(ArcadeTheme.Pink,.55f));Capsule(new(77,73),new(5,3),new Color(ArcadeTheme.Pink,.55f));
        }
        if(Ghost)DrawArc(new(55,8),17,0,Mathf.Tau,28,ArcadeTheme.Aqua,3,true);
    }
    private void Capsule(Vector2 center,Vector2 radii,Color color)=>DrawEllipse(center,radii,color);
    private void DrawEllipse(Vector2 center,Vector2 radii,Color color)
    {
        var points=Enumerable.Range(0,48).Select(i=>center+new Vector2(Mathf.Cos(i*Mathf.Tau/48)*radii.X,Mathf.Sin(i*Mathf.Tau/48)*radii.Y)).ToArray();DrawColoredPolygon(points,color);
    }
}

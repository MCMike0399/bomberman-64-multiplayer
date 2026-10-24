using Godot;
namespace BlastCircuit.UI;

/// <summary>Gold, silver and bronze stamped rank coins; later places stay plain.</summary>
public partial class RankBadge : Control
{
    public int Rank {get;set;}
    public override void _Ready()=>MouseFilter=MouseFilterEnum.Ignore;
    public override void _Draw()
    {
        var center=Size/2;float r=Math.Min(Size.X,Size.Y)/2-3;
        Color face=Rank switch {1=>ArcadeTheme.Gold,2=>new Color("d9dde8"),3=>new Color("e0a46a"),_=>new Color(ArcadeTheme.Night,.9f)};
        DrawCircle(center+new Vector2(0,3),r+2,ArcadeTheme.Ink);DrawCircle(center,r+2,ArcadeTheme.Ink);DrawCircle(center,r,face);
        if(Rank<=3)DrawArc(center,r*.78f,0,Mathf.Tau,32,new Color(1,1,1,.45f),1.5f,true);
        var text=Rank.ToString();int size=(int)(r*1.15f);
        var width=ArcadeTheme.Display.GetStringSize(text,HorizontalAlignment.Left,-1,size).X;
        var at=center+new Vector2(-width/2,size*.36f);
        DrawString(ArcadeTheme.Display,at,text,HorizontalAlignment.Left,-1,size,Rank<=3?ArcadeTheme.Ink:ArcadeTheme.Cream);
    }
}

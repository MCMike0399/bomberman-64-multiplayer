using Godot;
namespace BlastCircuit.UI;

/// <summary>Stamped gold victory tokens, drawn from simple original geometry.</summary>
public partial class ResultMedals : Control
{
    public int Wins {get;set;}
    public override void _Ready()=>MouseFilter=MouseFilterEnum.Ignore;
    public override void _Draw()
    {
        for(int i=0;i<Math.Min(5,Wins);i++)
        {
            var p=new Vector2(20+i*31,23);
            DrawColoredPolygon([p+new Vector2(-9,7),p+new Vector2(-11,21),p+new Vector2(0,16),p+new Vector2(11,21),p+new Vector2(9,7)],new Color("b44e6b"));
            DrawCircle(p,15,new Color("80541c"));DrawCircle(p+new Vector2(0,-1),13,new Color("f8d665"));
            DrawArc(p+new Vector2(0,-1),10,0,Mathf.Tau,28,new Color("b68729"),1.5f,true);
            Vector2[] star=new Vector2[10];for(int j=0;j<10;j++)star[j]=p+new Vector2(0,-1)+Vector2.FromAngle(-Mathf.Pi/2+j*Mathf.Tau/10)*(j%2==0?8:3.5f);
            DrawColoredPolygon(star,new Color("fff1ab"));
        }
        if(Wins==0)DrawString(ThemeDB.FallbackFont,new(10,31),"—",HorizontalAlignment.Left,-1,24,new Color(1,1,1,.6f));
    }
}

using Godot;
namespace BlastCircuit.UI;

/// <summary>CPU strength as three small bombs: lit ones count, dim ones do not.</summary>
public partial class DifficultyPips : Control
{
    private int _level;
    public int Level {get=>_level;set{if(_level!=value){_level=value;QueueRedraw();}}}
    public override void _Ready()=>MouseFilter=MouseFilterEnum.Ignore;
    public override void _Draw()
    {
        if(Level<=0)return;
        for(int i=0;i<3;i++)
        {
            var center=new Vector2(Size.X/2+(i-1)*22,Size.Y/2+2);
            if(i<Level)ArcadeIcons.Bomb(this,center,6.5f,ArcadeTheme.Red,.6f);
            else DrawArc(center,6.5f,0,Mathf.Tau,24,new Color(ArcadeTheme.Cream,.35f),2,true);
        }
    }
}

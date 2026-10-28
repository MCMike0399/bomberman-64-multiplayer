using Godot;
namespace BlastCircuit.UI;

/// <summary>CPU strength as three small bombs: lit ones count, dim ones do not.</summary>
public partial class DifficultyPips : Button
{
    private int _level;
    public int Level {get=>_level;set{if(_level!=value){_level=value;QueueRedraw();}}}
    public override void _Ready()
    {
        foreach(var state in new[]{"normal","hover","pressed","disabled","hover_pressed"})AddThemeStyleboxOverride(state,new StyleBoxEmpty());
        AddThemeStyleboxOverride("focus",ArcadeTheme.FocusRing());
        MouseDefaultCursorShape=CursorShape.PointingHand;
    }
    public override void _Draw()
    {
        if(Level<=0)return;
        string title=Level switch{1=>"Easy",2=>"Medium",_=>"Hard"};
        var width=ArcadeTheme.Body.GetStringSize(title,HorizontalAlignment.Left,-1,14).X;
        DrawString(ArcadeTheme.Body,new((Size.X-width)/2,38),title,HorizontalAlignment.Left,-1,14,ArcadeTheme.Cream);
        for(int i=0;i<3;i++)
        {
            var center=new Vector2(Size.X/2+(i-1)*22,12);
            if(i<Level)ArcadeIcons.Bomb(this,center,6.5f,ArcadeTheme.Red,.6f);
            else DrawArc(center,6.5f,0,Mathf.Tau,24,new Color(ArcadeTheme.Cream,.35f),2,true);
        }
    }
}

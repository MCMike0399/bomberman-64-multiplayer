using Godot;
namespace BlastCircuit.UI.Menu;

/// <summary>A bobbing gold marker above the chosen seat; the native role control keeps focus.</summary>
public partial class RosterSeatPanel : PanelContainer
{
    public bool Selected {get;set;}
    private float _clock;
    public override void _Process(double delta){if(!Selected)return;_clock+=(float)delta;QueueRedraw();}
    public override void _Draw()
    {
        if(!Selected)return;
        ArcadeIcons.Chevron(this,new(Size.X/2,-4+Mathf.Sin(_clock*5)*3),10,2,ArcadeTheme.Gold);
    }
}

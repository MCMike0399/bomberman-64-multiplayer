using Godot;
namespace BlastCircuit.Testing;

public partial class ControllerMenuProbe : Node
{
    public int DownSteps {get;private set;}
    public override void _Input(InputEvent input)
    {
        if(input is InputEventAction {Pressed:true} action&&action.Action=="ui_down")DownSteps++;
    }
}

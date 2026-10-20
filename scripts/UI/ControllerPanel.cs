using Godot;
using BlastCircuit.InputDevices;
namespace BlastCircuit.UI;

/// <summary>Live input diagnostics and plain-language bindings, using the production device registry.</summary>
public partial class ControllerPanel : VBoxContainer
{
    public event Action? BackRequested;
    private Label _status=null!,_live=null!;
    private double _elapsed;
    public override void _Ready()
    {
        AddThemeConstantOverride("separation",12);
        var header=ArcadeTheme.Row(this,20);ArcadeTheme.Text(header,"CONTROLS & CONTROLLERS",32).SizeFlagsHorizontal=SizeFlags.ExpandFill;
        ArcadeTheme.Button(header,"← BACK",()=>BackRequested?.Invoke()).Name="ControlsBack";
        ArcadeTheme.Text(this,"Move a stick or press any face button. Press Menu / + / Options or Esc to leave.",18);
        _status=ArcadeTheme.Text(this,"",17);_status.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var scroll=new ScrollContainer {CustomMinimumSize=new(0,200),SizeFlagsVertical=SizeFlags.ExpandFill};AddChild(scroll);
        _live=ArcadeTheme.Text(scroll,"",18);_live.SizeFlagsHorizontal=SizeFlags.ExpandFill;_live.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        ArcadeTheme.Text(this,"KEYBOARD 1    WASD to move • J bomb • hold K to carry • L remote\n                           Space / Shift / R still work.",18);
        ArcadeTheme.Text(this,"KEYBOARD 2    Arrows to move • N bomb • hold M to carry • comma remote\n                           Enter / Ctrl / Backspace or numpad 0 / 1 / 2 also work.",18);
        ArcadeTheme.Text(this,"GAMEPADS    Left stick or D-pad to move. Same physical button positions:\nXbox A / X / B     •     Switch B / Y / A     •     PS5 Cross / Square / Circle\nLeft shoulder also carries; right shoulder also detonates. Release carry to throw.",18);
        ArcadeTheme.Text(this,"Same running speed in every direction and on every input device.\nStick drift inside the 20% deadzone is ignored. Hold carry and tap bomb to inflate.",16);
        UpdateStatus();
    }
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree())return;_elapsed+=delta;if(_elapsed<.08)return;_elapsed=0;UpdateStatus();
    }
    private void UpdateStatus()
    {
        var hub=ControllerHub.Current;
        _status.Text=$"{hub.Devices.Count} CONNECTED  •  {hub.BackendStatus}";
        _live.Text=hub.Devices.Count==0?"No mapped gamepads detected. Keyboard play remains available.":
            string.Join("\n\n",hub.Devices.Select((d,i)=>
            {
                int id=d.Device.RuntimeId;var p=ControllerProfile.Labels(d.Family);
                var buttons=Enumerable.Range(0,16).Where(b=>Input.IsJoyButtonPressed(id,(JoyButton)b)).Select(b=>ControllerProfile.ButtonName(d.Family,(JoyButton)b));
                return $"PAD {i+1}  {d.Device.Name}  •  {d.Backend}  •  {(d.Mapped?"READY":"UNMAPPED")}\n"+
                    $"Stick {Input.GetJoyAxis(id,JoyAxis.LeftX):+0.00;-0.00;0.00}, {Input.GetJoyAxis(id,JoyAxis.LeftY):+0.00;-0.00;0.00}   "+
                    $"Held: {string.Join(", ",buttons).PadRight(1,'—')}   •   Bomb {p.South} / Carry {p.West} / Remote {p.East}";
            }));
    }
}

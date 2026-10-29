using Godot;
using BlastCircuit.InputDevices;
namespace BlastCircuit.UI.Menu;

/// <summary>Live input diagnostics and plain-language bindings, using the production device registry.</summary>
public partial class ControllerPanel : VBoxContainer
{
    public event Action? BackRequested;
    private Label _status=null!,_live=null!;
    private double _elapsed;
    public override void _Ready()
    {
        AddThemeConstantOverride("separation",12);
        var header=ArcadeTheme.Row(this,20);ArcadeTheme.Heading(header,"Controls",40).SizeFlagsHorizontal=SizeFlags.ExpandFill;
        ArcadeTheme.Quiet(ArcadeTheme.Button(header,"Back",()=>BackRequested?.Invoke())).Name="ControlsBack";
        ArcadeTheme.Text(this,"Move a stick or press a face button to test it here. Esc returns. Menu, + or Options focuses Back; confirm to leave.",17,ArcadeTheme.Muted);
        _status=ArcadeTheme.Text(this,"",17,ArcadeTheme.Gold);_status.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var scroll=new ScrollContainer {CustomMinimumSize=new(0,150),SizeFlagsVertical=SizeFlags.ExpandFill};AddChild(scroll);
        _live=ArcadeTheme.Text(scroll,"",16);_live.SizeFlagsHorizontal=SizeFlags.ExpandFill;_live.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var cards=ArcadeTheme.Row(this,18);
        Card(cards,"Keyboard 1",[("Move",["W","A","S","D"]),("Bomb / pump",["J","Left click"]),("Hold to carry",["K","Right click"]),("Point throw",["Mouse"]),("Precision aim",OperatingSystem.IsWindows()?["L"]:["L","Shift"]),("Detonate",["I","R"]),("Stop kick",["O"])]);
        Card(cards,"Keyboard 2",[("Move",["Arrows"]),("Bomb",["N","Enter","Num 0"]),("Hold to carry",["M","Num 1"]),("Precision aim",["Ctrl"]),("Detonate",[",","Backspace","Num 2"]),("Stop kick",[".","Num 3"])]);
        Card(cards,"Gamepads",[("Move",["Stick","D-pad"]),("Bomb",["A","A/B","Cross"]),("Hold to carry",["LB","L","L1"]),("Point throw",["Right stick"]),("Precision aim",["RT","ZR","R2"]),("Detonate",["B","X","Circle"]),("Stop kick",["RB","R","R1"])],"Xbox, Switch and PlayStation, in that order. X, Y or Square also carries; the right shoulder stops your kicked bombs.");
        var notes=ArcadeTheme.Text(this,"Hold carry and tap bomb to ready or pump. On Switch, hold Y and tap B (A also works). While carrying, tilt the right stick or move the mouse to place the landing ring and keep walking. For fine nudges, hold aim and use the stick, D-pad or WASD. Release carry to throw.",16,ArcadeTheme.Muted);
        notes.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        if(!OperatingSystem.IsMacOS())
        {
            var usb=ArcadeTheme.Text(this,"Switch 2 Pro (USB): macOS only. Other mapped controllers remain available.",16,ArcadeTheme.Muted);
            usb.Name="Switch2UsbSupport";usb.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        }
        UpdateStatus();
    }
    private static void Card(Node parent,string title,(string Action,string[] Keys)[] rows,string? footnote=null)
    {
        var panel=new PanelContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};panel.AddThemeStyleboxOverride("panel",ArcadeTheme.Panel(new Color("28305e"),new Color("394a8f"),2,16,16));parent.AddChild(panel);
        var column=ArcadeTheme.Column(panel,9);ArcadeTheme.Heading(column,title,22);
        foreach(var (action,keys) in rows)
        {
            var row=ArcadeTheme.Row(column,6);
            var label=ArcadeTheme.Text(row,action,15);label.CustomMinimumSize=new(118,0);label.VerticalAlignment=VerticalAlignment.Center;
            foreach(var key in keys)row.AddChild(PromptBar.Keycap(key,13));
        }
        if(footnote!=null){var note=ArcadeTheme.Text(column,footnote,13,ArcadeTheme.Muted);note.AutowrapMode=TextServer.AutowrapMode.WordSmart;note.CustomMinimumSize=new(300,0);}
    }
    public override void _Process(double delta)
    {
        if(!IsVisibleInTree())return;_elapsed+=delta;if(_elapsed<.08)return;_elapsed=0;UpdateStatus();
    }
    private void UpdateStatus()
    {
        var hub=ControllerHub.Current;
        _status.Text=$"{hub.Devices.Count} {(hub.Devices.Count==1?"controller":"controllers")} connected. {hub.BackendStatus}";
        _live.Text=hub.Devices.Count==0?"No mapped gamepads found. Keyboard play is still available.":
            string.Join("\n\n",hub.Devices.Select((d,i)=>
            {
                int id=d.Device.RuntimeId;string Label(JoyButton button)=>ControllerProfile.ButtonName(d.Family,button);
                var buttons=Enumerable.Range(0,16).Where(b=>Input.IsJoyButtonPressed(id,(JoyButton)b)).Select(b=>ControllerProfile.ButtonName(d.Family,(JoyButton)b));
                return $"Pad {i+1}: {d.Device.Name}, {d.Backend}, {(d.Mapped?"ready":"unmapped")}\n"+
                    $"Stick {Input.GetJoyAxis(id,JoyAxis.LeftX):+0.00;-0.00;0.00}, {Input.GetJoyAxis(id,JoyAxis.LeftY):+0.00;-0.00;0.00}    "+
                    $"Aim {ControllerProfile.AimName(d.Family)} {Input.GetJoyAxis(id,JoyAxis.TriggerRight):0.00}    Held: {string.Join(", ",buttons).PadRight(1,'—')}    Bomb {ControllerProfile.BombName(d.Family)}, carry {Label(JoyButton.LeftShoulder)} / {Label(ControllerProfile.CarryButton(d.Family))}, detonate {Label(ControllerProfile.RemoteButton(d.Family))}";
            }));
    }
}

using Godot;
namespace BlastCircuit.UI;

/// <summary>A native focusable arcade selector: vertical navigation, horizontal value changes.</summary>
public partial class ArcadeRuleDial : Button
{
    public string Caption {get;set;}="";
    public Func<string>? ReadValue {get;set;}
    public Action<int>? ChangeValue {get;set;}
    private Label _caption=null!,_value=null!,_left=null!,_right=null!;
    private int _activationDirection=1;
    public override void _Ready()
    {
        CustomMinimumSize=new(0,58);MouseDefaultCursorShape=CursorShape.PointingHand;
        foreach(var state in new[]{"normal","hover","pressed","focus","disabled"})AddThemeStyleboxOverride(state,new StyleBoxEmpty());
        var row=new HBoxContainer {MouseFilter=MouseFilterEnum.Ignore};AddChild(row);row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);row.OffsetLeft=24;row.OffsetRight=-12;
        _caption=ArcadeTheme.Text(row,Caption,20);_caption.SizeFlagsHorizontal=SizeFlags.ExpandFill;_caption.VerticalAlignment=VerticalAlignment.Center;
        _left=ArcadeTheme.Text(row,"◀",18,ArcadeTheme.Gold);_left.VerticalAlignment=VerticalAlignment.Center;
        _value=ArcadeTheme.Text(row,"",25);_value.CustomMinimumSize=new(148,0);_value.HorizontalAlignment=HorizontalAlignment.Center;_value.VerticalAlignment=VerticalAlignment.Center;
        _right=ArcadeTheme.Text(row,"▶",18,ArcadeTheme.Gold);_right.VerticalAlignment=VerticalAlignment.Center;
        Pressed+=()=>{ChangeValue?.Invoke(_activationDirection);_activationDirection=1;};
        GuiInput+=input=>
        {
            if(Disabled)return;
            if(input.IsActionPressed("ui_left")){ChangeValue?.Invoke(-1);AcceptEvent();}
            else if(input.IsActionPressed("ui_right")){ChangeValue?.Invoke(1);AcceptEvent();}
            else if(input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left})_activationDirection=_left.GetGlobalRect().Grow(12).HasPoint(GetGlobalMousePosition())?-1:1;
        };
    }
    public override void _Process(double delta)
    {
        _caption.Text=Caption;_value.Text=ReadValue?.Invoke()??"";
        bool selected=HasFocus()||IsHovered();
        _caption.Modulate=_value.Modulate=Disabled?new Color(1,1,1,.38f):Colors.White;
        _left.Modulate=_right.Modulate=selected&&!Disabled?Colors.White:new Color(1,1,1,.32f);
        QueueRedraw();
    }
    public override void _Draw()
    {
        bool selected=HasFocus()||IsHovered();
        if(selected&&!Disabled)
        {
            DrawStyleBox(ArcadeTheme.Panel(new Color(.13f,.19f,.34f,.78f),ArcadeTheme.Gold,2,2,0),new Rect2(Vector2.Zero,Size));
            DrawColoredPolygon([new(4,Size.Y/2),new(-8,Size.Y/2-9),new(-8,Size.Y/2+9)],ArcadeTheme.Red);
        }
        else DrawLine(new(24,Size.Y-1),new(Size.X-12,Size.Y-1),new Color(1,1,1,.14f),1);
    }
}

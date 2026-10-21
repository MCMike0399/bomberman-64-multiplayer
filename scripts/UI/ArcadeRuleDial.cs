using Godot;
namespace BlastCircuit.UI;

/// <summary>A native focusable arcade selector: vertical navigation, horizontal value changes.</summary>
public partial class ArcadeRuleDial : Button
{
    public string Caption {get;set;}="";
    public Func<string>? ReadValue {get;set;}
    public Action<int>? ChangeValue {get;set;}
    private Label _caption=null!,_value=null!;
    private Control _left=null!,_right=null!;
    private int _activationDirection=1;
    private float _nudge;
    private bool _wasSelected;
    public float ValueWidth {get;set;}=190;
    private const float ArrowWidth=34;
    public override void _Ready()
    {
        CustomMinimumSize=new(0,58);MouseDefaultCursorShape=CursorShape.PointingHand;
        foreach(var state in new[]{"normal","hover","pressed","focus","disabled","hover_pressed"})AddThemeStyleboxOverride(state,new StyleBoxEmpty());
        var row=new HBoxContainer {MouseFilter=MouseFilterEnum.Ignore};AddChild(row);row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);row.OffsetLeft=22;row.OffsetRight=-14;
        _caption=ArcadeTheme.Text(row,Caption,19);_caption.SizeFlagsHorizontal=SizeFlags.ExpandFill;_caption.VerticalAlignment=VerticalAlignment.Center;
        _left=new Control {CustomMinimumSize=new(ArrowWidth,0),MouseFilter=MouseFilterEnum.Ignore};row.AddChild(_left);
        _value=ArcadeTheme.Text(row,"",22,null,true);_value.CustomMinimumSize=new(ValueWidth,0);_value.HorizontalAlignment=HorizontalAlignment.Center;_value.VerticalAlignment=VerticalAlignment.Center;
        _value.ClipText=true;_value.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        _right=new Control {CustomMinimumSize=new(ArrowWidth,0),MouseFilter=MouseFilterEnum.Ignore};row.AddChild(_right);
        Pressed+=()=>{Change(_activationDirection);_activationDirection=1;};
        GuiInput+=input=>
        {
            if(Disabled)return;
            if(input.IsActionPressed("ui_left")){Change(-1);AcceptEvent();}
            else if(input.IsActionPressed("ui_right")){Change(1);AcceptEvent();}
            else if(input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left})_activationDirection=_left.GetGlobalRect().Grow(12).HasPoint(GetGlobalMousePosition())?-1:1;
        };
    }
    private void Change(int direction){ChangeValue?.Invoke(direction);_nudge=direction;}
    public override void _Process(double delta)
    {
        _caption.Text=Caption;_value.Text=ReadValue?.Invoke()??"";
        bool selected=(HasFocus()||IsHovered())&&!Disabled;
        if(selected!=_wasSelected)
        {
            // On the lit sand key the text switches to ink; elsewhere it floats on the stage with an outline.
            _wasSelected=selected;
            foreach(var label in new[]{_caption,_value})
            {
                label.AddThemeColorOverride("font_color",selected?ArcadeTheme.Ink:ArcadeTheme.Cream);
                label.AddThemeConstantOverride("outline_size",selected?0:label==_value?4:3);
                label.AddThemeColorOverride("font_shadow_color",selected?new Color(0,0,0,0):ArcadeTheme.Ink);
            }
        }
        Modulate=Disabled?new Color(1,1,1,.4f):Colors.White;
        _nudge=Mathf.MoveToward(_nudge,0,(float)delta*7);
        QueueRedraw();
    }
    public override void _Draw()
    {
        bool selected=(HasFocus()||IsHovered())&&!Disabled;
        var face=new Rect2(new Vector2(4,4),Size-new Vector2(8,10));
        if(selected)DrawStyleBox(ArcadeTheme.Key(ArcadeTheme.Sand,false,false,14,0),face);
        else DrawLine(new(22,Size.Y-2),new(Size.X-14,Size.Y-2),new Color(ArcadeTheme.Cream,.16f),2);
        float y=face.Position.Y+face.Size.Y/2;
        float leftX=_left.Position.X+22+ArrowWidth/2,rightX=_right.Position.X+22+ArrowWidth/2;
        var fill=selected?ArcadeTheme.Red:new Color(ArcadeTheme.Gold,.75f);
        float push=Mathf.Sin(Mathf.Abs(_nudge)*Mathf.Pi)*6;
        ArcadeIcons.Chevron(this,new(leftX-(_nudge<0?push:0),y),selected?11:9,-1,fill);
        ArcadeIcons.Chevron(this,new(rightX+(_nudge>0?push:0),y),selected?11:9,1,fill);
    }
}
